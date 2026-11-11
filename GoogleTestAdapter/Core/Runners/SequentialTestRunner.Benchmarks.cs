using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using GoogleTestAdapter.DiaResolver;
using GoogleTestAdapter.Framework;
using GoogleTestAdapter.Helpers;
using GoogleTestAdapter.Model;
using GoogleTestAdapter.ProcessExecution.Contracts;
using GoogleTestAdapter.Scheduling;
using GoogleTestAdapter.TestResults;

namespace GoogleTestAdapter.Runners
{

    /// <summary>
    /// Runs the benchmarks of Google Benchmark executables: their results are read from the JSON file written by
    /// Google Benchmark, and reported as soon as they are complete (i.e., when the next benchmark appears in the file,
    /// since Google Benchmark writes all runs of a benchmark at once). Benchmarks run in the order Google Benchmark
    /// lists them, so the first benchmark without results is the one which has crashed or timed out.
    /// </summary>
    public partial class SequentialTestRunner
    {
        private bool IsBenchmarkExecutable(string executable, IEnumerable<TestCase> testCases)
        {
            List<TestCase> testCasesAsList = testCases.ToList();
            if (testCasesAsList.Any(tc => tc.IsBenchmark))
                return true;

            // the exit code test case of a benchmark executable is not marked as benchmark
            return testCasesAsList.Count > 0 && testCasesAsList.All(tc => tc.IsExitCodeTestCase) && File.Exists(executable)
                && GoogleTestDiscoverer.GetTestFramework(executable,
                    GoogleTestDiscoverer.GetGoogleTestExecutableIndicator(executable, _settings.TestDiscoveryRegex, _logger),
                    _logger) == TestFramework.GoogleBenchmark;
        }

        private void RunBenchmarksFromExecutable(string executable, string workingDir, List<TestCase> testCasesToRun,
            string userParameters, IDictionary<string, string> environmentVariables, bool isBeingDebugged,
            IDebuggedProcessExecutorFactory processExecutorFactory)
        {
            bool isDryRun = _settings.BenchmarkDryRun && SupportsDryRun(executable);
            string jsonFile = Path.GetTempFileName();
            try
            {
                var generator = new BenchmarkCommandLineGenerator(testCasesToRun, executable.Length, userParameters, jsonFile, isDryRun);
                foreach (BenchmarkCommandLineGenerator.Args arguments in generator.GetCommandLines())
                {
                    if (_canceled)
                        break;

                    List<TestCase> benchmarksNotRun = RunBenchmarks(executable, workingDir, arguments, userParameters,
                        environmentVariables, isBeingDebugged, processExecutorFactory, jsonFile, isDryRun);

                    // as with tests, a benchmark timing out does not affect the other benchmarks
                    if (benchmarksNotRun.Count > 0 && !_canceled)
                    {
                        _logger.DebugInfo($"{_threadName}Running the remaining {benchmarksNotRun.Count} benchmark(s) of executable '{executable}'");
                        RunBenchmarksFromExecutable(executable, workingDir, benchmarksNotRun, userParameters,
                            environmentVariables, isBeingDebugged, processExecutorFactory);
                    }
                }
            }
            finally
            {
                try
                {
                    File.Delete(jsonFile);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    _logger.DebugWarning($"{_threadName}Could not delete benchmark result file '{jsonFile}': {e.Message}");
                }
            }
        }

        // --benchmark_dry_run is supported by Google Benchmark 1.9.1 or later, older versions exit on unknown flags
        private bool SupportsDryRun(string executable)
        {
            try
            {
                var binaries = new List<string> { executable };
                string directory = Path.GetDirectoryName(executable) ?? "";
                binaries.AddRange(PeParser.ParseImports(executable, _logger)
                    .Where(GoogleTestDiscoverer.IsGoogleBenchmarkDll)
                    .Select(dll => Path.Combine(directory, dll))
                    .Where(File.Exists));
                if (binaries.Any(binary => Utils.BinaryFileContainsStrings(binary, Encoding.ASCII, new[] { BenchmarkCommandLineGenerator.DryRunMarker })))
                    return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _logger.DebugWarning($"{_threadName}Could not check whether executable '{executable}' supports {BenchmarkCommandLineGenerator.DryRunOption}: {e.Message}");
            }

            _logger.LogInfo($"{_threadName}Executable '{executable}' does not support dry runs of benchmarks (Google Benchmark 1.9.1 or later), its benchmarks are run completely");
            return false;
        }

