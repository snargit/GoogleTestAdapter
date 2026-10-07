using System.Collections.Generic;
using FluentAssertions;
using GoogleTestAdapter.Tests.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GoogleTestAdapter.Tests.Common.TestMetadata.TestCategories;

namespace GoogleTestAdapter.Settings
{
    [TestClass]
    public class TestPropertySettingsContainerTests : TestsBase
    {
        private const string Executable = @"C:\build\tests\MyTests.exe";

        [TestMethod]
        [TestCategory(Unit)]
        public void GetSettingsForTest_ExactName_ReturnsSettingsOfTest()
        {
            var container = new TestPropertySettingsContainer(new[]
            {
                CreateTest("Suite.Test1", @"C:\build\dir1"),
                CreateTest("Suite.Test2", @"C:\build\dir2")
            });

            container.GetSettingsForTest(Executable, "Suite.Test1").WorkingDirectory.Should().Be(@"C:\build\dir1");
            container.GetSettingsForTest(Executable, "Suite.Test2").WorkingDirectory.Should().Be(@"C:\build\dir2");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetSettingsForTest_ExecutablePathDiffersInCaseAndNotation_ReturnsSettingsOfTest()
        {
            var container = new TestPropertySettingsContainer(new[]
            {
                CreateTest("Suite.Test1", @"C:\build\dir1", command: @"c:\BUILD\other\..\tests\mytests.EXE"),
                CreateTest("Suite.Test2", @"C:\build\dir2")
            });

            container.GetSettingsForTest(Executable, "Suite.Test1").WorkingDirectory.Should().Be(@"C:\build\dir1");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetSettingsForTest_ParameterizedTestAddedByGtestAddTests_ReturnsSettingsOfTest()
        {
            var container = new TestPropertySettingsContainer(new[]
            {
                CreateTest("*/Suite.Test/*", @"C:\build\dir1"),
                CreateTest("Suite.Other", @"C:\build\dir2")
            });

            container.GetSettingsForTest(Executable, "Instance/Suite.Test/3").WorkingDirectory.Should().Be(@"C:\build\dir1");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetSettingsForTest_NameDoesNotMatchButAllTestsShareSettings_ReturnsSharedSettings()
        {
            var container = new TestPropertySettingsContainer(new[]
            {
                CreateTest("prefix.Suite.Test1", @"C:\build\dir", new Dictionary<string, string> { { "MYVAR", "value" } }),
                CreateTest("Instance/Suite.Test/pretty_name", @"C:\build\dir", new Dictionary<string, string> { { "myvar", "value" } })
            });

            var settings = container.GetSettingsForTest(Executable, "Instance/Suite.Test/0");

            settings.WorkingDirectory.Should().Be(@"C:\build\dir");
            settings.Environment["MYVAR"].Should().Be("value");
            container.GetSettingsForExecutable(Executable).Should().BeSameAs(settings);
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetSettingsForTest_NameDoesNotMatchAndTestsDifferInSettings_ReturnsNull()
        {
            var container = new TestPropertySettingsContainer(new[]
            {
                CreateTest("Suite.Test1", @"C:\build\dir1"),
                CreateTest("Suite.Test2", @"C:\build\dir2")
            });

            container.GetSettingsForTest(Executable, "Suite.Test3").Should().BeNull();
            container.GetSettingsForExecutable(Executable).Should().BeNull();
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetSettingsForTest_OtherExecutable_ReturnsNull()
        {
            var container = new TestPropertySettingsContainer(new[] { CreateTest("Suite.Test1", @"C:\build\dir1") });

            container.GetSettingsForTest(@"C:\build\tests\OtherTests.exe", "Suite.Test1").Should().BeNull();
            container.GetSettingsForExecutable(@"C:\build\tests\OtherTests.exe").Should().BeNull();
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void Constructor_DuplicateAndInvalidEntries_AreIgnored()
        {
            var container = new TestPropertySettingsContainer(new[]
            {
                CreateTest("Suite.Test1", @"C:\build\dir1"),
                CreateTest("Suite.Test1", @"C:\build\dir2"),
                CreateTest(null, @"C:\build\dir3"),
                CreateTest("Suite.Test2", @"C:\build\dir4", command: null),
                CreateTest("Suite.Test3", @"C:\build\dir5", command: "C:\\in<valid|path"),
            });

            container.IsEmpty.Should().BeFalse();
            container.GetSettingsForTest(Executable, "Suite.Test1").WorkingDirectory.Should().Be(@"C:\build\dir1");
            container.GetSettingsForExecutable(Executable).WorkingDirectory.Should().Be(@"C:\build\dir1");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void Constructor_NoValidEntries_IsEmpty()
        {
            new TestPropertySettingsContainer(new[] { CreateTest("Suite.Test", @"C:\build\dir", command: "") })
                .IsEmpty.Should().BeTrue();
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void Equals_SameValuesInDifferentCase_AreEqual()
        {
            var settings = new TestPropertySettings(@"C:\Build", new Dictionary<string, string> { { "Var", "Value" } });
            var otherSettings = new TestPropertySettings(@"c:\build", new Dictionary<string, string> { { "VAR", "Value" } });
            var differentValue = new TestPropertySettings(@"C:\Build", new Dictionary<string, string> { { "Var", "value" } });

            settings.Should().Be(otherSettings);
            settings.GetHashCode().Should().Be(otherSettings.GetHashCode());
            settings.Should().NotBe(differentValue);
            new TestPropertySettings(" ", null).WorkingDirectory.Should().BeNull();
        }

        private static TestPropertySettingsContainer.TestProperties CreateTest(string name, string workingDirectory,
            IDictionary<string, string> environment = null, string command = Executable)
        {
            return new TestPropertySettingsContainer.TestProperties
            {
                Name = name,
                Command = command,
                WorkingDirectory = workingDirectory,
                Environment = environment
            };
        }
    }
}
