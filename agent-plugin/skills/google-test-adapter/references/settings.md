# GTA's settings

Each setting is an option in **Tools > Options > Google Test Adapter** (global, for all solutions) and an XML element
in settings files (`<sln>.gta.runsettings` next to the solution, or a `.runsettings` file selected in Test Explorer).
The *DTE name* is the name `gta.ps1 options` shows and `gta.ps1 set-option` takes; it is the XML element's name unless
stated otherwise. A test of GTA checks that this file lists every setting.

## Settings files

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <GoogleTestAdapterSettings>
    <SolutionSettings>
      <Settings>
        <!-- settings for all executables of the solution -->
        <PathExtension>$(SolutionDir)\bin\$(PlatformName)\$(ConfigurationName)</PathExtension>
      </Settings>
    </SolutionSettings>
    <ProjectSettings>
      <!-- settings for the executables whose full path matches the (.NET) regex; the first match wins -->
      <Settings ProjectRegex="(?i)\\MyTests\.exe$">
        <WorkingDir>$(SolutionDir)\data</WorkingDir>
      </Settings>
    </ProjectSettings>
  </GoogleTestAdapterSettings>
</RunSettings>
```

Precedence, highest first: project settings of a `.runsettings` file selected in Test Explorer, project settings of the
solution settings file, solution settings of the selected file, solution settings of the solution settings file, global
options. Only set what differs: every setting in a shared solution settings file overrides each developer's options.
In Open Folder mode (e.g. CMake), the settings file is `<folder>.gta.runsettings` in the opened folder (or the only
`*.gta.runsettings` file there). Without Visual Studio (vstest.console.exe), solution settings files are not read: pass
the settings with `/Settings:<file>` (GTA's automation object's `GetRunSettings()` provides them, `gta.ps1 runsettings`).

## Placeholders

| Placeholder | Value |
|---|---|
| `$(SolutionDir)` | the solution's directory (the opened folder in Open Folder mode); only inside Visual Studio |
| `$(PlatformName)`, `$(ConfigurationName)` | the active solution configuration, e.g. `x64`, `Debug`; only inside Visual Studio |
| `$(Executable)`, `$(ExecutableDir)` | the test executable, its directory |
| `$(TestDir)` | a directory the tests can use; only when running tests |
| `$(ThreadId)` | the id of the thread running the tests (parallel execution); only when running tests |
| `%<name>%` | environment variable `<name>`, e.g. `%PATH%` |

The options pages' descriptions (Tools > Options) list the placeholders each setting supports.

Lists use separators: `;` for paths (*PATH extension*, *Additional PDBs*), `//||//` between entries of *Environment
variables* (`A=1//||//B=2`), *Source path mapping* (`C:\build=>$(SolutionDir)//||//...`), and traits
(`MySuite\..*///Type,Small//||//Other\..*///Type,Large`).

## General

| Option | DTE name | XML element | Default | Meaning |
|---|---|---|---|---|
| Print test output | `PrintTestOutput` | `<PrintTestOutput>` | false | Print the executables' output to the Tests pane (sequential execution only) |
| Output mode | `OutputMode` | `<OutputMode>` | Info | `None`, `Info`, `Debug`, `Verbose`: how much GTA writes to the Tests pane; `Debug` shows why executables are (not) discovered |
| Timestamp output | `TimestampMode` | `<TimestampMode>` | Automatic | `Automatic`, `PrintTimestamp`, `DoNotPrintTimestamp` |
| Print severity | `SeverityMode` | `<SeverityMode>` | Automatic | `Automatic`, `PrintSeverity`, `DoNotPrintSeverity` |
| Print summary | `SummaryMode` | `<SummaryMode>` | WarningOrError | `Never`, `Error`, `WarningOrError`: summary of problems at the end of discovery and runs |
| Prefix output with [GTA] | `PrefixOutputWithGta` | `<PrefixOutputWithGta>` | false | |
| Skip check of file origin | `SkipOriginCheck` | `<SkipOriginCheck>` | false | **Global option only** (ignored in settings files): run executables Windows considers to come from another computer (downloaded and not unblocked, or on a network drive). Security relevant: ask the user |

## Test Discovery