        /// <returns>The benchmarks which have not been run because a benchmark has timed out</returns>
        private List<TestCase> RunBenchmarks(string executable, string workingDir, BenchmarkCommandLineGenerator.Args arguments,
            string userParameters, IDictionary<string, string> environmentVariables, bool isBeingDebugged,
            IDebuggedProcessExecutorFactory processExecutorFactory, string jsonFile, bool isDryRun)
        {
            var results = new List<TestResult>();
            List<TestCase> benchmarks = arguments.TestCases.Where(tc => !tc.IsExitCodeTestCase).ToList();
            Dictionary<string, TestCase> benchmarksByName = benchmarks
                .GroupBy(tc => tc.FullyQualifiedName)
                .ToDictionary(g => g.Key, g => g.First());

            void ReportFinishedBenchmark(string name, IList<BenchmarkRun> runs)
            {
                if (_canceled || !benchmarksByName.TryGetValue(name, out TestCase benchmark))
                    return;
                TestResult result = BenchmarkTestResults.CreateTestResult(benchmark, runs, isDryRun);
                lock (results)
                {
                    results.Add(result);
                }
                ReportBenchmarkResults(executable, new[] { result });
            }

            try
            {
                // a file of an earlier command line must not be taken for results
                File.WriteAllText(jsonFile, "");
                string pathExtension = GetPathExtension(executable, ref environmentVariables);
                bool isTestOutputAvailable = IsTestOutputAvailable(isBeingDebugged);

                // the order of the benchmarks is needed for telling which one is running; a debugged benchmark might
                // be paused at a breakpoint
                Dictionary<string, TimeSpan> timeouts = isBeingDebugged || !isTestOutputAvailable
                    ? new Dictionary<string, TimeSpan>()
                    : GetBenchmarkTimeouts(executable, benchmarks);
                IList<string> runOrder = timeouts.Count > 0
                    ? ListBenchmarks(executable, workingDir, arguments.Filter, userParameters, pathExtension, environmentVariables, processExecutorFactory)
                    : null;

                var output = new List<string>();
                CreateProcessExecutor(isBeingDebugged, processExecutorFactory);
                int exitCode;
                IList<BenchmarkRun> runs;
                var watcher = new BenchmarkJsonWatcher(jsonFile, ReportFinishedBenchmark, runOrder, timeouts, _processExecutor,
                    (name, timeout) => _logger.LogWarning($"{_threadName}Benchmark {name} has not finished within its timeout of {timeout.TotalSeconds}s, killing executable '{executable}'"));
                try
                {
                    exitCode = _processExecutor.ExecuteCommandBlocking(
                        executable, arguments.CommandLine, workingDir, pathExtension, environmentVariables,
                        isTestOutputAvailable ? (Action<string>)(line => { lock (output) output.Add(line); }) : null);
                }
                finally
                {
                    runs = watcher.Stop();
                }
                ExecutableResults.Add(new ExecutableResult(executable, exitCode));
                _logger.DebugInfo($"{_threadName}Executable '{executable}' returned with exit code {exitCode} after running {runs.Select(r => r.RunName).Distinct().Count()} benchmark(s)");

                if (_canceled)
                    return new List<TestCase>();

                List<TestCase> missing;
                lock (results)
                {
                    missing = benchmarks.Except(results.Select(r => r.TestCase)).ToList();
                }
                if (missing.Count == 0)
                    return new List<TestCase>();

                var missingResults = new List<TestResult>();
                List<TestCase> benchmarksNotRun = CreateResultsOfMissingBenchmarks(executable, workingDir, arguments,
                    userParameters, pathExtension, environmentVariables, processExecutorFactory, exitCode, output,
                    watcher, runOrder, missing, missingResults);
                ReportBenchmarkResults(executable, missingResults);
                return benchmarksNotRun;
            }
            catch (Exception e)
            {
                LogExecutionError(_logger, executable, workingDir, arguments.CommandLine, e, _threadName);
                return new List<TestCase>();
            }
        }

