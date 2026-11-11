using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using GoogleTestAdapter.Common;
using GoogleTestAdapter.Helpers;
using GoogleTestAdapter.Model;
using GoogleTestAdapter.Scheduling;
using GoogleTestAdapter.Settings;
using GoogleTestAdapter.TestResults;
using GoogleTestAdapter.Tests.Common;
using GoogleTestAdapter.Tests.Common.Assertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static GoogleTestAdapter.Tests.Common.TestMetadata.TestCategories;

namespace GoogleTestAdapter.Runners
{

    /// <summary>
    /// Runs the benchmarks of the BenchmarkTests sample
    /// </summary>
    [TestClass]
    public class BenchmarkRunnerTests : TestsBase
    {
        private readonly List<TestResult> _results = new List<TestResult>();

        [TestInitialize]
        public override void SetUp()
        {
            base.SetUp();
            _results.Clear();
            MockFrameworkReporter
                .Setup(r => r.ReportTestResults(It.IsAny<IEnumerable<TestResult>>()))
                .Callback((IEnumerable<TestResult> results) =>
                {
                    lock (_results)
                        _results.AddRange(results);
                });
        }

        private IList<TestCase> DiscoverBenchmarks(params string[] names)
        {
            TestResources.BenchmarkTests_DebugX86.AsFileInfo().Should().Exist("building the SampleTests solution produces that executable");
            IList<TestCase> testCases = new GoogleTestDiscoverer(TestEnvironment.Logger, TestEnvironment.Options)
                .GetTestsFromExecutable(TestResources.BenchmarkTests_DebugX86);
            testCases.Should().HaveCount(TestResources.BenchmarksOfBenchmarkTests.Length);
            return names.Length == 0 ? testCases : testCases.Where(tc => names.Contains(tc.FullyQualifiedName)).ToList();
        }

        private SequentialTestRunner CreateRunner(SettingsWrapper settings)
        {
            return new SequentialTestRunner("", 0, "", MockFrameworkReporter.Object, TestEnvironment.Logger, settings, new SchedulingAnalyzer(TestEnvironment.Logger));
        }

