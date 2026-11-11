// This file has been modified by Microsoft on 7/2017.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using GoogleTestAdapter.Common;
using GoogleTestAdapter.DiaResolver;
using GoogleTestAdapter.Framework;
using GoogleTestAdapter.Helpers;
using GoogleTestAdapter.Model;
using GoogleTestAdapter.ProcessExecution;
using GoogleTestAdapter.ProcessExecution.Contracts;
using GoogleTestAdapter.Settings;
using GoogleTestAdapter.TestCases;

namespace GoogleTestAdapter
{
    public class GoogleTestDiscoverer
    {
        public const string GoogleTestIndicator = ".is_google_test";
        public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(3);

        private readonly ILogger _logger;
        private readonly SettingsWrapper _settings;
        private readonly IDiaResolverFactory _diaResolverFactory;
        private readonly IProcessExecutorFactory _processExecutorFactory;

        public GoogleTestDiscoverer(ILogger logger, SettingsWrapper settings, IProcessExecutorFactory processExecutorFactory = null, IDiaResolverFactory diaResolverFactory = null)
        {
            _logger = logger;
            _settings = settings;
            _processExecutorFactory = processExecutorFactory ?? new ProcessExecutorFactory();
            _diaResolverFactory = diaResolverFactory ?? DefaultDiaResolverFactory.Instance;
        }

        public void DiscoverTests(IEnumerable<string> executables, ITestFrameworkReporter reporter)
        {
            var discoveryActions = executables
                .Select(e => (Action)(() => DiscoverTests(e, reporter, _settings.Clone(), _logger, _diaResolverFactory, _processExecutorFactory)))
                .ToArray();
            Utils.SpawnAndWait(discoveryActions);
        }

        private static void DiscoverTests(string executable, ITestFrameworkReporter reporter, SettingsWrapper settings, ILogger logger, IDiaResolverFactory diaResolverFactory, IProcessExecutorFactory processExecutorFactory)
        {
            settings.ExecuteWithSettingsForExecutable(executable, logger, () =>
            {
                if (!VerifyExecutableTrust(executable, settings, logger))
                    return;
                string indicator = GetGoogleTestExecutableIndicator(executable, settings.TestDiscoveryRegex, logger);
                if (indicator == null)
                    return;
                TestFramework framework = GetTestFramework(executable, indicator, logger);

                // discovery of executables with many tests takes a while, and their tests are only reported at the end
                logger.LogInfo(framework == TestFramework.GoogleBenchmark
                    ? "Discovering benchmarks in executable " + executable
                    : "Discovering tests in executable " + executable);

                int nrOfTestCases = 0;
                void ReportTestCases(TestCase testCase)
                {
                    reporter.ReportTestsFound(testCase.Yield());
                    logger.VerboseInfo("Added testcase " + testCase.DisplayName);
                    nrOfTestCases++;
                }

                var factory = new TestCaseFactory(executable, logger, settings, diaResolverFactory, processExecutorFactory, framework);
                factory.CreateTestCases(ReportTestCases);
                logger.LogInfo("Found " + nrOfTestCases + " tests in executable " + executable);
            });
        }

        /// <summary>
        /// The tests of the executable, with the settings for the executable (for callers which do not reference
        /// GoogleTestAdapter.DiaResolver, e.g. GTA's automation object)
        /// </summary>
        public static IList<TestCase> GetTestsFromExecutable(string executable, SettingsWrapper settings, ILogger logger)
        {
            return new GoogleTestDiscoverer(logger, settings).GetTestsFromExecutable(executable);
        }

        public IList<TestCase> GetTestsFromExecutable(string executable)
        {
            TestFramework framework = File.Exists(executable)
                ? GetTestFramework(executable, GetGoogleTestExecutableIndicator(executable, _settings.TestDiscoveryRegex, _logger), _logger)
                : TestFramework.GoogleTest;
            var factory = new TestCaseFactory(executable, _logger, _settings, _diaResolverFactory, _processExecutorFactory, framework);
            IList<TestCase> testCases = factory.CreateTestCases();

            foreach (TestCase testCase in testCases)
            {
                _logger.VerboseInfo("Added testcase " + testCase.DisplayName);
            }
            _logger.LogInfo("Found " + testCases.Count + " tests in executable " + executable);

            return testCases;
        }

        public static bool IsGoogleTestExecutable(string executable, string customRegex, ILogger logger)
        {
            return GetGoogleTestExecutableIndicator(executable, customRegex, logger) != null;
        }

        // the indicators returned by GetGoogleTestExecutableIndicator()
        public const string IndicatorFile = "IndicatorFile";
        public const string IndicatorGoogleTestDll = "GoogleTestDll";
        public const string IndicatorGoogleTestFunctions = "GoogleTestFunctions";
        public const string IndicatorGoogleTestMarkers = "GoogleTestMarkers";
        public const string IndicatorTestDiscoveryRegex = "TestDiscoveryRegex";
        public const string IndicatorGoogleBenchmarkDll = "GoogleBenchmarkDll";
        public const string IndicatorGoogleBenchmarkFunctions = "GoogleBenchmarkFunctions";
        public const string IndicatorGoogleBenchmarkMarkers = "GoogleBenchmarkMarkers";