        private List<TestCase> CreateResultsOfMissingBenchmarks(string executable, string workingDir,
            BenchmarkCommandLineGenerator.Args arguments, string userParameters, string pathExtension,
            IDictionary<string, string> environmentVariables, IDebuggedProcessExecutorFactory processExecutorFactory,
            int exitCode, List<string> output, BenchmarkJsonWatcher watcher, IList<string> runOrder,
            List<TestCase> missing, List<TestResult> results)
        {
            TestCase timedOut = missing.FirstOrDefault(tc => tc.FullyQualifiedName == watcher.TimedOutBenchmark);
            if (timedOut != null)
            {
                results.Add(CreateFailedBenchmarkResult(timedOut,
                    StreamingStandardOutputTestResultParser.CreateTimeoutText(watcher.Timeout), watcher.Timeout));
                missing.Remove(timedOut);
                return missing;
            }

            var collector = new TestResultCollector(_logger, _threadName, _settings);
            if (exitCode != 0)
            {
                runOrder = runOrder ?? ListBenchmarks(executable, workingDir, arguments.Filter, userParameters, pathExtension, environmentVariables, processExecutorFactory);
                TestCase crashed = runOrder?
                    .Select(name => missing.FirstOrDefault(tc => tc.FullyQualifiedName == name))
                    .FirstOrDefault(tc => tc != null);
                if (crashed != null)
                {
                    results.Add(CreateFailedBenchmarkResult(crashed,
                        StreamingStandardOutputTestResultParser.CrashText + StreamingStandardOutputTestResultParser.CreateExitCodeText(exitCode),
                        TimeSpan.Zero));
                    missing.Remove(crashed);
                    collector.CreateMissingResults(missing.ToArray(), crashed, results);
                    return new List<TestCase>();
                }

                string outputText;
                lock (output)
                {
                    outputText = output.Count > 0
                        ? $", output:{Environment.NewLine}{string.Join(Environment.NewLine, output)}"
                        : ", no output";
                }
                _logger.LogWarning($"{_threadName}Executable '{executable}' returned with exit code {exitCode} without running {missing.Count} benchmark(s){outputText}");
            }

            collector.ReportSuspiciousTestCases(missing.ToArray(), results);
            return new List<TestCase>();
        }

        private static TestResult CreateFailedBenchmarkResult(TestCase benchmark, string errorMessage, TimeSpan duration)
        {
            return new TestResult(benchmark)
            {
                ComputerName = Environment.MachineName,
                DisplayName = benchmark.DisplayName,
                Outcome = TestOutcome.Failed,
                ErrorMessage = errorMessage,
                ErrorStackTrace = BenchmarkTestResults.GetLocationStackTrace(benchmark),
                Duration = duration
            };
        }

        private void ReportBenchmarkResults(string executable, IList<TestResult> results)
        {
            if (results.Count == 0)
                return;

            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                _frameworkReporter.ReportTestsStarted(results.Select(tr => tr.TestCase));
                _frameworkReporter.ReportTestResults(results);
                stopwatch.Stop();
                _logger.VerboseInfo($"{_threadName}Reported {results.Count} benchmark results to VS, executable: '{executable}', duration: {stopwatch.Elapsed}");
            }
            catch (TestRunCanceledException e)
            {
                _logger.DebugInfo($"{_threadName}Execution has been canceled: {e.InnerException?.Message ?? e.Message}");
                Cancel();
            }

            new TestDurationSerializer().UpdateTestDurations(results);
            foreach (TestResult result in results)
            {
                if (!_schedulingAnalyzer.AddActualDuration(result.TestCase, (int)result.Duration.TotalMilliseconds))
                    _logger.DebugWarning($"{_threadName}TestCase already in analyzer: {result.TestCase.FullyQualifiedName}");
            }
        }

        private Dictionary<string, TimeSpan> GetBenchmarkTimeouts(string executable, IEnumerable<TestCase> benchmarks)
        {
            TimeSpan? defaultTimeout = _settings.TestTimeoutInSeconds > 0
                ? TimeSpan.FromSeconds(_settings.TestTimeoutInSeconds)
                : (TimeSpan?)null;
            var timeouts = new Dictionary<string, TimeSpan>();
            foreach (TestCase benchmark in benchmarks)
            {
                TimeSpan? timeout = _settings.GetTestPropertySettings(executable, benchmark.FullyQualifiedName)?.Timeout
                                    ?? defaultTimeout;
                if (timeout.HasValue)
                    timeouts[benchmark.FullyQualifiedName] = timeout.Value;
            }
            return timeouts;
        }

        /// <returns>The names of the benchmarks the filter selects, in the order they are run, or null if they can
        /// not be listed</returns>
        private IList<string> ListBenchmarks(string executable, string workingDir, string filter, string userParameters,
            string pathExtension, IDictionary<string, string> environmentVariables, IProcessExecutorFactory processExecutorFactory)
        {
            string parameters = GoogleBenchmarkConstants.ListTestsOption;
            if (filter != null)
                parameters += " " + filter;
            if (!string.IsNullOrEmpty(userParameters))
                parameters += " " + userParameters;

            var lines = new List<string>();
            try
            {
                int exitCode = processExecutorFactory.CreateExecutor(false, _logger).ExecuteCommandBlocking(
                    executable, parameters, workingDir, pathExtension, environmentVariables, line => lines.Add(line));
                if (exitCode == 0)
                    return lines.Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();

                _logger.DebugWarning($"{_threadName}Could not list the benchmarks of executable '{executable}': exit code {exitCode}");
            }
            catch (Exception e)
            {
                _logger.DebugWarning($"{_threadName}Could not list the benchmarks of executable '{executable}': {e.Message}");
            }
            return null;
        }

