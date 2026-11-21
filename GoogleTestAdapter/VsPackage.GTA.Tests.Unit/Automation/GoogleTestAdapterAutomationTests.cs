using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using FluentAssertions;
using GoogleTestAdapter.Settings;
using GoogleTestAdapter.TestAdapter.Settings;
using GoogleTestAdapter.Tests.Common;
using GoogleTestAdapter.VsPackage.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static GoogleTestAdapter.Tests.Common.TestMetadata.TestCategories;

namespace GoogleTestAdapter.VsPackage.Automation
{
    [TestClass]
    public class GoogleTestAdapterAutomationTests : TestsBase
    {
        private const string SolutionSettings = @"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
  <GoogleTestAdapterSettings>
    <SolutionSettings>
      <Settings>
        <WorkingDir>SolutionWorkingDir</WorkingDir>
        <AdditionalTestDiscoveryParam>-solution $(Executable)</AdditionalTestDiscoveryParam>
      </Settings>
    </SolutionSettings>
    <ProjectSettings>
      <Settings ProjectRegex=""\\Tests_gta\.exe$"">
        <WorkingDir>$(ExecutableDir)\ProjectWorkingDir</WorkingDir>
      </Settings>
    </ProjectSettings>
  </GoogleTestAdapterSettings>
</RunSettings>";

        private string _solutionSettingsFile;
        private Mock<IVisualStudioEnvironment> _mockEnvironment;

        [TestInitialize]
        public void CreateSolutionSettingsFile()
        {
            _solutionSettingsFile = Path.GetTempFileName();
            File.WriteAllText(_solutionSettingsFile, SolutionSettings);
            _mockEnvironment = new Mock<IVisualStudioEnvironment>();
        }

        [TestCleanup]
        public void DeleteSolutionSettingsFile()
        {
            File.Delete(_solutionSettingsFile);
        }

        /// <param name="solutionSettings">whether the solution settings file is used (its working dir does not exist)</param>
        private GoogleTestAdapterAutomation CreateAutomation(bool initialized = true, bool solutionSettings = true,
            string additionalTestExecutionParam = "-global")
        {
            var globalRunSettings = new RunSettings
            {
                TestDiscoveryRegex = "",
                AdditionalTestExecutionParam = additionalTestExecutionParam,
                SkipOriginCheck = false
            };
            var mockGlobalRunSettings = new Mock<IGlobalRunSettings>();
            mockGlobalRunSettings.Setup(grs => grs.RunSettings).Returns(globalRunSettings);
            var service = new RunSettingsServiceUnderTest(mockGlobalRunSettings.Object, solutionSettings ? _solutionSettingsFile : null);

            return new GoogleTestAdapterAutomation(() => initialized ? service : null, _mockEnvironment.Object,
                "Google Test Adapter", new Version(1, 2, 3, 4));
        }

        private static Dictionary<string, object> Parse(string json)
        {
            var result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            result["schemaVersion"].Should().Be(GoogleTestAdapterAutomation.SchemaVersion);
            return result;
        }