        private static readonly string[] GoogleBenchmarkIndicators =
            { IndicatorGoogleBenchmarkDll, IndicatorGoogleBenchmarkFunctions, IndicatorGoogleBenchmarkMarkers };

        /// <returns>What makes the executable a Google Test or Google Benchmark executable (one of the Indicator*
        /// constants), or null if it is neither</returns>
        public static string GetGoogleTestExecutableIndicator(string executable, string customRegex, ILogger logger)
        {
            string googleTestIndicatorFile = $"{executable}{GoogleTestIndicator}";
            if (File.Exists(googleTestIndicatorFile))
            {
                logger.DebugInfo($"Google Test indicator file found for executable {executable} ({googleTestIndicatorFile})");
                return IndicatorFile;
            }

            if (string.IsNullOrWhiteSpace(customRegex))
            {
                string indicator = FindTestFrameworkIndicator(executable, logger);
                if (indicator != null)
                {
                    logger.DebugInfo(IsGoogleBenchmarkIndicator(indicator)
                        ? $"Google Benchmark indicators found in executable {executable}"
                        : $"Google Test indicators found in executable {executable}");
                    return indicator;
                }
            }
            else
            {
                if (SafeMatches(executable, customRegex, logger))
                {
                    logger.DebugInfo($"Custom regex '{customRegex}' matches executable '{executable}'");
                    return IndicatorTestDiscoveryRegex;
                }
            }

            logger.DebugInfo($"File does not seem to be Google Test executable: '{executable}'");
            return null;
        }

        // Google Test wins for executables containing both tests and benchmarks
        private static string FindTestFrameworkIndicator(string executable, ILogger logger)
        {
            List<string> imports = PeParser.ParseImports(executable, logger);
            if (imports.Any(IsGoogleTestDll))
                return IndicatorGoogleTestDll;
            if (PeParser.FindImportedFunction(executable, IsGoogleTestFunction, logger))
                return IndicatorGoogleTestFunctions;

            byte[] content = File.ReadAllBytes(executable);
            if (Utils.BinaryContainsStrings(content, Encoding.ASCII, GoogleTestConstants.GoogleTestExecutableMarkers))
                return IndicatorGoogleTestMarkers;

            if (imports.Any(IsGoogleBenchmarkDll))
                return IndicatorGoogleBenchmarkDll;
            if (PeParser.FindImportedFunction(executable, IsGoogleBenchmarkFunction, logger))
                return IndicatorGoogleBenchmarkFunctions;
            if (Utils.BinaryContainsStrings(content, Encoding.ASCII, GoogleBenchmarkConstants.GoogleBenchmarkExecutableMarkers))
                return IndicatorGoogleBenchmarkMarkers;

            return null;
        }

        public static bool IsGoogleBenchmarkIndicator(string indicator)
        {
            return GoogleBenchmarkIndicators.Contains(indicator);
        }

        /// <param name="indicator">the result of <see cref="GetGoogleTestExecutableIndicator"/> for the executable</param>
        /// <returns>The test framework of a test executable; executables made test executables by an indicator file
        /// or the test discovery regex are inspected for it</returns>
        public static TestFramework GetTestFramework(string executable, string indicator, ILogger logger)
        {
            if (indicator == IndicatorFile || indicator == IndicatorTestDiscoveryRegex)
                indicator = FindTestFrameworkIndicator(executable, logger);

            return IsGoogleBenchmarkIndicator(indicator) ? TestFramework.GoogleBenchmark : TestFramework.GoogleTest;
        }

        public static bool IsGoogleBenchmarkDll(string import)
        {
            return GoogleBenchmarkConstants.GoogleBenchmarkDllMarkers.Contains(import, StringComparer.OrdinalIgnoreCase);
        }

        // scopes of Google Benchmark's core: every executable linking Google Benchmark as DLL uses some of them (e.g.
        // benchmark::internal::RegisterBenchmarkInternal() for BENCHMARK(), benchmark::State for the benchmark loop)
        private static readonly string[] GoogleBenchmarkScopes = { "benchmark::internal::", "benchmark::State::" };

        private static readonly Regex MsvcGoogleBenchmarkFunctionRegex = new Regex(
            @"@(?:internal|State)@benchmark@@|^\?\?[0-9]State@benchmark@@", RegexOptions.Compiled);

        private static readonly Regex ItaniumGoogleBenchmarkFunctionRegex = new Regex(
            @"^_?_ZNK?9benchmark(?:8internal|5State)(?=[0-9CDE])", RegexOptions.Compiled);

