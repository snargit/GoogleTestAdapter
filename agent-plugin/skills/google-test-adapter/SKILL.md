---
name: google-test-adapter
description: Diagnose and configure Google Test Adapter (GTA), the Visual Studio extension that shows and runs Google Test tests and Google Benchmark benchmarks of C++ projects in Test Explorer. Use when C++ tests or benchmarks do not show up in Visual Studio's Test Explorer, show up wrongly (names, source locations, traits), fail or hang only when run from Visual Studio, or when the user wants to change how GTA runs them, for example "my gtest tests don't appear in Test Explorer", "Test Explorer can't find the DLLs my tests need", "set the working directory for my tests", "run my benchmarks with real measurements", or "what does the .gta.runsettings file do".
---

# Google Test Adapter (GTA)

GTA is a Visual Studio extension (VS 2017 to 2026) that discovers the tests of Google Test executables and the benchmarks
of Google Benchmark executables among the outputs of a solution's projects, shows them in Test Explorer, and runs them.
Its home is https://github.com/snargit/GoogleTestAdapter (a fork; the original project ended at 0.18.0).

## Tools

**`scripts/gta.ps1`** (in this skill's folder) asks GTA in the running Visual Studio, which knows the solution, its
configuration and its settings files. Run it with Windows PowerShell; each command prints one JSON object:

```
powershell -NoProfile -ExecutionPolicy Bypass -File "<this skill's folder>\scripts\gta.ps1" <command> [arguments]
```

| Command | What it does |
|---|---|
| `instances` | The running Visual Studio instances and their solutions |
| `version` | GTA's version, Visual Studio's version, the open solution |
| `executables` | The solution's project outputs, which of them Test Explorer gives to GTA, and what GTA makes of each .exe (GTA 0.23.0 or later) |
| `check <exe>` | Whether GTA would discover the tests of an executable, and why (does not run it) |
| `settings <exe>` | The settings GTA uses for an executable, after merging options, solution and project settings |
| `runsettings [<file>]` | The run settings GTA provides to Test Explorer (e.g. for `vstest.console.exe /Settings:`) |
| `discover <exe>` | Lists the tests of an executable as GTA does (runs it with `--gtest_list_tests` or `--benchmark_list_tests`) |
| `output [<lines>]` | The end of the Output window's *Tests* pane, where Test Explorer and GTA log (GTA 0.23.0 or later; default 200 lines) |
| `options [<page>]` | GTA's options (Tools > Options > Google Test Adapter) |
| `set-option <page> <name> <value>` | Changes one of GTA's options (`""` clears a string option); **ask the user first**, options apply to all their solutions |

- Just call the commands: if the installed GTA lacks a method a command needs (the versions in the table are those
  of releases; development builds may report other versions), the script says so.
- Exit code 0: success. 1: the script couldn't do what was asked; `error` says why and how to call it instead (e.g. the
  usage of the command). 2: GTA reported errors, see `messages` (and `discoveryMessages`); warnings are there as well.
- With several Visual Studio instances, add `-ProcessId <pid>` or `-Solution <name or path of the .sln>`.
- If Visual Studio shows a dialog, it doesn't respond: the script names the dialog, its text and its buttons. Tell the
  user what it says and ask them to answer it; don't guess.
- `output` shows the *Tests* pane in the Output window if necessary (Visual Studio only provides the text of panes which
  have been shown).
- If no Visual Studio is running, or GTA is older than 0.21.0, work from the files (projects, settings files) and the
  executables themselves, as below.

**The settings reference** is `references/settings.md`: every option, its XML element for settings files, its default,
placeholders such as `$(SolutionDir)`, and the separators of list-valued settings. Read it before writing settings.

## Where settings come from

From highest to lowest priority, for each executable:

1. project settings (`<ProjectSettings>`, matched by a regex on the executable's full path) of a `.runsettings` file
   selected in Test Explorer, then those of the solution settings file;
2. solution settings (`<SolutionSettings>`) of the selected file, then those of the solution settings file;
3. GTA's options (Tools > Options > Google Test Adapter).

The solution settings file is `<solution name>.gta.runsettings` next to the `.sln`. It is usually under source control
and shared, so change it only for settings the whole team needs, and keep it minimal. **Before editing any settings
file, read its first lines: tools that generate solutions may generate it too (e.g. "regenerating the solution
overwrites this file"); then change the tool's input or ask the user.** Settings take effect at the next discovery or
run; see "Test Explorer does not pick up changes" below.

**Where a value comes from:** `gta.ps1 settings <exe>` shows the merged values and which project settings matched
(`projectSettings.projectRegex`), not where each value comes from. Look for the setting from the highest priority down:
the matching project settings, then `<SolutionSettings>`, in the `.runsettings` file selected in Test Explorer (if
any) and in the solution settings file (`gta.ps1 executables` shows its path, `solution.settingsFile`), then
`gta.ps1 options`.

## Tests or benchmarks don't show up in Test Explorer

Work through these in order, and tell the user what you found at each step.

1. **Is GTA installed and is it GTA?** `gta.ps1 version`. Microsoft's *Test Adapter for Google Test* ships assemblies with
   the same names, and Visual Studio then loads either adapter: if the Tests pane (`gta.ps1 output`) mentions "Test
   Adapter for Google Test" or "Invalid run settings: Invalid GoogleTestAdapterSettings", Microsoft's adapter is
   running and ignores GTA's settings. The user has to remove it (Visual Studio Installer, individual component *Test
   Adapter for Google Test*); GTA also warns about it when Visual Studio starts.
2. **Does Test Explorer give the executable to GTA?** `gta.ps1 executables` lists each project's output for the active
   configuration.
   - The project isn't listed: it's not loaded, or the solution is in Open Folder mode (CMake), where Test Explorer gets
     the executables from CMake (use `check` on the executable directly).
   - `isTestContainer` is false: the output is unknown (see `error`), or the project sets MSBuild property
     `IsTestProject` to false.
   - `executable.exists` is false: the project hasn't been built for this configuration.
3. **Does GTA recognize it?** `executable.wouldBeDiscovered` (or `gta.ps1 check <exe>`):
   - `testDiscoveryRegex` is set and `googleTestIndicator` is null: the regex doesn't match the executable's full path.
     **A discovery regex replaces GTA's own detection**: executables that don't match are ignored even if they are
     test executables. Find where it is set (see "Where a value comes from" below), then extend it or remove it.
     If it is a global option, it applies to all the user's solutions (it may be there for other solutions): offer
     the choice of clearing it, extending it (combining regexes with `|`), or setting a regex for this solution in its
     settings file instead.
   - No regex, `googleTestIndicator` null: GTA didn't find Google Test or Google Benchmark in the executable (e.g.
     unusual linking). Create an empty file `<executable>.is_google_test` next to it, or set a regex.
   - `trust.isTrusted` false: Windows considers the file to come from another computer (downloaded, or on a network
     drive: `trust.isOnNetworkDrive`). Unblock the file (file properties), or the user can set the global option
     *Skip check of file origin* (`SkipOriginCheck` on page `General`); it is security relevant and only works as global
     option, so ask.
