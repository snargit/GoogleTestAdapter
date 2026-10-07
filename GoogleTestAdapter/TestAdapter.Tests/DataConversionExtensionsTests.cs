using System;
using FluentAssertions;
using GoogleTestAdapter.Model;
using GoogleTestAdapter.Tests.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VsTestResultMessage = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResultMessage;
using static GoogleTestAdapter.Tests.Common.TestMetadata.TestCategories;

namespace GoogleTestAdapter.TestAdapter
{
    [TestClass]
    public class DataConversionExtensionsTests : TestAdapterTestsBase
    {
        [TestMethod]
        [TestCategory(Unit)]
        public void ToVsTestResult_ResultWithStandardOutput_OutputIsAttachedAsStandardOutMessage()
        {
            var testResult = new TestResult(TestDataCreator.ToTestCase("Suite.Test"))
            {
                ComputerName = Environment.MachineName,
                Outcome = TestOutcome.Passed,
                Duration = TimeSpan.FromMilliseconds(1),
                StandardOutput = "Some output\n"
            };

            var vsTestResult = testResult.ToVsTestResult();

            vsTestResult.Messages.Should().ContainSingle();
            vsTestResult.Messages[0].Category.Should().Be(VsTestResultMessage.StandardOutCategory);
            vsTestResult.Messages[0].Text.Should().Be("Some output\n");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void ToVsTestResult_ResultWithoutStandardOutput_NoMessageIsAttached()
        {
            var testResult = new TestResult(TestDataCreator.ToTestCase("Suite.Test"))
            {
                ComputerName = Environment.MachineName,
                Outcome = TestOutcome.Passed
            };

            testResult.ToVsTestResult().Messages.Should().BeEmpty();
        }
    }
}
