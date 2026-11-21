using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using GoogleTestAdapter.Model;
using GoogleTestAdapter.Scheduling;
using GoogleTestAdapter.TestResults;
using GoogleTestAdapter.Tests.Common;
using GoogleTestAdapter.Tests.Common.Assertions;
using GoogleTestAdapter.Tests.Common.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GoogleTestAdapter.Tests.Common.TestMetadata.TestCategories;

namespace GoogleTestAdapter
{
    [TestClass]
    public class GoogleTestExecutorTests : TestsBase
    {

        [TestMethod]
        [TestCategory(Integration)]
        public void RunTests_SequentialExecution_ProducesTestDurationsFile()
        {
            AssertDurationsFileIsCreated(false);
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void RunTests_ParallelExecution_ProducesTestDurationsFile()
        {
            AssertDurationsFileIsCreated(true);
        }

        // Other test assemblies, which run in parallel, run the same sample executables and thus update the same
        // durations files (the serializer locks them across processes, and merges the durations). So the files must not
        // be deleted (deleting them is not locked, and fails while another process has them open, or another process
        // recreates them right away); instead, they must have been written during the test run, and contain the
        // durations of its tests.
        private void AssertDurationsFileIsCreated(bool parallelExecution)
        {
            string sampleTestsDurationsFile = TestResources.Tests_DebugX86 + GoogleTestConstants.DurationsExtension;
            string crashingTestsDurationsFile = TestResources.CrashingTests_DebugX86 + GoogleTestConstants.DurationsExtension;
            // file system timestamps may be slightly coarser than the clock
            DateTime start = DateTime.UtcNow.AddSeconds(-1);

            MockOptions.Setup(o => o.ParallelTestExecution).Returns(parallelExecution);
            MockOptions.Setup(o => o.MaxNrOfThreads).Returns(2);
            MockOptions.Setup(o => o.SolutionDir).Returns(TestResources.SampleTestsSolutionDir);

            var collectingReporter = new FakeFrameworkReporter();
            var exitCodeTestsAggregator = new ExitCodeTestsAggregator();
            var exitCodeTestsReporter = new ExitCodeTestsReporter(MockFrameworkReporter.Object, exitCodeTestsAggregator, MockOptions.Object, MockLogger.Object);
            var testExecutor = new GoogleTestExecutor(TestEnvironment.Logger, TestEnvironment.Options, ProcessExecutorFactory, exitCodeTestsReporter);
            testExecutor.RunTests(TestDataCreator.AllTestCasesExceptLoadTests, collectingReporter,false);

            AssertWrittenSince(sampleTestsDurationsFile, start);
            var durations = new TestDurationSerializer().ReadTestDurations(TestDataCreator.AllTestCasesOfSampleTests);
            durations.Keys.Should().Contain(
                TestDataCreator.AllTestCasesOfSampleTests.Where(tc => collectingReporter.ReportedTestResults.Any(tr => tc.Equals(tr.TestCase) && (tr.Outcome == TestOutcome.Passed || tr.Outcome == TestOutcome.Failed))));

            AssertWrittenSince(crashingTestsDurationsFile, start);
            durations = new TestDurationSerializer().ReadTestDurations(TestDataCreator.AllTestCasesOfHardCrashingTests);
            durations.Keys.Should().Contain(TestDataCreator.AllTestCasesOfHardCrashingTests.Where(tc => collectingReporter.ReportedTestResults.Any(tr => tc.Equals(tr.TestCase) && (tr.Outcome == TestOutcome.Passed || tr.Outcome == TestOutcome.Failed))));
        }

        private static void AssertWrittenSince(string file, DateTime start)
        {
            file.AsFileInfo().Should().Exist("test execution should result in a test durations file");
            File.GetLastWriteTimeUtc(file).Should().BeOnOrAfter(start, "test execution should update the test durations file");
        }

    }

}