4. **Can GTA list the tests?** `gta.ps1 discover <exe>`: `status`, `discoveryMessages` and `tests`. It only runs
   executables GTA would discover (step 3); to check that an executable lists its tests anyway, run it yourself.
   - Exit code or crash messages: run the executable yourself with `--gtest_list_tests` (or `--benchmark_list_tests`)
     in the working directory from `gta.ps1 settings <exe>` (`discovery.workingDir`).
   - Missing DLLs (exit code -1073741515, 0xC0000135): add their folders to *PATH extension* (`<PathExtension>`).
   - Timeouts: the executable does expensive work before `main()`'s tests are listed; raise
     *Test discovery timeout in s*, or move the work into test fixtures.
   - Google Benchmark executables exit with an error on unknown flags ("unrecognized command-line flag"):
     *Additional test execution parameters* meant for Google Test break them. Move such parameters into project
     settings for the Google Test executables, or clear them for the benchmark executables with project settings
     containing an empty `<AdditionalTestExecutionParam></AdditionalTestExecutionParam>`.
5. **Discovery works, but Test Explorer still shows nothing (or old tests)?** See the next section, and read what was
   logged: `gta.ps1 output`. For more detail, *Output mode* `Debug` makes GTA log why it skips executables (option
   `OutputMode` on page `General`; ask before changing it, and set it back afterwards).

## Test Explorer does not pick up changes

Test Explorer asks the adapters for the tests of a project output only when a build changes that output (or when the
solution is opened). After changing settings that affect discovery (e.g. the discovery regex), **rebuild** the
projects whose executables are affected; building an up-to-date project, or touching the executable, is not enough.
Reopening the solution also works.

## Tests behave differently in Visual Studio

`gta.ps1 settings <exe>` shows what GTA uses: the working directory (default: the executable's folder), PATH
extension, environment variables, and parameters for discovery (`discovery`) and execution (`settings`). Compare with
how the tests run elsewhere (CTest, scripts). For CMake projects, GTA uses CTest's test properties (`WORKING_DIRECTORY`,
`ENVIRONMENT`, `TIMEOUT`, `LABELS`, `DISABLED`) unless *Use CTest test properties* is off. Results of a run are in the
Tests pane (`gta.ps1 output`); with *Print test output* on, it also shows the executables' output (only when tests are
not run in parallel).

## Configuring GTA

- For one project's executables, add project settings to the solution settings file:
  `<Settings ProjectRegex="(?i)\\MyTests\.exe$">` (a .NET regex on the full path; escape `\` and `.`).
- For the whole solution, use `<SolutionSettings>`; for the user only, their global options (with consent).
- Check the result with `gta.ps1 settings <exe>` before asking the user to run the tests.
- The XML must follow the schema (element names in `references/settings.md`); GTA logs settings it can't read.

## Benchmarks (Google Benchmark)

Each benchmark instance is a test (`BM_Copy/8`, `BM_Copy/64`) with trait *(Type,Benchmark)*. By default GTA runs them
as **dry runs** (one iteration, `--benchmark_dry_run`, Google Benchmark 1.9.1 or later; messages start with `Dry run:`).
For real measurements set *Dry runs of benchmarks* to false (`<BenchmarkDryRun>false</BenchmarkDryRun>`, e.g. in
project settings for the benchmark executables). A passing benchmark's message summarizes its measurement (e.g.
`12.3 ns (CPU 12.1 ns), 56M iterations`), its output has all statistics and counters. A benchmark fails if it calls
`State::SkipWithError()`, is skipped if it calls `State::SkipWithMessage()`; with parallel execution, benchmarks run
after the tests. Executables containing both Google Test tests and benchmarks are treated as Google Test executables.

## Without Visual Studio

`vstest.console.exe <exe> /TestAdapterPath:<GTA's extension folder> /Settings:<file>` runs GTA outside Visual Studio
(GTA's extension folder: `%LOCALAPPDATA%\Microsoft\VisualStudio\<version>_<instance>\Extensions\<random>\`, the one
containing `GoogleTestAdapter.TestAdapter.dll`). Solution settings files are not read there: get the settings with
`gta.ps1 runsettings` while Visual Studio is running, or write a `.runsettings` file with a `<GoogleTestAdapterSettings>`
section. `/ListTests` lists the tests, `/TestCaseFilter:` selects some.
