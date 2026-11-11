using System;
using System.Collections.Generic;
using System.Linq;
using GoogleTestAdapter.Common;
using GoogleTestAdapter.DiaResolver;
using GoogleTestAdapter.Settings;

namespace GoogleTestAdapter.TestCases
{

    /// <summary>
    /// Finds the source locations of Google Benchmark benchmarks in the executable's debug symbols. A benchmark's
    /// name starts with the name of its function (e.g. "BM_Foo/8/threads:2" for BENCHMARK(BM_Foo)->Arg(8)->Threads(2),
    /// "BM_Foo&lt;int&gt;" for BENCHMARK_TEMPLATE(BM_Foo, int)) or with its fixture and method ("Fixture/Method" for
    /// BENCHMARK_F(Fixture, Method), defined as method BenchmarkCase() of class Fixture_Method_Benchmark). Benchmarks
    /// with custom names (Benchmark::Name()) or registered with RegisterBenchmark() have no location.
    /// </summary>
    public sealed class BenchmarkLocationResolver : IDisposable
    {
        private readonly string _executable;
        private readonly ILogger _logger;
        private readonly IDiaResolver _diaResolver;

        // benchmarks of the same function or fixture method share the location
        private readonly Dictionary<string, TestCaseLocation> _locationsByPrefix = new Dictionary<string, TestCaseLocation>(StringComparer.Ordinal);

        public TestCaseLocation MainMethodLocation { get; }

        /// <summary>Whether the executable's debug symbols have been loaded</summary>
        public bool HasSymbols => _diaResolver != null;

        public BenchmarkLocationResolver(string executable, IDiaResolverFactory diaResolverFactory, SettingsWrapper settings, ILogger logger)
        {
            _executable = executable;
            _logger = logger;

            if (!settings.ParseSymbolInformation)
                return;

            string pdb = PdbLocator.FindPdbFile(executable, settings.GetPathExtension(executable), logger);
            if (pdb == null)
            {
                logger.LogWarning($"No .pdb file found for benchmark executable '{executable}', benchmarks will have neither source locations nor namespaces (make sure the executable is built with debug information, e.g. linker option /DEBUG)");
                return;
            }

            try
            {
                _diaResolver = diaResolverFactory.Create(executable, pdb, logger);
                SourceFileLocation main = _diaResolver.GetFunctions("main").FirstOrDefault();
                if (main != null)
                    MainMethodLocation = new TestCaseLocation(main.Symbol, main.Sourcefile, main.Line) { Namespace = "" };
            }
            catch (Exception e)
            {
                logger.DebugError($"Exception while loading the debug symbols of '{executable}':{Environment.NewLine}{e}");
            }
        }

        /// <param name="isFixtureBenchmark">whether the benchmark is a method of a fixture</param>
        /// <returns>The location of the benchmark's function or fixture method, or null if it is not found</returns>
        public TestCaseLocation FindBenchmarkLocation(string benchmarkName, out bool isFixtureBenchmark)
        {
            isFixtureBenchmark = false;
            if (_diaResolver == null)
                return null;

            IList<string> parts = BenchmarkNames.Split(benchmarkName);
            if (parts.Count >= 2)
            {
                TestCaseLocation location = FindFunction(BenchmarkNames.GetFixtureClass(parts[0], parts[1]), isFixtureClass: true);
                if (location != null)
                {
                    isFixtureBenchmark = true;
                    return location;
                }
            }
            return FindFunction(parts[0], isFixtureClass: false);
        }

        private TestCaseLocation FindFunction(string name, bool isFixtureClass)
        {
            string qualifiedName = isFixtureClass ? name + "::" + GoogleBenchmarkConstants.FixtureMethod : name;
            if (_locationsByPrefix.TryGetValue(qualifiedName, out TestCaseLocation location))
                return location;

            try
            {
                // the function might be defined in a namespace the benchmark's name does not mention
                SourceFileLocation symbol = _diaResolver.GetFunctions("*" + qualifiedName)
                    .Where(s => s.Symbol == qualifiedName || s.Symbol.EndsWith("::" + qualifiedName, StringComparison.Ordinal))
                    .OrderBy(s => s.Symbol.Length)
                    .FirstOrDefault();
                if (symbol != null)
                {
                    // the namespace of the function or of the fixture class
                    string function = isFixtureClass
                        ? symbol.Symbol.Substring(0, symbol.Symbol.Length - GoogleBenchmarkConstants.FixtureMethod.Length - 2)
                        : symbol.Symbol;
                    location = new TestCaseLocation(symbol.Symbol, symbol.Sourcefile, symbol.Line)
                    {
                        Namespace = BenchmarkNames.GetNamespace(function)
                    };
                }
            }
            catch (Exception e)
            {
                _logger.DebugError($"Exception while resolving the location of '{qualifiedName}' in '{_executable}':{Environment.NewLine}{e}");
            }

            _locationsByPrefix.Add(qualifiedName, location);
            return location;
        }

        public void Dispose()
        {
            _diaResolver?.Dispose();
        }
    }

}
