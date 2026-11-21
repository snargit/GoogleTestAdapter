using System.Runtime.InteropServices;

namespace GoogleTestAdapter.VsPackage.Automation
{
    /// <summary>
    /// GTA's automation object, available as DTE.GetObject("GoogleTestAdapter"), e.g. for scripts and MCP servers. All
    /// methods return JSON objects with members "schemaVersion" (incremented on incompatible changes) and "messages"
    /// (the warnings and errors which occurred, each with "severity" and "message"); they do not throw.
    /// </summary>
    [ComVisible(true)]
    [Guid("055B4C3E-034E-447E-ABBC-F05650F4A849")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IGoogleTestAdapterAutomation
    {
        /// <returns>"name" and "version" of the installed GTA</returns>
        string GetVersion();

        /// <summary>
        /// The run settings GTA provides to Test Explorer for the open solution, i.e. GTA's options, merged with the
        /// solution's settings file and the given run settings file.
        /// </summary>
        /// <param name="runSettingsFile">a .runsettings file (as selected in Test Explorer), or null or empty</param>
        /// <returns>"runSettings": the run settings XML, e.g. for vstest.console's /Settings option (Test Explorer
        /// adds settings of Visual Studio and of other test adapters, which are not contained)</returns>
        string GetRunSettings(string runSettingsFile);

        /// <summary>
        /// The settings GTA uses for an executable of the open solution: GTA's options and the settings of the
        /// solution's settings file, overridden by the project settings matching the executable.
        /// </summary>
        /// <returns>"executable", "projectSettings" ("projectRegex" of the matching project settings, or null),
        /// "settings" (by name of the setting, see GTA's documentation), and "discovery" (the values GTA resolves for
        /// test discovery: "workingDir", "pathExtension", "environmentVariables", "additionalParameters")</returns>
        string GetEffectiveSettings(string executable);

        /// <summary>
        /// Whether GTA would discover the tests of an executable, and why (the executable is not run; Test Explorer
        /// asks GTA for the outputs of the solution's projects only).
        /// </summary>
        /// <returns>"executable", "exists", "isExe", "trust" ("securityZone", "isOnNetworkDrive", "skipOriginCheck",
        /// "isTrusted"), "googleTestIndicator" (what makes it a Google Test or Google Benchmark executable:
        /// "IndicatorFile", "GoogleTestDll", "GoogleTestFunctions", "GoogleTestMarkers", "GoogleBenchmarkDll",
        /// "GoogleBenchmarkFunctions", "GoogleBenchmarkMarkers", or "TestDiscoveryRegex"; null if it is neither),
        /// "testFramework" ("GoogleTest" or "GoogleBenchmark"; null if neither), "testDiscoveryRegex", "pdb" (null if
        /// not found), and "wouldBeDiscovered"</returns>
        string CheckExecutable(string executable);

        /// <summary>
        /// Starts discovering the tests of an executable in the background (discovery runs the executable, which
        /// takes up to the discovery timeout; COM calls would block Visual Studio's UI thread meanwhile). The
        /// executable is only run if GTA would discover its tests (see CheckExecutable()).
        /// </summary>
        /// <returns>"discoveryId" for GetTestDiscoveryResult(), and "executable"</returns>
        string StartTestDiscovery(string executable);

        /// <summary>
        /// The state and result of a test discovery started by StartTestDiscovery(); the results of the latest 32
        /// discoveries are kept.
        /// </summary>
        /// <returns>"discoveryId", "status" ("Running", "Completed", "Failed", or "Unknown" for unknown ids),
        /// "executable", "durationInSeconds", "discoveryMessages" (the warnings and errors of the discovery so far),
        /// and, if completed, "tests": "fullyQualifiedName", "displayName", "kind" ("Test", "TearDownTestSuite",
        /// "Benchmark", or "ExitCode"), "codeFilePath" and "lineNumber" (if known), "namespace", "testClass", "testGroup", "traits"
        /// ("name", "value"), and "testProperties" (the CTest test properties "workingDirectory", "environment",
        /// "labels", "timeoutInSeconds", and "disabled"; null if there are none)</returns>
        string GetTestDiscoveryResult(string discoveryId);

        // methods are only added at the end, so that the interface stays compatible with early-bound clients

        /// <summary>
        /// The executables Test Explorer gives to the test adapters for the open solution: the key outputs of the active
        /// configurations of the solution's loaded projects, except those of projects setting MSBuild property
        /// IsTestProject to false, with what GTA makes of each .exe (see CheckExecutable()). In Open Folder mode (e.g. CMake
        /// projects), the solution has no projects.
        /// </summary>
        /// <returns>"solution" ("fullName", "settingsFile" (GTA's solution settings file), "settingsFileExists"), and
        /// "projects": "name", "projectFile", "configuration" ("Configuration|Platform"), "output" (null if unknown),
        /// "isTestProject" (null if not set), "isTestContainer" (whether Test Explorer gives the output to the test
        /// adapters), "error" (why the output is unknown, or null), and "executable" (for .exe outputs: the members
        /// "exists" to "wouldBeDiscovered" of CheckExecutable()'s result; otherwise null)</returns>
        string GetSolutionExecutables();

        /// <summary>
        /// The end of the text Test Explorer and the test adapters (e.g. GTA, see its option Output mode) have written to
        /// the "Tests" pane of Visual Studio's Output window. Visual Studio only provides the text of panes which have been
        /// shown, so the pane is shown in the Output window (without activating it) if necessary.
        /// </summary>
        /// <param name="maxLines">the number of lines to return from the end of the pane; 0 for all</param>
        /// <returns>"available" (false if the pane does not exist yet, or Visual Studio does not provide its text, see
        /// "messages"), "lineCount" (the pane's number of lines), and "text"</returns>
        string GetTestOutput(int maxLines);
    }
}