        /// <summary>
        /// Executables linking Google Benchmark as DLL, whatever its name, import functions of benchmark::internal or
        /// benchmark::State (see <see cref="IsGoogleTestFunction"/>).
        /// </summary>
        public static bool IsGoogleBenchmarkFunction(string importedFunction)
        {
            string qualifiedName = SymbolUndecorator.UndecorateNameOnly(importedFunction);
            if (qualifiedName != null)
                return GoogleBenchmarkScopes.Any(scope => qualifiedName.StartsWith(scope, StringComparison.Ordinal));

            return MsvcGoogleBenchmarkFunctionRegex.IsMatch(importedFunction)
                || ItaniumGoogleBenchmarkFunctionRegex.IsMatch(importedFunction);
        }

        public static bool IsGoogleTestDll(string import)
        {
            return GoogleTestConstants.GoogleTestDllMarkers.Contains(import, StringComparer.OrdinalIgnoreCase);
        }

        // scopes of Google Test's core: every test executable linking Google Test as DLL uses some of them (e.g.
        // testing::internal::MakeAndRegisterTestInfo() for TEST(), testing::UnitTest for TEST_P())
        private static readonly string[] GoogleTestScopes = { "testing::internal::", "testing::UnitTest::", "testing::Test::" };

        // fallback if a name can not be undecorated: MSVC mangles names from the innermost to the outermost scope, e.g.
        // "?GetInstance@UnitTest@testing@@..."; constructors and destructors are mangled as "??0Test@testing@@...", "??1..."
        private static readonly Regex MsvcGoogleTestFunctionRegex = new Regex(
            @"@(?:internal|UnitTest|Test)@testing@@|^\?\?[0-9]Test@testing@@", RegexOptions.Compiled);

        // Itanium mangling (MinGW, clang), which dbghelp can not undecorate, names the outermost scope first, e.g.
        // "_ZN7testing8UnitTest11GetInstanceEv"; names are prefixed with their length
        private static readonly Regex ItaniumGoogleTestFunctionRegex = new Regex(
            @"^_?_ZNK?7testing(?:8internal|8UnitTest|4Test)(?=[0-9CDE])", RegexOptions.Compiled);

        /// <summary>
        /// Test executables linking Google Test (or Google Mock) as DLL, whatever its name, import functions of
        /// testing::internal, testing::UnitTest, or testing::Test.
        /// </summary>
        public static bool IsGoogleTestFunction(string importedFunction)
        {
            string qualifiedName = SymbolUndecorator.UndecorateNameOnly(importedFunction);
            if (qualifiedName != null)
                return GoogleTestScopes.Any(scope => qualifiedName.StartsWith(scope, StringComparison.Ordinal));

            return MsvcGoogleTestFunctionRegex.IsMatch(importedFunction)
                || ItaniumGoogleTestFunctionRegex.IsMatch(importedFunction);
        }

        private static bool SafeMatches(string executable, string regex, ILogger logger)
        {
            bool matches = false;
            try
            {
                matches = Regex.IsMatch(executable, regex, RegexOptions.None, RegexTimeout);
            }
            catch (ArgumentException e)
            {
                logger.LogError($"Regex '{regex}' can not be parsed: {e.Message}");
            }
            catch (RegexMatchTimeoutException e)
            {
                logger.LogError($"Regex '{regex}' timed out: {e.Message}");
            }
            return matches;
        }

        public static bool VerifyExecutableTrust(string executable, SettingsWrapper settings, ILogger logger)
        {
            if (settings.SkipOriginCheck)
                return true;

            var zone = Zone.CreateFromUrl(executable);
            if (IsTrustedZone(zone.SecurityZone))
                return true;

            string skipOriginCheck = $"set option '{SettingsWrapper.OptionSkipOriginCheck}' (Tools/Options/Google Test Adapter/{SettingsWrapper.PageGeneralName}/{SettingsWrapper.CategorySecurityName}; can not be set via settings files)";
            if (IsOnNetworkDrive(executable))
            {
                // wherever the executable has been built: others might replace it
                logger.LogError($"Executable {executable} is located on a network drive (security zone '{zone.SecurityZone}') and was blocked to help protect this computer. " +
                                $"If you trust the executable, {skipOriginCheck}, or build it to a local drive.");
            }
            else
            {
                logger.LogError($"Executable {executable} came from another computer and was blocked to help protect this computer. " +
                                $"If you trust the executable, {skipOriginCheck} or, if the executable has been downloaded, unblock it via the file's properties.");
            }
            return false;
        }

        public static bool IsTrustedZone(System.Security.SecurityZone zone)
        {
            return zone == System.Security.SecurityZone.MyComputer;
        }

        // UNC paths and mapped network drives (the file itself is not accessed, which might take long for unreachable hosts)
        public static bool IsOnNetworkDrive(string executable)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(executable));
                return root.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(root).DriveType == DriveType.Network;
            }
            catch (Exception)
            {
                return false;
            }
        }

    }

}