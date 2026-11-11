using System.Collections.Generic;
using System.Linq;
using System.Text;
using GoogleTestAdapter.Model;

namespace GoogleTestAdapter.Runners
{

    /// <summary>
    /// The command lines running benchmarks of a Google Benchmark executable, writing their results to a JSON file
    /// </summary>
    public class BenchmarkCommandLineGenerator
    {
        public class Args
        {
            public IList<TestCase> TestCases { get; }
            public string CommandLine { get; }
            /// <summary>The (quoted) --benchmark_filter argument, null if all benchmarks are run</summary>
            public string Filter { get; }

            internal Args(IList<TestCase> testCases, string commandLine, string filter)
            {
                TestCases = testCases;
                CommandLine = commandLine;
                Filter = filter;
            }
        }

        public const string FilterOption = "--benchmark_filter=";
        public const string DryRunOption = "--benchmark_dry_run";
        // the name of the flag in executables of Google Benchmark 1.9.1 or later
        public const string DryRunMarker = "benchmark_dry_run";

        // a filter not matching any benchmark, for running the executable for its exit code only
        private const string NoBenchmarksFilter = "^$";

        private readonly IList<TestCase> _testCasesToRun;
        private readonly int _lengthOfExecutableString;
        private readonly string _userParameters;
        private readonly string _jsonFile;
        private readonly bool _isDryRun;

        public BenchmarkCommandLineGenerator(IEnumerable<TestCase> testCasesToRun, int lengthOfExecutableString,
            string userParameters, string jsonFile, bool isDryRun)
        {
            _testCasesToRun = testCasesToRun.ToList();
            _lengthOfExecutableString = lengthOfExecutableString;
            _userParameters = userParameters ?? "";
            _jsonFile = jsonFile;
            _isDryRun = isDryRun;
        }

        public IList<Args> GetCommandLines()
        {
            string baseCommandLine = QuoteArgument("--benchmark_out=" + _jsonFile) + " --benchmark_out_format=json";
            if (_isDryRun)
                baseCommandLine += " " + DryRunOption;
            // last, so that users can override GTA's options
            string userParameters = _userParameters.Length == 0 ? "" : " " + _userParameters;

            List<TestCase> benchmarks = _testCasesToRun.Where(tc => !tc.IsExitCodeTestCase).ToList();
            if (benchmarks.Count == 0)
            {
                string noBenchmarks = QuoteArgument(FilterOption + NoBenchmarksFilter);
                return new List<Args> { new Args(_testCasesToRun.ToList(), $"{baseCommandLine} {noBenchmarks}{userParameters}", noBenchmarks) };
            }

            int nrOfBenchmarksOfExecutable = benchmarks
                .SelectMany(tc => tc.Properties.OfType<TestCaseMetaDataProperty>())
                .Select(p => p.NrOfTestCasesInExecutable)
                .DefaultIfEmpty(int.MaxValue)
                .Max();
            if (benchmarks.Count >= nrOfBenchmarksOfExecutable)
                return new List<Args> { new Args(benchmarks, baseCommandLine + userParameters, null) };

            var commandLines = new List<Args>();
            int maxLengthOfNames = CommandLineGenerator.MaxCommandLength - _lengthOfExecutableString - baseCommandLine.Length
                - userParameters.Length - FilterOption.Length - "\"^()$\" ".Length - 1;
            var testCases = new List<TestCase>();
            var names = new StringBuilder();
            foreach (TestCase benchmark in benchmarks)
            {
                string name = EscapeRegex(benchmark.FullyQualifiedName);
                if (testCases.Count > 0 && names.Length + 1 + name.Length > maxLengthOfNames)
                {
                    commandLines.Add(CreateArgs(baseCommandLine, userParameters, testCases, names.ToString()));
                    testCases = new List<TestCase>();
                    names.Clear();
                }
                if (names.Length > 0)
                    names.Append('|');
                names.Append(name);
                testCases.Add(benchmark);
            }
            commandLines.Add(CreateArgs(baseCommandLine, userParameters, testCases, names.ToString()));
            return commandLines;
        }

        private static Args CreateArgs(string baseCommandLine, string userParameters, List<TestCase> testCases, string names)
        {
            string filter = QuoteArgument($"{FilterOption}^({names})$");
            return new Args(testCases, $"{baseCommandLine} {filter}{userParameters}", filter);
        }

        /// <summary>
        /// Escapes the special characters of POSIX extended regular expressions, which Google Benchmark uses for
        /// --benchmark_filter (std::regex with std::regex_constants::extended); other characters must not be
        /// escaped (e.g. "\/" and "\&lt;" are invalid)
        /// </summary>
        public static string EscapeRegex(string name)
        {
            var escaped = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (@"\.[](){}*+?|^$".IndexOf(c) >= 0)
                    escaped.Append('\\');
                escaped.Append(c);
            }
            return escaped.ToString();
        }

        /// <summary>
        /// Quotes the argument for CommandLineToArgvW() and the C runtime: backslashes are literal unless they precede
        /// a quote
        /// </summary>
        public static string QuoteArgument(string argument)
        {
            var quoted = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    quoted.Append('\\', backslashes * 2 + 1);
                }
                else
                {
                    quoted.Append('\\', backslashes);
                }
                quoted.Append(c);
                backslashes = 0;
            }
            quoted.Append('\\', backslashes * 2);
            quoted.Append('"');
            return quoted.ToString();
        }
    }

}