        private static IEnumerable<string> Messages(Dictionary<string, object> result, string severity)
        {
            return ((ArrayList)result["messages"]).Cast<Dictionary<string, object>>()
                .Where(m => (string)m["severity"] == severity)
                .Select(m => (string)m["message"]);
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetVersion__NameAndVersion()
        {
            var result = Parse(CreateAutomation().GetVersion());

            result["name"].Should().Be("Google Test Adapter");
            result["version"].Should().Be("1.2.3.4");
            ((ArrayList)result["messages"]).Should().BeEmpty();
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetRunSettings_NoRunSettingsFile_GlobalAndSolutionSettings()
        {
            var result = Parse(CreateAutomation().GetRunSettings(null));

            var runSettings = (string)result["runSettings"];
            runSettings.Should().StartWith("<RunSettings>");
            runSettings.Should().Contain("<AdditionalTestExecutionParam>-global</AdditionalTestExecutionParam>");
            runSettings.Should().Contain("<WorkingDir>SolutionWorkingDir</WorkingDir>");
            runSettings.Should().Contain(@"ProjectRegex=""\\Tests_gta\.exe$""");
            Messages(result, "Error").Should().BeEmpty();
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetRunSettings_RunSettingsFile_ItsSettingsTakePrecedence()
        {
            string runSettingsFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(runSettingsFile, SolutionSettings.Replace("SolutionWorkingDir", "UserWorkingDir"));

                var result = Parse(CreateAutomation().GetRunSettings(runSettingsFile));

                ((string)result["runSettings"]).Should().Contain("<WorkingDir>UserWorkingDir</WorkingDir>");
            }
            finally
            {
                File.Delete(runSettingsFile);
            }
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetRunSettings_NotInitialized_ErrorMessage()
        {
            var result = Parse(CreateAutomation(initialized: false).GetRunSettings(null));

            result.Should().NotContainKey("runSettings");
            Messages(result, "Error").Should().ContainSingle().Which.Should().Contain("not been initialized");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetRunSettings_RunSettingsFileDoesNotExist_ErrorMessage()
        {
            var result = Parse(CreateAutomation().GetRunSettings(@"C:\NotExisting\My.runsettings"));

            Messages(result, "Error").Should().ContainSingle().Which.Should().Contain("My.runsettings");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetEffectiveSettings_ExecutableMatchingProjectSettings_ProjectSettingsAreUsed()
        {
            string executable = Path.GetFullPath(TestResources.Tests_DebugX86);

            var result = Parse(CreateAutomation().GetEffectiveSettings(executable));

            result["executable"].Should().Be(executable);
            ((Dictionary<string, object>)result["projectSettings"])["projectRegex"].Should().Be(@"\\Tests_gta\.exe$");
            var settings = (Dictionary<string, object>)result["settings"];
            settings[nameof(SettingsWrapper.WorkingDir)].Should().Be(@"$(ExecutableDir)\ProjectWorkingDir");
            settings[nameof(SettingsWrapper.AdditionalTestExecutionParam)].Should().Be("-global");
            settings[nameof(SettingsWrapper.ParallelTestExecution)].Should().Be(false);
            settings[nameof(SettingsWrapper.DebuggerKind)].Should().BeOfType<string>();
            var discovery = (Dictionary<string, object>)result["discovery"];
            discovery["workingDir"].Should().Be(Path.GetDirectoryName(executable) + @"\ProjectWorkingDir");
            discovery["additionalParameters"].Should().Be("-solution " + executable);
            Messages(result, "Error").Should().BeEmpty();
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetEffectiveSettings_ExecutableNotMatchingProjectSettings_SolutionSettingsAreUsed()
        {
            var result = Parse(CreateAutomation().GetEffectiveSettings(Path.GetFullPath(TestResources.LoadTests_ReleaseX86)));

            result["projectSettings"].Should().BeNull();
            ((Dictionary<string, object>)result["settings"])[nameof(SettingsWrapper.WorkingDir)].Should().Be("SolutionWorkingDir");
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void CheckExecutable_GoogleTestExecutable_WouldBeDiscovered()
        {
            var result = Parse(CreateAutomation().CheckExecutable(Path.GetFullPath(TestResources.Tests_DebugX86)));

            result["exists"].Should().Be(true);
            result["isExe"].Should().Be(true);
            var trust = (Dictionary<string, object>)result["trust"];
            trust["securityZone"].Should().Be("MyComputer");
            trust["isOnNetworkDrive"].Should().Be(false);
            trust["isTrusted"].Should().Be(true);
            result["googleTestIndicator"].Should().Be(GoogleTestDiscoverer.IndicatorGoogleTestMarkers);
            result["testFramework"].Should().Be("GoogleTest");
            ((string)result["pdb"]).Should().EndWith("Tests_gta.pdb");
            result["wouldBeDiscovered"].Should().Be(true);
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void CheckExecutable_GoogleBenchmarkExecutable_WouldBeDiscovered()
        {
            var result = Parse(CreateAutomation().CheckExecutable(Path.GetFullPath(TestResources.BenchmarkTests_DebugX86)));

            result["googleTestIndicator"].Should().Be(GoogleTestDiscoverer.IndicatorGoogleBenchmarkMarkers);
            result["testFramework"].Should().Be("GoogleBenchmark");
            result["wouldBeDiscovered"].Should().Be(true);
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void CheckExecutable_ExecutableDoesNotExist_WouldNotBeDiscovered()
        {
            var result = Parse(CreateAutomation().CheckExecutable(@"C:\NotExisting\Tests.exe"));

            result["exists"].Should().Be(false);
            result["googleTestIndicator"].Should().BeNull();
            result["testFramework"].Should().BeNull();
            result["wouldBeDiscovered"].Should().Be(false);
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void CheckExecutable_NetworkShare_NotTrusted()
        {
            var result = Parse(CreateAutomation().CheckExecutable(@"\\gta-nonexisting-host\share\Tests.exe"));

            var trust = (Dictionary<string, object>)result["trust"];
            trust["isOnNetworkDrive"].Should().Be(true);
            trust["isTrusted"].Should().Be(false);
            result["wouldBeDiscovered"].Should().Be(false);
        }

        // polls like a client would
        private static Dictionary<string, object> WaitForTestDiscovery(GoogleTestAdapterAutomation automation, string discoveryId)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                var result = Parse(automation.GetTestDiscoveryResult(discoveryId));
                if ((string)result["status"] != TestDiscoveryJobs.Running)
                    return result;
                stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60), "discovery should finish");
                System.Threading.Thread.Sleep(100);
            }
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void StartTestDiscovery_FixtureTests_TestsWithMetadata()
        {
            var automation = CreateAutomation(solutionSettings: false);

            var started = Parse(automation.StartTestDiscovery(Path.GetFullPath(TestResources.FixtureTests_DebugX86)));
            var result = WaitForTestDiscovery(automation, (string)started["discoveryId"]);

            result["status"].Should().Be(TestDiscoveryJobs.Completed);
            result["executable"].Should().Be(Path.GetFullPath(TestResources.FixtureTests_DebugX86));
            var tests = ((ArrayList)result["tests"]).Cast<Dictionary<string, object>>().ToList();
            tests.Should().HaveCount(20, new JavaScriptSerializer().Serialize(result["discoveryMessages"]));
            var test = tests.Single(t => (string)t["fullyQualifiedName"] == "NamespacedFixture.Passes");
            test["kind"].Should().Be("Test");
            test["namespace"].Should().Be("fixtures");
            test["testClass"].Should().Be("NamespacedFixture");
            ((string)test["codeFilePath"]).Should().EndWith("FixtureTests.cpp");
            ((int)test["lineNumber"]).Should().BeGreaterThan(0);
            test["testProperties"].Should().BeNull();
            tests.Single(t => (string)t["fullyQualifiedName"] == "FailingTearDown.TearDownTestSuite")["kind"].Should().Be("TearDownTestSuite");
            ((ArrayList)result["discoveryMessages"]).Cast<Dictionary<string, object>>()
                .Where(m => (string)m["severity"] == "Error").Should().BeEmpty();
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void StartTestDiscovery_BenchmarkTests_BenchmarksWithMetadata()
        {
            // unlike Google Test, Google Benchmark rejects unknown flags
            var automation = CreateAutomation(solutionSettings: false, additionalTestExecutionParam: "");

            var started = Parse(automation.StartTestDiscovery(Path.GetFullPath(TestResources.BenchmarkTests_DebugX86)));
            var result = WaitForTestDiscovery(automation, (string)started["discoveryId"]);

            result["status"].Should().Be(TestDiscoveryJobs.Completed, new JavaScriptSerializer().Serialize(result["discoveryMessages"]));
            var tests = ((ArrayList)result["tests"]).Cast<Dictionary<string, object>>().ToList();
            tests.Select(t => (string)t["fullyQualifiedName"]).Should().Equal(TestResources.BenchmarksOfBenchmarkTests,
                new JavaScriptSerializer().Serialize(result["discoveryMessages"]));
            tests.Should().OnlyContain(t => (string)t["kind"] == "Benchmark");
            var fixture = tests.Single(t => (string)t["fullyQualifiedName"] == "MyFixture/FixtureMethod");
            fixture["testClass"].Should().Be("MyFixture");
            fixture["testGroup"].Should().Be("FixtureMethod");
            ((string)fixture["codeFilePath"]).Should().EndWith("BenchmarkTests.cpp");
            var traits = ((ArrayList)fixture["traits"]).Cast<Dictionary<string, object>>().ToList();
            traits.Should().Contain(t => (string)t["name"] == GoogleBenchmarkConstants.TraitName && (string)t["value"] == GoogleBenchmarkConstants.TraitValue);
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void StartTestDiscovery_ExecutableCanNotBeRun_Fails()
        {
            // the working dir of the solution settings does not exist
            var automation = CreateAutomation();

            var started = Parse(automation.StartTestDiscovery(Path.GetFullPath(TestResources.FixtureTests_DebugX86)));
            var result = WaitForTestDiscovery(automation, (string)started["discoveryId"]);

            result["status"].Should().Be(TestDiscoveryJobs.Failed);
            result.Should().NotContainKey("tests");
            ((ArrayList)result["discoveryMessages"]).Cast<Dictionary<string, object>>()
                .Should().Contain(m => (string)m["severity"] == "Error" && ((string)m["message"]).Contains("Failed to run test executable"));
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void StartTestDiscovery_UntrustedExecutable_FailsWithoutRunningIt()
        {
            var automation = CreateAutomation();

            var started = Parse(automation.StartTestDiscovery(@"\\gta-nonexisting-host\share\Tests.exe"));
            var result = WaitForTestDiscovery(automation, (string)started["discoveryId"]);

            result["status"].Should().Be(TestDiscoveryJobs.Failed);
            result.Should().NotContainKey("tests");
            ((ArrayList)result["discoveryMessages"]).Cast<Dictionary<string, object>>()
                .Should().Contain(m => ((string)m["message"]).Contains("would not discover"));
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void StartTestDiscovery_NotInitialized_ErrorMessageAndNoId()
        {
            var result = Parse(CreateAutomation(initialized: false).StartTestDiscovery(Path.GetFullPath(TestResources.FixtureTests_DebugX86)));

            result.Should().NotContainKey("discoveryId");
            Messages(result, "Error").Should().ContainSingle().Which.Should().Contain("not been initialized");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetTestDiscoveryResult_UnknownId_StatusUnknown()
        {
            var result = Parse(CreateAutomation().GetTestDiscoveryResult("no such id"));

            result["status"].Should().Be(TestDiscoveryJobs.Unknown);
            result["discoveryId"].Should().Be("no such id");
        }

        [TestMethod]
        [TestCategory(Integration)]
        public void GetSolutionExecutables_ProjectsOfAllKinds_OutputsAndExecutablesAreReported()
        {
            string solution = Path.Combine(Path.GetTempPath(), "GtaAutomationTests.sln");
            _mockEnvironment.Setup(e => e.SolutionFullName).Returns(solution);
            _mockEnvironment.Setup(e => e.GetProjectOutputs()).Returns(new List<ProjectOutput>
            {
                new ProjectOutput { Name = "Tests", ProjectFile = "Tests.vcxproj", Configuration = "Debug|Win32", KeyOutput = Path.GetFullPath(TestResources.Tests_DebugX86) },
                new ProjectOutput { Name = "Benchmarks", Configuration = "Debug|Win32", KeyOutput = Path.GetFullPath(TestResources.BenchmarkTests_DebugX86) },
                new ProjectOutput { Name = "Library", Configuration = "Release|Win32", KeyOutput = Path.GetFullPath(TestResources.DllTestsDll_ReleaseX86) },
                new ProjectOutput { Name = "Tool", Configuration = "Debug|Win32", KeyOutput = Path.GetFullPath(TestResources.Tests_DebugX86), IsTestProject = false },
                new ProjectOutput { Name = "Broken", Error = "The project has no active configuration" }
            });

            var result = Parse(CreateAutomation(solutionSettings: false).GetSolutionExecutables());

            Messages(result, "Error").Should().BeEmpty();
            var solutionInfo = (Dictionary<string, object>)result["solution"];
            solutionInfo["fullName"].Should().Be(solution);
            solutionInfo["settingsFile"].Should().Be(Path.ChangeExtension(solution, ".gta.runsettings"));
            solutionInfo["settingsFileExists"].Should().Be(false);
            var projects = ((ArrayList)result["projects"]).Cast<Dictionary<string, object>>().ToDictionary(p => (string)p["name"]);
            projects.Keys.Should().Equal("Tests", "Benchmarks", "Library", "Tool", "Broken");

            var tests = projects["Tests"];
            tests["projectFile"].Should().Be("Tests.vcxproj");
            tests["configuration"].Should().Be("Debug|Win32");
            tests["isTestProject"].Should().BeNull();
            tests["isTestContainer"].Should().Be(true);
            var testsExecutable = (Dictionary<string, object>)tests["executable"];
            testsExecutable["testFramework"].Should().Be("GoogleTest");
            testsExecutable["wouldBeDiscovered"].Should().Be(true);
            ((Dictionary<string, object>)projects["Benchmarks"]["executable"])["testFramework"].Should().Be("GoogleBenchmark");

            projects["Library"]["isTestContainer"].Should().Be(true);
            projects["Library"]["executable"].Should().BeNull("only .exe files are test executables");
            projects["Tool"]["isTestProject"].Should().Be(false);
            projects["Tool"]["isTestContainer"].Should().Be(false, "Test Explorer ignores projects setting IsTestProject to false");
            projects["Broken"]["output"].Should().BeNull();
            projects["Broken"]["isTestContainer"].Should().Be(false);
            projects["Broken"]["error"].Should().Be("The project has no active configuration");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetSolutionExecutables_NoSolution_NoProjects()
        {
            _mockEnvironment.Setup(e => e.GetProjectOutputs()).Returns(new List<ProjectOutput>());

            var result = Parse(CreateAutomation().GetSolutionExecutables());

            result["solution"].Should().BeNull();
            ((ArrayList)result["projects"]).Should().BeEmpty();
            Messages(result, "Error").Should().BeEmpty();
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetSolutionExecutables_NotInitialized_ErrorMessage()
        {
            var result = Parse(CreateAutomation(initialized: false).GetSolutionExecutables());

            result.Should().NotContainKey("projects");
            Messages(result, "Error").Should().ContainSingle().Which.Should().Contain("not been initialized");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetTestOutput_PaneWithText_LastLinesAreReturned()
        {
            _mockEnvironment.Setup(e => e.GetOutputPaneText(GoogleTestAdapterAutomation.TestsOutputPane))
                .Returns("line 1\r\nline 2\r\nline 3\r\n");
            var automation = CreateAutomation();

            var result = Parse(automation.GetTestOutput(2));

            result["available"].Should().Be(true);
            result["lineCount"].Should().Be(3);
            result["text"].Should().Be("line 2" + Environment.NewLine + "line 3");
            Parse(automation.GetTestOutput(0))["text"].Should().Be(string.Join(Environment.NewLine, "line 1", "line 2", "line 3"));
            Parse(automation.GetTestOutput(10))["text"].Should().Be(string.Join(Environment.NewLine, "line 1", "line 2", "line 3"));
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetTestOutput_NoPane_NotAvailableWithWarning()
        {
            var result = Parse(CreateAutomation().GetTestOutput(0));

            result["available"].Should().Be(false);
            result.Should().NotContainKey("text");
            Messages(result, "Warning").Should().ContainSingle().Which.Should().Contain("no pane 'Tests'");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetTestOutput_TextNotProvidedByVisualStudio_NotAvailableWithWarning()
        {
            _mockEnvironment.Setup(e => e.GetOutputPaneText(It.IsAny<Guid>()))
                .Throws(new NotSupportedException("This version of Visual Studio does not provide the text"));

            var result = Parse(CreateAutomation().GetTestOutput(0));

            result["available"].Should().Be(false);
            Messages(result, "Warning").Should().ContainSingle().Which.Should().Contain("does not provide the text");
            Messages(result, "Error").Should().BeEmpty();
        }

        // callers (e.g. agents) correct their calls from the messages, so these must say what to pass

        [TestMethod]
        [TestCategory(Unit)]
        public void CheckExecutable_NoPath_ErrorNamesParameterAndWhatToPass()
        {
            foreach (string executable in new[] { null, "", "  " })
            {
                var result = Parse(CreateAutomation().CheckExecutable(executable));

                result.Should().NotContainKey("wouldBeDiscovered");
                Messages(result, "Error").Should().ContainSingle().Which.Should()
                    .Be("Parameter executable is missing: pass the full path of a test executable (.exe)");
            }
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetEffectiveSettings_RelativePath_ErrorAsksForFullPath()
        {
            foreach (string executable in new[] { @"Debug\Tests.exe", @"\Debug\Tests.exe" })
            {
                var result = Parse(CreateAutomation().GetEffectiveSettings(executable));

                result.Should().NotContainKey("settings");
                Messages(result, "Error").Should().ContainSingle().Which.Should()
                    .StartWith($"Parameter executable is '{executable}', which is no full path: pass the full path of a test executable (.exe)")
                    .And.Contain(Environment.CurrentDirectory);
            }
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void StartTestDiscovery_InvalidPath_ErrorAndNoDiscovery()
        {
            var result = Parse(CreateAutomation().StartTestDiscovery(@"C:\Tests|Debug\Tests.exe"));

            result.Should().NotContainKey("discoveryId");
            Messages(result, "Error").Should().ContainSingle().Which.Should()
                .StartWith(@"Parameter executable is 'C:\Tests|Debug\Tests.exe', which is no valid path (")
                .And.EndWith("): pass the full path of a test executable (.exe)");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void CheckExecutable_ExecutableDoesNotExist_WarningPointsToSolutionExecutables()
        {
            var result = Parse(CreateAutomation().CheckExecutable(@"C:\NotExisting\Tests.exe"));

            Messages(result, "Warning").Should().ContainSingle().Which.Should()
                .Contain(@"'C:\NotExisting\Tests.exe' does not exist").And.Contain("GetSolutionExecutables()");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetRunSettings_FileIsNoXml_ErrorNamesFile()
        {
            string runSettingsFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(runSettingsFile, "no xml");

                var result = Parse(CreateAutomation().GetRunSettings(runSettingsFile));

                result.Should().NotContainKey("runSettings");
                Messages(result, "Error").Should().ContainSingle().Which.Should()
                    .StartWith($"Parameter runSettingsFile: '{runSettingsFile}' is no valid XML file: ");
            }
            finally
            {
                File.Delete(runSettingsFile);
            }
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetRunSettings_RelativePath_ErrorAsksForFullPath()
        {
            var result = Parse(CreateAutomation().GetRunSettings("My.runsettings"));

            Messages(result, "Error").Should().ContainSingle().Which.Should()
                .StartWith("Parameter runSettingsFile is 'My.runsettings', which is no full path: pass a .runsettings file (or null for none)");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetTestDiscoveryResult_UnknownId_WarningExplainsWhichIdsAreKnown()
        {
            var result = Parse(CreateAutomation().GetTestDiscoveryResult("no such id"));

            Messages(result, "Warning").Should().ContainSingle().Which.Should()
                .StartWith("Unknown discoveryId 'no such id': pass the discoveryId returned by StartTestDiscovery()");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void GetTestOutput_NegativeMaxLines_ErrorSaysWhatToPass()
        {
            _mockEnvironment.Setup(e => e.GetOutputPaneText(It.IsAny<Guid>())).Returns("line\r\n");

            var result = Parse(CreateAutomation().GetTestOutput(-1));

            result.Should().NotContainKey("text");
            Messages(result, "Error").Should().ContainSingle().Which.Should()
                .Be("Parameter maxLines is -1: pass the number of lines to return from the end of the pane, or 0 for all lines");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void Write_SpecialCharactersAndValues_ValidJson()
        {
            string json = Json.Write(new JsonObject
            {
                { "string", "quote \" backslash \\ newline \n tab \t control \u0001 umlaut \u00e4" },
                { "number", 42 },
                { "bool", true },
                { "null", null },
                { "enum", DayOfWeek.Monday },
                { "list", new List<int> { 1, 2 } },
                { "dictionary", new Dictionary<string, string> { { "key", "value" } } }
            });

            var result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            result["string"].Should().Be("quote \" backslash \\ newline \n tab \t control \u0001 umlaut \u00e4");
            result["number"].Should().Be(42);
            result["bool"].Should().Be(true);
            result["null"].Should().BeNull();
            result["enum"].Should().Be("Monday");
            ((ArrayList)result["list"]).Should().Equal(1, 2);
            ((Dictionary<string, object>)result["dictionary"])["key"].Should().Be("value");
        }
    }
}
