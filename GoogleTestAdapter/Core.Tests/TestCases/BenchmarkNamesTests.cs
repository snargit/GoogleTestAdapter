using FluentAssertions;
using GoogleTestAdapter.Model;
using GoogleTestAdapter.Tests.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GoogleTestAdapter.Tests.Common.TestMetadata.TestCategories;

namespace GoogleTestAdapter.TestCases
{

    [TestClass]
    public class BenchmarkNamesTests : TestsBase
    {

        [TestMethod]
        [TestCategory(Unit)]
        public void Split_BenchmarkNames_SplitsAtSlashesOutsideOfTemplateArguments()
        {
            BenchmarkNames.Split("BM_Foo").Should().Equal("BM_Foo");
            BenchmarkNames.Split("BM_Foo/8/threads:2").Should().Equal("BM_Foo", "8", "threads:2");
            BenchmarkNames.Split("BM_Foo/size:100/inserts:128").Should().Equal("BM_Foo", "size:100", "inserts:128");
            BenchmarkNames.Split("Fixture/Method/min_time:0.500").Should().Equal("Fixture", "Method", "min_time:0.500");
            BenchmarkNames.Split("BM_Foo<std::array<int, 3>>/8").Should().Equal("BM_Foo<std::array<int, 3>>", "8");
            BenchmarkNames.Split("BM_Foo<a/b>/8").Should().Equal("BM_Foo<a/b>", "8");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetFixtureClass_FixtureAndMethod_ClassDefinedByGoogleBenchmark()
        {
            BenchmarkNames.GetFixtureClass("MyFixture", "Method").Should().Be("MyFixture_Method_Benchmark");
            BenchmarkNames.GetFixtureClass("MyFixture<int>", "Method").Should().Be("MyFixture_Method_Benchmark");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void RemoveTemplateArguments_Names_TrailingTemplateArgumentsAreRemoved()
        {
            BenchmarkNames.RemoveTemplateArguments("BM_Foo").Should().Be("BM_Foo");
            BenchmarkNames.RemoveTemplateArguments("BM_Foo<int>").Should().Be("BM_Foo");
            BenchmarkNames.RemoveTemplateArguments("BM_Foo<std::array<int, 3>, char>").Should().Be("BM_Foo");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void RemoveNamespaceAndGetNamespace_QualifiedNames_AreSplitOutsideOfTemplateArguments()
        {
            BenchmarkNames.RemoveNamespace("BM_Foo").Should().Be("BM_Foo");
            BenchmarkNames.GetNamespace("BM_Foo").Should().Be("");

            BenchmarkNames.RemoveNamespace("outer::inner::BM_Foo").Should().Be("BM_Foo");
            BenchmarkNames.GetNamespace("outer::inner::BM_Foo").Should().Be("outer::inner");

            BenchmarkNames.RemoveNamespace("ns::BM_Foo<std::string>").Should().Be("BM_Foo<std::string>");
            BenchmarkNames.GetNamespace("ns::BM_Foo<std::string>").Should().Be("ns");
            BenchmarkNames.GetNamespace("BM_Foo<std::string>").Should().Be("");

            BenchmarkNames.GetNamespace("outer::`anonymous namespace'::BM_Foo").Should().Be("outer::(anonymous namespace)");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void CreateDescriptor_Benchmarks_ClassAndGroupOfFunctionOrFixture()
        {
            TestCaseDescriptor descriptor = BenchmarkNames.CreateDescriptor("BM_Foo/8", false);
            descriptor.FullyQualifiedName.Should().Be("BM_Foo/8");
            descriptor.DisplayName.Should().Be("BM_Foo/8");
            descriptor.Suite.Should().Be("BM_Foo");
            descriptor.TestClass.Should().Be("BM_Foo");
            descriptor.TestGroup.Should().Be("BM_Foo");

            descriptor = BenchmarkNames.CreateDescriptor("ns::BM_Foo<int>", false);
            descriptor.TestClass.Should().Be("BM_Foo");
            descriptor.TestGroup.Should().Be("BM_Foo<int>");

            descriptor = BenchmarkNames.CreateDescriptor("MyFixture/Method/8", true);
            descriptor.Suite.Should().Be("MyFixture");
            descriptor.TestClass.Should().Be("MyFixture");
            descriptor.TestGroup.Should().Be("Method");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void TestCaseMetaDataProperty_Benchmark_IsSerializedAndDeserialized()
        {
            var property = new TestCaseMetaDataProperty(2, 5, isBenchmark: true);
            var deserialized = new TestCaseMetaDataProperty(property.Serialization);

            deserialized.NrOfTestCasesInSuite.Should().Be(2);
            deserialized.NrOfTestCasesInExecutable.Should().Be(5);
            deserialized.IsBenchmark.Should().BeTrue();
            deserialized.IsTearDownTestSuite.Should().BeFalse();
            new TestCaseMetaDataProperty("2:5").IsBenchmark.Should().BeFalse();
            new TestCaseMetaDataProperty("2:5:TearDownTestSuite").IsBenchmark.Should().BeFalse();
        }

    }

}