        private TestResult GetResult(string name)
        {
            lock (_results)
                return _results.Should().ContainSingle(r => r.TestCase.FullyQualifiedName == name, name).Which;
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void RunTests_AllBenchmarks_ResultsAreReadFromJsonFile()
        {
            IList<TestCase> benchmarks = DiscoverBenchmarks();
            SequentialTestRunner runner = CreateRunner(CreateSettings());

            runner.RunTests(benchmarks, false, ProcessExecutorFactory);

            MockLogger.Verify(l => l.LogError(It.IsAny<string>()), Times.Never);
            MockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Never);
            runner.ExecutableResults.Should().ContainSingle().Which.ExitCode.Should().Be(0);
            _results.Select(r => r.TestCase.FullyQualifiedName).Should().BeEquivalentTo(TestResources.BenchmarksOfBenchmarkTests);

            GetResult("BM_Error").Outcome.Should().Be(TestOutcome.Failed);
            GetResult("BM_Error").ErrorMessage.Should().Be("Benchmark error");
            GetResult("BM_Error").ErrorStackTrace.Should().Contain("BenchmarkTests.cpp");
            GetResult("BM_Skip").Outcome.Should().Be(TestOutcome.Skipped);
            GetResult("BM_Skip").ErrorMessage.Should().Be("Skipped by benchmark");
            foreach (TestResult result in _results.Where(r => r.TestCase.FullyQualifiedName != "BM_Error" && r.TestCase.FullyQualifiedName != "BM_Skip"))
            {
                result.Outcome.Should().Be(TestOutcome.Passed, result.TestCase.FullyQualifiedName);
                result.ErrorMessage.Should().StartWith(BenchmarkTestResults.DryRunPrefix, result.TestCase.FullyQualifiedName)
                    .And.Contain(" iteration", result.TestCase.FullyQualifiedName);
            }
            GetResult("BM_CountersAndLabel").StandardOutput.Should().Contain("Items: 42").And.Contain("Label: my label");
            // one iteration per thread
            GetResult("BM_Simple").ErrorMessage.Should().EndWith(", 1 iteration");
            GetResult("BM_Threads/threads:2").ErrorMessage.Should().EndWith(", 2 iterations");
            GetResult("BM_Threads/threads:2").StandardOutput.Should().Contain("Threads: 2");
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void RunTests_SomeBenchmarks_OnlyTheseAreRun()
        {
            string[] names = { TestResources.CustomNameBenchmark, "BM_Args/8", "BM_Template<int>", "MyFixture/FixtureMethod", "BM_NamedArgs/size:100/inserts:128" };
            IList<TestCase> benchmarks = DiscoverBenchmarks(names);
            benchmarks.Should().HaveCount(names.Length);

            CreateRunner(CreateSettings()).RunTests(benchmarks, false, ProcessExecutorFactory);

            MockLogger.Verify(l => l.LogError(It.IsAny<string>()), Times.Never);
            MockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Never);
            _results.Select(r => r.TestCase.FullyQualifiedName).Should().BeEquivalentTo(names);
            _results.Should().OnlyContain(r => r.Outcome == TestOutcome.Passed);
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void RunTests_FullRun_BenchmarkIsMeasured()
        {
            IList<TestCase> benchmarks = DiscoverBenchmarks("BM_Simple");

            CreateRunner(CreateSettings(benchmarkDryRun: false)).RunTests(benchmarks, false, ProcessExecutorFactory);

            MockLogger.Verify(l => l.LogError(It.IsAny<string>()), Times.Never);
            TestResult result = GetResult("BM_Simple");
            result.Outcome.Should().Be(TestOutcome.Passed);
            result.ErrorMessage.Should().NotStartWith(BenchmarkTestResults.DryRunPrefix).And.EndWith(" iterations");
            result.StandardOutput.Should().NotContain("Dry run");
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void RunTests_CrashingBenchmark_CrashIsReportedAndRemainingBenchmarksAreSkipped()
        {
            IList<TestCase> benchmarks = DiscoverBenchmarks();
            SequentialTestRunner runner = CreateRunner(CreateSettings(environmentVariables: "GTA_BENCHMARK_CRASH=1"));

            runner.RunTests(benchmarks, false, ProcessExecutorFactory);

            MockLogger.Verify(l => l.LogError(It.IsAny<string>()), Times.Never);
            runner.ExecutableResults.Should().ContainSingle().Which.ExitCode.Should().NotBe(0);
            GetResult("BM_Simple").Outcome.Should().Be(TestOutcome.Passed);
            GetResult("BM_Hang").Outcome.Should().Be(TestOutcome.Passed);

            TestResult crashed = GetResult("BM_Crash");
            crashed.Outcome.Should().Be(TestOutcome.Failed);
            crashed.ErrorMessage.Should().StartWith(StreamingStandardOutputTestResultParser.CrashText);
            crashed.ErrorStackTrace.Should().Contain("BenchmarkTests.cpp");

            TestResult afterCrash = GetResult("BM_AfterCrash");
            afterCrash.Outcome.Should().Be(TestOutcome.Skipped);
            afterCrash.ErrorMessage.Should().Contain("crash of test BM_Crash");
            _results.Should().HaveCount(TestResources.BenchmarksOfBenchmarkTests.Length);
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void RunTests_HangingBenchmark_IsKilledAndRemainingBenchmarksAreRun()
        {
            IList<TestCase> benchmarks = DiscoverBenchmarks("BM_Simple", "BM_Hang", "BM_Crash", "BM_AfterCrash");
            SequentialTestRunner runner = CreateRunner(CreateSettings(environmentVariables: "GTA_BENCHMARK_HANG=1", testTimeoutInSeconds: 1));

            Stopwatch stopwatch = Stopwatch.StartNew();
            runner.RunTests(benchmarks, false, ProcessExecutorFactory);
            stopwatch.Stop();

            // BM_Hang sleeps for 30s
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
            MockLogger.Verify(l => l.LogError(It.IsAny<string>()), Times.Never);
            MockLogger.Verify(l => l.LogWarning(It.Is<string>(s => s.Contains("BM_Hang") && s.Contains("timeout"))), Times.Once);
            runner.ExecutableResults.Should().HaveCount(2);

            TestResult timedOut = GetResult("BM_Hang");
            timedOut.Outcome.Should().Be(TestOutcome.Failed);
            timedOut.ErrorMessage.Should().Be(StreamingStandardOutputTestResultParser.CreateTimeoutText(TimeSpan.FromSeconds(1)));
            GetResult("BM_Simple").Outcome.Should().Be(TestOutcome.Passed);
            GetResult("BM_Crash").Outcome.Should().Be(TestOutcome.Passed);
            GetResult("BM_AfterCrash").Outcome.Should().Be(TestOutcome.Passed);
            _results.Should().HaveCount(4);
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void RunTests_OnlyExitCodeTestCase_ExecutableIsRunWithoutBenchmarks()
        {
            MockOptions.Setup(o => o.ExitCodeTestCase).Returns("ExitCode");
            TestCase exitCodeTestCase = new GoogleTestDiscoverer(TestEnvironment.Logger, TestEnvironment.Options)
                .GetTestsFromExecutable(TestResources.BenchmarkTests_DebugX86)
                .Single(tc => tc.IsExitCodeTestCase);
            SequentialTestRunner runner = CreateRunner(CreateSettings());

            runner.RunTests(exitCodeTestCase.Yield(), false, ProcessExecutorFactory);

            MockLogger.Verify(l => l.LogError(It.IsAny<string>()), Times.Never);
            runner.ExecutableResults.Should().ContainSingle().Which.ExitCode.Should().Be(0);
            _results.Should().BeEmpty();
        }

        private SettingsWrapper CreateSettings(string environmentVariables = null, int? testTimeoutInSeconds = null, bool? benchmarkDryRun = null)
        {
            var mockContainer = new Mock<IGoogleTestAdapterSettingsContainer>();
            var solutionSettings = new RunSettings
            {
                EnvironmentVariables = environmentVariables,
                TestTimeoutInSeconds = testTimeoutInSeconds,
                BenchmarkDryRun = benchmarkDryRun
            };
            mockContainer.Setup(c => c.SolutionSettings).Returns(solutionSettings);

            return new SettingsWrapper(mockContainer.Object, TestResources.SampleTestsSolutionDir)
            {
                RegexTraitParser = new RegexTraitParser(MockLogger.Object),
                EnvironmentVariablesParser = new EnvironmentVariablesParser(MockLogger.Object),
                HelperFilesCache = new HelperFilesCache(MockLogger.Object)
            };
        }
    }

}
