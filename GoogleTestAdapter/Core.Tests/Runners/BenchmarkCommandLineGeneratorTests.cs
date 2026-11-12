using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GoogleTestAdapter.Model;
using GoogleTestAdapter.Tests.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GoogleTestAdapter.Tests.Common.TestMetadata.TestCategories;

namespace GoogleTestAdapter.Runners
{

    [TestClass]
    public class BenchmarkCommandLineGeneratorTests : TestsBase
    {
        private const string JsonFile = @"C:\Temp\results.json";

        private static List<TestCase> CreateBenchmarks(int nrOfBenchmarksOfExecutable, params string[] names)
        {
            return names.Select(name =>
            {
                var testCase = new TestCase(name, "BenchmarkTests.exe", name, "", 0);
                testCase.Properties.Add(new TestCaseMetaDataProperty(1, nrOfBenchmarksOfExecutable, isBenchmark: true));
                return testCase;
            }).ToList();
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void EscapeRegex_SpecialCharactersOfExtendedRegexes_AreEscaped()
        {
            BenchmarkCommandLineGenerator.EscapeRegex("BM_Foo/8/threads:2").Should().Be("BM_Foo/8/threads:2");
            BenchmarkCommandLineGenerator.EscapeRegex("BM_Foo<int, 3>").Should().Be("BM_Foo<int, 3>");
            BenchmarkCommandLineGenerator.EscapeRegex(@"BM.(1+2)*[3]{4}|^$\?").Should().Be(@"BM\.\(1\+2\)\*\[3[]]\{4\}\|\^\$\\\?");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void QuoteArgument_Arguments_AreQuotedForTheCRuntime()
        {
            BenchmarkCommandLineGenerator.QuoteArgument("simple").Should().Be("\"simple\"");
            BenchmarkCommandLineGenerator.QuoteArgument(@"C:\dir\file").Should().Be(@"""C:\dir\file""");
            BenchmarkCommandLineGenerator.QuoteArgument(@"ends with\").Should().Be(@"""ends with\\""");
            BenchmarkCommandLineGenerator.QuoteArgument(@"a ""quote""").Should().Be(@"""a \""quote\""""");
            BenchmarkCommandLineGenerator.QuoteArgument(@"backslash\""quote").Should().Be(@"""backslash\\\""quote""");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetCommandLines_AllBenchmarks_NoFilter()
        {
            List<TestCase> benchmarks = CreateBenchmarks(2, "BM_A", "BM_B");

            IList<BenchmarkCommandLineGenerator.Args> commandLines =
                new BenchmarkCommandLineGenerator(benchmarks, 10, "--my_flag", JsonFile, true).GetCommandLines();

            commandLines.Should().ContainSingle();
            commandLines[0].CommandLine.Should().Be($@"""--benchmark_out={JsonFile}"" --benchmark_out_format=json --benchmark_dry_run --my_flag");
            commandLines[0].Filter.Should().BeNull();
            commandLines[0].TestCases.Should().BeEquivalentTo(benchmarks);
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetCommandLines_SomeBenchmarks_FilterOfEscapedNames()
        {
            List<TestCase> benchmarks = CreateBenchmarks(5, "BM_A/8", "BM_B<int>", "BM.(c)");

            IList<BenchmarkCommandLineGenerator.Args> commandLines =
                new BenchmarkCommandLineGenerator(benchmarks, 10, "", JsonFile, false).GetCommandLines();

            commandLines.Should().ContainSingle();
            commandLines[0].Filter.Should().Be(@"""--benchmark_filter=^(BM_A/8|BM_B<int>|BM\.\(c\))$""");
            commandLines[0].CommandLine.Should().Be($@"""--benchmark_out={JsonFile}"" --benchmark_out_format=json {commandLines[0].Filter}");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetCommandLines_ManyBenchmarks_FiltersAreSplit()
        {
            string[] names = Enumerable.Range(0, 1000).Select(i => $"BM_Benchmark_With_A_Long_Name/{i}").ToArray();
            List<TestCase> benchmarks = CreateBenchmarks(2000, names);

            IList<BenchmarkCommandLineGenerator.Args> commandLines =
                new BenchmarkCommandLineGenerator(benchmarks, 50, "--my_flag", JsonFile, true).GetCommandLines();

            commandLines.Should().HaveCountGreaterThan(1);
            commandLines.SelectMany(a => a.TestCases).Should().Equal(benchmarks);
            foreach (BenchmarkCommandLineGenerator.Args args in commandLines)
            {
                (args.CommandLine.Length + 50 + 1).Should().BeLessOrEqualTo(CommandLineGenerator.MaxCommandLength);
                args.CommandLine.Should().EndWith(" --my_flag");
                args.Filter.Should().Be($"\"--benchmark_filter=^({string.Join("|", args.TestCases.Select(tc => tc.FullyQualifiedName))})$\"");
            }
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetCommandLines_OnlyExitCodeTestCase_FilterMatchingNoBenchmark()
        {
            var exitCodeTestCase = new TestCase("Exit.BenchmarkTests", "BenchmarkTests.exe", "Exit.BenchmarkTests", "", 0);

            IList<BenchmarkCommandLineGenerator.Args> commandLines =
                new BenchmarkCommandLineGenerator(new[] { exitCodeTestCase }, 10, "", JsonFile, true).GetCommandLines();

            commandLines.Should().ContainSingle();
            commandLines[0].Filter.Should().Be(@"""--benchmark_filter=^$""");
            commandLines[0].TestCases.Should().Equal(exitCodeTestCase);
        }
    }

}