        /// <summary>
        /// Watches the JSON file Google Benchmark writes while running benchmarks, reporting each benchmark when it
        /// is complete, and killing the executable when the running benchmark exceeds its timeout
        /// </summary>
        private sealed class BenchmarkJsonWatcher
        {
            private readonly string _jsonFile;
            private readonly Action<string, IList<BenchmarkRun>> _onBenchmarkFinished;
            private readonly IList<string> _runOrder;
            private readonly IDictionary<string, TimeSpan> _timeouts;
            private readonly IProcessExecutor _processExecutor;
            private readonly Action<string, TimeSpan> _onTimeout;
            private readonly System.Threading.Timer _timer;
            private readonly object _lock = new object();
            private readonly HashSet<string> _reportedBenchmarks = new HashSet<string>();
            private bool _isStopped;
            private int _nrOfStartedBenchmarks;
            private DateTime _startOfRunningBenchmark = DateTime.UtcNow;

            public string TimedOutBenchmark { get; private set; }
            public TimeSpan Timeout { get; private set; }

            public BenchmarkJsonWatcher(string jsonFile, Action<string, IList<BenchmarkRun>> onBenchmarkFinished,
                IList<string> runOrder, IDictionary<string, TimeSpan> timeouts, IProcessExecutor processExecutor,
                Action<string, TimeSpan> onTimeout)
            {
                _jsonFile = jsonFile;
                _onBenchmarkFinished = onBenchmarkFinished;
                _runOrder = runOrder;
                _timeouts = timeouts;
                _processExecutor = processExecutor;
                _onTimeout = onTimeout;
                _timer = new System.Threading.Timer(_ => Check(), null, TimeoutWatchdogInterval, TimeoutWatchdogInterval);
            }

            /// <returns>All runs of the file; the benchmarks not reported so far are reported</returns>
            public IList<BenchmarkRun> Stop()
            {
                lock (_lock)
                {
                    _isStopped = true;
                    _timer.Dispose();
                    return Poll(isFinal: true);
                }
            }

            private void Check()
            {
                lock (_lock)
                {
                    if (_isStopped)
                        return;
                    try
                    {
                        Poll(isFinal: false);
                    }
                    catch (Exception)
                    {
                        // e.g. the file is being written; it is read again by the next check
                    }
                }
            }

            private IList<BenchmarkRun> Poll(bool isFinal)
            {
                IList<BenchmarkRun> runs = BenchmarkJsonParser.ParseRuns(ReadFile());
                List<string> names = runs.Select(r => r.RunName).Distinct().ToList();

                // the last benchmark of the file might not be complete while the executable is running
                int nrOfFinishedBenchmarks = isFinal ? names.Count : names.Count - 1;
                for (int i = 0; i < nrOfFinishedBenchmarks; i++)
                {
                    string name = names[i];
                    if (_reportedBenchmarks.Add(name))
                        _onBenchmarkFinished(name, runs.Where(r => r.RunName == name).ToList());
                }

                if (!isFinal && _runOrder != null && TimedOutBenchmark == null)
                    CheckTimeout(names.Count);
                return runs;
            }

            // Google Benchmark writes the runs of a benchmark when it has finished, i.e., the benchmarks in the file
            // have finished, and the next one is running
            private void CheckTimeout(int nrOfFinishedBenchmarks)
            {
                if (nrOfFinishedBenchmarks != _nrOfStartedBenchmarks)
                {
                    _nrOfStartedBenchmarks = nrOfFinishedBenchmarks;
                    _startOfRunningBenchmark = DateTime.UtcNow;
                }
                if (nrOfFinishedBenchmarks >= _runOrder.Count)
                    return;

                string runningBenchmark = _runOrder[nrOfFinishedBenchmarks];
                if (!_timeouts.TryGetValue(runningBenchmark, out TimeSpan timeout) || DateTime.UtcNow - _startOfRunningBenchmark <= timeout)
                    return;

                TimedOutBenchmark = runningBenchmark;
                Timeout = timeout;
                _onTimeout(runningBenchmark, timeout);
                _processExecutor.Cancel();
            }

            private string ReadFile()
            {
                try
                {
                    using (var stream = new FileStream(_jsonFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    return "";
                }
            }
        }
    }

}