| Option | DTE name | XML element | Default | Meaning |
|---|---|---|---|---|
| Regex for test discovery | `TestDiscoveryRegex` | `<TestDiscoveryRegex>` | empty | If set, **only** executables whose full path matches it are test executables (GTA's detection of Google Test and Google Benchmark is skipped); the most common reason for missing tests |
| Test discovery timeout in s | `TestDiscoveryTimeoutInSeconds` | `<TestDiscoveryTimeoutInSeconds>` | 30 | Listing the tests of an executable is canceled after this time |
| Additional test discovery parameters | `AdditionalTestDiscoveryParams` | `<AdditionalTestDiscoveryParam>` | empty | Parameters for listing the tests; if empty, *Additional test execution parameters* are used |
| Parse symbol information | `ParseSymbolInformation` | `<ParseSymbolInformation>` | true | Read source locations, traits, and namespaces from the .pdb (switch off if discovery is slow) |
| TearDownTestSuite test cases | `TearDownTestSuiteTestCases` | `<TearDownTestSuiteTestCases>` | true | A test `<suite>.TearDownTestSuite` reports failures of fixtures' `TearDownTestSuite()` |
| Test name separator | `TestNameSeparator` | `<TestNameSeparator>` | empty | Replaces `/` in test names (e.g. of parameterized tests) in Test Explorer |
| Traits: before test discovery | `TraitsRegexesBefore` | `<TraitsRegexesBefore>` | empty | Traits for tests matching regexes, overridden by the traits of `GTA_Traits.h` macros |
| Traits: after test discovery | `TraitsRegexesAfter` | `<TraitsRegexesAfter>` | empty | Traits for tests matching regexes, overriding those of the macros |

## Test Execution

| Option | DTE name | XML element | Default | Meaning |
|---|---|---|---|---|
| Working directory | `WorkingDir` | `<WorkingDir>` | `$(ExecutableDir)` | Working directory of the executables (discovery and execution) |
| PATH extension | `PathExtension` | `<PathExtension>` | empty | Appended to `PATH` (discovery and execution), e.g. for DLLs outside the executable's directory |
| Environment variables | `EnvironmentVariables` | `<EnvironmentVariables>` | empty | `NAME=value` entries separated by `//\|\|//` |
| Additional test execution parameters | `AdditionalTestExecutionParams` | `<AdditionalTestExecutionParam>` | empty | Command line parameters for running tests (and listing them, unless *Additional test discovery parameters* are set). Google Benchmark executables exit on unknown flags: set parameters meant for Google Test in project settings |
| Test timeout in s | `TestTimeoutInSeconds` | `<TestTimeoutInSeconds>` | 0 | Tests (and benchmarks) running longer are killed and fail; 0: no timeout |
| Dry runs of benchmarks | `BenchmarkDryRun` | `<BenchmarkDryRun>` | true | Run benchmarks for one iteration only (`--benchmark_dry_run`, Google Benchmark 1.9.1 or later); false for real measurements |
| Use CTest test properties | `UseCTestTestProperties` | `<UseCTestTestProperties>` | true | Use the test properties of CTest (`TIMEOUT`, `ENVIRONMENT`, `WORKING_DIRECTORY`, `LABELS`, `DISABLED`) of CMake projects |
| Additional PDBs | `AdditionalPdbs` | `<AdditionalPdbs>` | empty | File patterns of further .pdb files to read source locations from |
| Source path mapping | `SourcePathMapping` | `<SourcePathMapping>` | empty | `buildPath=>localPath` pairs for executables built elsewhere (e.g. by CI) |
| Map only missing source paths | `MapOnlyMissingSourcePaths` | `<MapOnlyMissingSourcePaths>` | false | Map only paths of files which do not exist |
| Parallel test execution | `EnableParallelTestExecution` | `<ParallelTestExecution>` | false | Run tests in several processes at once (benchmarks run after the tests) |
| Maximum number of threads | `MaxNrOfThreads` | `<MaxNrOfThreads>` | 0 | For parallel execution; 0: number of processors |
| Test setup batch file | `BatchForTestSetup` | `<BatchForTestSetup>` | empty | Batch file run before the tests (per thread) |
| Test teardown batch file | `BatchForTestTeardown` | `<BatchForTestTeardown>` | empty | Batch file run after the tests (per thread) |
| Kill processes on cancel | `KillProcessesOnCancel` | `<KillProcessesOnCancel>` | false | Kill the executables when a test run is canceled |
| Exit code test case | `ExitCodeTestCase` | `<ExitCodeTestCase>` | empty | If set, a test `<name>.<executable>` per executable passes if its exit code is 0 (e.g. for memory leak checks) |
| Behavior for missing test results | `MissingTestsReportMode` | `<MissingTestsReportMode>` | ReportAsNotFound | `DoNotReport`, `ReportAsNotFound`, `ReportAsSkipped`, `ReportAsFailed`: for tests without a result (e.g. after a crash) |
| Debugger engine | `DebuggerKind` | `<DebuggerKind>` | Native | `VsTestFramework`, `Native`, `ManagedAndNative`, `ManagedCoreAndNative` |

## Google Test

| Option | DTE name | XML element | Default | Meaning |
|---|---|---|---|---|
| Catch exceptions | `CatchExceptions` | `<CatchExceptions>` | true | `--gtest_catch_exceptions`; false lets the debugger break on exceptions |
| Break on failure | `BreakOnFailure` | `<BreakOnFailure>` | false | `--gtest_break_on_failure`, while debugging |
| Also run disabled tests | `RunDisabledTests` | `<RunDisabledTests>` | false | `--gtest_also_run_disabled_tests` |
| Number of test repetitions | `NrOfTestRepetitions` | `<NrOfTestRepetitions>` | 1 | `--gtest_repeat`; -1: forever |
| Shuffle tests per execution | `ShuffleTests` | `<ShuffleTests>` | false | `--gtest_shuffle` |
| Shuffle tests: Seed | `ShuffleTestsSeed` | `<ShuffleTestsSeed>` | 0 | `--gtest_random_seed`; 0: from the system time |

Deprecated elements, still accepted: `<DebugMode>` (use `<OutputMode>`), `<TimestampOutput>` (use `<TimestampMode>`),
`<UseNewTestExecutionFramework>` (use `<DebuggerKind>`), `<ShowReleaseNotes>`. GTA sets `<SolutionDir>`,
`<PlatformName>`, `<ConfigurationName>`, and `<DebuggingNamedPipeId>` itself; do not write them.
