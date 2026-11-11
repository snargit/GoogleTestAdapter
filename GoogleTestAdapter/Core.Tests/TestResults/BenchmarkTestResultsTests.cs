using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GoogleTestAdapter.Model;
using GoogleTestAdapter.Tests.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GoogleTestAdapter.Tests.Common.TestMetadata.TestCategories;

namespace GoogleTestAdapter.TestResults
{

    [TestClass]
    public class BenchmarkTestResultsTests : TestsBase
    {
        private const string Context = @"{
  ""context"": {
    ""date"": ""2026-11-11T16:46:03+00:00"",
    ""caches"": [ { ""type"": ""Data"", ""level"": 1 } ],
    ""load_avg"": [],
    ""json_schema_version"": 1,
    ""dry_run"": ""true""
  },
  ""benchmarks"": [";

        private const string Simple = @"
    {
      ""name"": ""BM_Simple"",
      ""family_index"": 0,
      ""per_family_instance_index"": 0,
      ""run_name"": ""BM_Simple"",
      ""run_type"": ""iteration"",
      ""repetitions"": 1,
      ""repetition_index"": 0,
      ""threads"": 1,
      ""iterations"": 56000000,
      ""real_time"": 1.2345000000000000e+01,
      ""cpu_time"": 1.2100000000000000e+01,
      ""time_unit"": ""ns""
    }";

        private const string Counters = @"
    {
      ""name"": ""BM_Counters/8"",
      ""run_name"": ""BM_Counters/8"",
      ""run_type"": ""iteration"",
      ""repetitions"": 1,
      ""threads"": 2,
      ""iterations"": 1,
      ""real_time"": 2.7000005502486601e+03,
      ""cpu_time"": 0.0000000000000000e+00,
      ""time_unit"": ""us"",
      ""Items"": 4.2000000000000000e+01,
      ""Rate"": Infinity,
      ""Nothing"": NaN,
      ""label"": ""my \""label\""\u0021""
    }";

        private const string Error = @"
    {
      ""name"": ""BM_Error"",
      ""run_name"": ""BM_Error"",
      ""run_type"": ""iteration"",
      ""repetitions"": 1,
      ""threads"": 1,
      ""error_occurred"": true,
      ""error_message"": ""Benchmark error"",
      ""iterations"": 1,
      ""real_time"": 0.0000000000000000e+00,
      ""cpu_time"": 0.0000000000000000e+00,
      ""time_unit"": ""ns""
    }";

        private const string Skip = @"
    {
      ""name"": ""BM_Skip"",
      ""run_name"": ""BM_Skip"",
      ""run_type"": ""iteration"",
      ""repetitions"": 1,
      ""threads"": 1,
      ""skipped"": true,
      ""skip_message"": ""Skipped by benchmark"",
      ""iterations"": 1,
      ""real_time"": 0.0000000000000000e+00,
      ""cpu_time"": 0.0000000000000000e+00,
      ""time_unit"": ""ns""
    }";

        private static string Repetition(int index) => $@"
    {{
      ""name"": ""BM_Rep"",
      ""run_name"": ""BM_Rep"",
      ""run_type"": ""iteration"",
      ""repetitions"": 2,
      ""repetition_index"": {index},
      ""threads"": 1,
      ""iterations"": 1000000,
      ""real_time"": {(6.8 + index).ToString(System.Globalization.CultureInfo.InvariantCulture)},
      ""cpu_time"": 15.625,
      ""time_unit"": ""ns""
    }}";

        private static string Aggregate(string name, double realTime, string unit = "time") => $@"
    {{
      ""name"": ""BM_Rep_{name}"",
      ""run_name"": ""BM_Rep"",
      ""run_type"": ""aggregate"",
      ""repetitions"": 2,
      ""threads"": 1,
      ""aggregate_name"": ""{name}"",
      ""aggregate_unit"": ""{unit}"",
      ""iterations"": 2,
      ""real_time"": {realTime.ToString(System.Globalization.CultureInfo.InvariantCulture)},
      ""cpu_time"": 7.8125,
      ""time_unit"": ""ns""
    }}";

        private static string Json(params string[] runs) => Context + string.Join(",", runs) + "\n  ]\n}\n";

        [TestMethod]
        [TestCategory(Unit)]
        public void ParseRuns_CompleteFile_AllRunsWithTheirValues()
        {
            IList<BenchmarkRun> runs = BenchmarkJsonParser.ParseRuns(Json(Simple, Counters, Error, Skip));

            runs.Select(r => r.Name).Should().Equal("BM_Simple", "BM_Counters/8", "BM_Error", "BM_Skip");

            BenchmarkRun simple = runs[0];
            simple.RunName.Should().Be("BM_Simple");
            simple.IsAggregate.Should().BeFalse();
            simple.Iterations.Should().Be(56000000);
            simple.RealTime.Should().Be(12.345);
            simple.CpuTime.Should().Be(12.1);
            simple.TimeUnit.Should().Be("ns");
            simple.Counters.Should().BeEmpty();

            BenchmarkRun counters = runs[1];
            counters.Threads.Should().Be(2);
            counters.Label.Should().Be("my \"label\"!");
            counters.Counters.Select(c => c.Key).Should().Equal("Items", "Rate", "Nothing");
            counters.Counters[0].Value.Should().Be(42);
            double.IsPositiveInfinity(counters.Counters[1].Value).Should().BeTrue();
            double.IsNaN(counters.Counters[2].Value).Should().BeTrue();

            runs[2].ErrorOccurred.Should().BeTrue();
            runs[2].ErrorMessage.Should().Be("Benchmark error");
            runs[3].Skipped.Should().BeTrue();
            runs[3].SkipMessage.Should().Be("Skipped by benchmark");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void ParseRuns_TruncatedFile_CompleteRunsOnly()
        {
            string json = Json(Simple, Counters, Error);
            int endOfSecondRun = json.IndexOf("my \\\"label", System.StringComparison.Ordinal);

            BenchmarkJsonParser.ParseRuns(json.Substring(0, endOfSecondRun)).Select(r => r.Name).Should().Equal("BM_Simple");
            BenchmarkJsonParser.ParseRuns(json.Substring(0, json.IndexOf("BM_Error", System.StringComparison.Ordinal) - 5))
                .Select(r => r.Name).Should().Equal("BM_Simple", "BM_Counters/8");
            // the file has been written up to the closing bracket of a run
            string uptoRun = Json(Simple);
            BenchmarkJsonParser.ParseRuns(uptoRun.Substring(0, uptoRun.LastIndexOf('}') - 4)).Select(r => r.Name).Should().Equal("BM_Simple");
            BenchmarkJsonParser.ParseRuns(Context).Should().BeEmpty();
            BenchmarkJsonParser.ParseRuns("").Should().BeEmpty();
            BenchmarkJsonParser.ParseRuns("{").Should().BeEmpty();
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void ParseRuns_TruncatedNumber_RunIsIgnored()
        {
            string json = Json(Simple);
            string truncated = json.Substring(0, json.IndexOf("1.2100", System.StringComparison.Ordinal) + 3);

            BenchmarkJsonParser.ParseRuns(truncated).Should().BeEmpty();
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void CreateTestResult_Outcomes_FromErrorsAndSkips()
        {
            IList<BenchmarkRun> runs = BenchmarkJsonParser.ParseRuns(Json(Simple, Error, Skip));
            var testCase = new TestCase("BM", "exe", "BM", @"C:\BenchmarkTests.cpp", 42);

            TestResult passed = BenchmarkTestResults.CreateTestResult(testCase, new[] { runs[0] }, false);
            passed.Outcome.Should().Be(TestOutcome.Passed);
            passed.ErrorMessage.Should().Be("12.3 ns (CPU 12.1 ns), 56M iterations");
            passed.Duration.TotalMilliseconds.Should().BeApproximately(56000000 * 12.345 / 1e6, 1);

            TestResult failed = BenchmarkTestResults.CreateTestResult(testCase, new[] { runs[1] }, false);
            failed.Outcome.Should().Be(TestOutcome.Failed);
            failed.ErrorMessage.Should().Be("Benchmark error");
            failed.ErrorStackTrace.Should().Contain(@"C:\BenchmarkTests.cpp").And.Contain("42");

            TestResult skipped = BenchmarkTestResults.CreateTestResult(testCase, new[] { runs[2] }, false);
            skipped.Outcome.Should().Be(TestOutcome.Skipped);
            skipped.ErrorMessage.Should().Be("Skipped by benchmark");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void CreateTestResult_DryRun_MessageAndOutputSayItIsADryRun()
        {
            IList<BenchmarkRun> runs = BenchmarkJsonParser.ParseRuns(Json(Simple));

            TestResult result = BenchmarkTestResults.CreateTestResult(new TestCase("BM", "exe", "BM", "", 0), runs, true);

            result.ErrorMessage.Should().Be(BenchmarkTestResults.DryRunPrefix + "12.3 ns (CPU 12.1 ns), 56M iterations");
            result.StandardOutput.Should().StartWith("Dry run");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void CreateTestResult_CountersAndLabel_AreInOutput()
        {
            IList<BenchmarkRun> runs = BenchmarkJsonParser.ParseRuns(Json(Counters));

            TestResult result = BenchmarkTestResults.CreateTestResult(new TestCase("BM_Counters/8", "exe", "BM_Counters/8", "", 0), runs, false);

            result.ErrorMessage.Should().Be("2700 us (CPU 0 us), 1 iteration");
            result.StandardOutput.Should().Contain("BM_Counters/8").And.Contain("Real time: 2700 us").And.Contain("CPU time: 0 us")
                .And.Contain("Iterations: 1").And.Contain("Threads: 2").And.Contain("Items: 42").And.Contain("Rate: inf")
                .And.Contain("Nothing: nan").And.Contain("Label: my \"label\"!");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void CreateTestResult_Repetitions_SummaryIsMeanAndOutputHasAllRuns()
        {
            IList<BenchmarkRun> runs = BenchmarkJsonParser.ParseRuns(Json(Repetition(0), Repetition(1),
                Aggregate("mean", 7.3), Aggregate("median", 7.3), Aggregate("stddev", 0.7), Aggregate("cv", 0.0959, "percentage")));
            runs.Should().HaveCount(6);
            runs.Should().OnlyContain(r => r.RunName == "BM_Rep");

            TestResult result = BenchmarkTestResults.CreateTestResult(new TestCase("BM_Rep", "exe", "BM_Rep", "", 0), runs, false);

            result.Outcome.Should().Be(TestOutcome.Passed);
            result.ErrorMessage.Should().Be("7.3 ns (CPU 7.81 ns), mean of 2 repetitions of 1M iterations");
            result.StandardOutput.Should().Contain("BM_Rep (repetition 1 of 2)").And.Contain("BM_Rep (repetition 2 of 2)")
                .And.Contain("BM_Rep_mean").And.Contain("BM_Rep_stddev").And.Contain("BM_Rep_cv").And.Contain("Real time: 9.59 %");
            // the aggregates are not counted
            result.Duration.TotalMilliseconds.Should().BeApproximately(1000000 * (6.8 + 7.8) / 1e6, 0.1);
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void FormatNumber_Numbers_ThreeSignificantDigitsAndSuffixes()
        {
            BenchmarkTestResults.FormatNumber(0).Should().Be("0");
            BenchmarkTestResults.FormatNumber(0.012345).Should().Be("0.0123");
            BenchmarkTestResults.FormatNumber(1.2345).Should().Be("1.23");
            BenchmarkTestResults.FormatNumber(12.345).Should().Be("12.3");
            BenchmarkTestResults.FormatNumber(123.45).Should().Be("123");
            BenchmarkTestResults.FormatNumber(1234.5, false).Should().Be("1235");
            BenchmarkTestResults.FormatNumber(1234.5).Should().Be("1.23k");
            BenchmarkTestResults.FormatNumber(56000000).Should().Be("56M");
            BenchmarkTestResults.FormatNumber(-1500000).Should().Be("-1.5M");
            BenchmarkTestResults.FormatNumber(double.PositiveInfinity).Should().Be("inf");

            BenchmarkTestResults.FormatCount(9999).Should().Be("9999");
            BenchmarkTestResults.FormatCount(25000).Should().Be("25k");
            BenchmarkTestResults.FormatIterations(1).Should().Be("1 iteration");
            BenchmarkTestResults.FormatIterations(2).Should().Be("2 iterations");
        }
    }

}
