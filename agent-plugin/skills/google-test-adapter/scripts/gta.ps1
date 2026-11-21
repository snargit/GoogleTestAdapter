<#
.SYNOPSIS
Inspects and configures Google Test Adapter (GTA) in a running Visual Studio (2017 to 2026).

.DESCRIPTION
Each command prints one JSON object on stdout. Exit codes:
  0  success
  1  the script could not do what was asked: the result is {"error": "..."}, which says what to do differently (e.g. a
     missing argument, no running Visual Studio)
  2  GTA reported errors: see the result's "messages" (and "discoveryMessages" of discover)
Warnings GTA reports are in "messages" as well.

Commands:
  instances                       the running Visual Studio instances with their solutions
  version                         GTA's and Visual Studio's versions, and the open solution
  executables                     the solution's project outputs, and which of them GTA discovers tests in
  check <executable>              whether GTA would discover the tests of an executable, and why (does not run it)
  settings <executable>           the settings GTA uses for an executable (options, solution and project settings)
  runsettings [<file>]            the run settings GTA provides to Test Explorer (optionally with a .runsettings file)
  discover <executable>           discovers the tests of an executable as GTA does (runs it with --gtest_list_tests or
                                  --benchmark_list_tests)
  output [<lines>]                the end of the Output window's "Tests" pane (default: 200 lines; 0 for all)
  options [<page>]                GTA's options (Tools > Options > Google Test Adapter): pages "General",
                                  "Test Discovery", "Test Execution", "Google Test"
  set-option <page> <name> <value>
                                  changes one of GTA's options; options apply to all solutions, so ask the user first

With several Visual Studio instances running, select one with -ProcessId or -Solution (path or name of the .sln).

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File gta.ps1 executables
.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File gta.ps1 check C:\src\out\Debug\MyTests.exe -Solution MySolution
#>
param(
    [Parameter(Position = 0)][string]$Command = "help",
    [Parameter(Position = 1)][string]$Argument,
    [Parameter(Position = 2)][string]$Name,
    [Parameter(Position = 3)][string]$Value,
    # the process id of the Visual Studio instance to use
    [string]$ProcessId,
    # the path or file name of the solution, to select the Visual Studio instance which has it open
    [string]$Solution,
    # discover: the maximum number of tests to print (0 for all)
    [string]$MaxTests = "100",
    # discover: how long to wait for the discovery, in seconds
    [string]$TimeoutSeconds = "600",
    # anything else, which is reported as an error (parameters are strings for the same reason: PowerShell's own errors
    # would not be JSON)
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$UnexpectedArguments
)

# DTE is only available from Windows PowerShell (.NET Framework), so PowerShell 7 runs the script there
if ($PSVersionTable.PSEdition -eq "Core") {
    $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath)
    foreach ($parameter in $PSBoundParameters.GetEnumerator()) {
        if ($parameter.Key -eq "UnexpectedArguments") { continue }
        $arguments += "-$($parameter.Key)"
        $arguments += "$($parameter.Value)"
    }
    if ($UnexpectedArguments) { $arguments += $UnexpectedArguments }
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" @arguments
    exit $LASTEXITCODE
}

$ErrorActionPreference = "Stop"
# an empty value counts (e.g. set-option clearing a string option)
$boundArgumentCount = @("Argument", "Name", "Value" | Where-Object { $PSBoundParameters.ContainsKey($_) }).Count
# e.g. paths and test names may contain any characters
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Web.Extensions
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$serializer.MaxJsonLength = [int]::MaxValue
$serializer.RecursionLimit = 100

# the methods added to GTA's automation object after 0.21.0, with the GTA version providing them
$methodVersions = @{ "GetSolutionExecutables" = "0.23.0"; "GetTestOutput" = "0.23.0" }
$optionPages = "General", "Test Discovery", "Test Execution", "Google Test"
# DTE returns the values of enumeration options as numbers (a test of GTA checks that these are GTA's enumerations)
$enumOptions = @{
    "OutputMode" = @{ None = 0; Info = 10; Debug = 20; Verbose = 30 }
    "TimestampMode" = @{ Automatic = 0; PrintTimestamp = 1; DoNotPrintTimestamp = 2 }
    "SeverityMode" = @{ Automatic = 0; PrintSeverity = 1; DoNotPrintSeverity = 2 }
    "SummaryMode" = @{ Never = 0; Error = 1; WarningOrError = 2 }
    "DebuggerKind" = @{ VsTestFramework = 0; Native = 1; ManagedAndNative = 2; ManagedCoreAndNative = 3 }
    "MissingTestsReportMode" = @{ DoNotReport = 0; ReportAsNotFound = 1; ReportAsSkipped = 2; ReportAsFailed = 3 }
}

function Get-EnumName([string]$option, $number) {
    if (-not $enumOptions.ContainsKey($option)) { return $number }
    foreach ($entry in $enumOptions[$option].GetEnumerator()) {
        if ($entry.Value -eq $number) { return $entry.Key }
    }
    return $number
}

function Write-Result($result) {
    # the serializer escapes ' < > & although JSON does not need it, which makes paths and messages hard to read
    $json = [regex]::Replace($serializer.Serialize($result), '(?<!\\)((?:\\\\)*)\\u00(27|3c|3e|26)', {
        param($match)
        $match.Groups[1].Value + [char][Convert]::ToInt32($match.Groups[2].Value, 16)
    })
    [Console]::Out.WriteLine($json)
}

function Fail([string]$message) {
    Write-Result @{ error = $message }
    exit 1
}

# the result of a GTA method; exit code 2 tells callers that GTA reported errors
function Write-GtaResult($result) {
    Write-Result $result
    $messages = @($result["messages"]) + @($result["discoveryMessages"])
    if (@($messages | Where-Object { $_ -and $_["severity"] -eq "Error" }).Count -gt 0) { exit 2 }
    exit 0
}

# the arguments each command takes; the number is how many of them are required
$usages = [ordered]@{
    "instances" = @(0, "instances")
    "version" = @(0, "version")
    "executables" = @(0, "executables")
    "check" = @(1, "check <executable>   (the path of a test executable, e.g. an 'output' listed by 'executables')")
    "settings" = @(1, "settings <executable>   (the path of a test executable)")
    "runsettings" = @(0, "runsettings [<file>]   (optionally the path of a .runsettings file selected in Test Explorer)")
    "discover" = @(1, "discover <executable> [-MaxTests <n>] [-TimeoutSeconds <s>]   (the path of a test executable)")
    "output" = @(0, "output [<lines>]   (the number of lines from the end of the pane; 0 for all; default 200)")
    "options" = @(0, "options [<page>]   (page: $($optionPages -join ', '))")
    "set-option" = @(3, "set-option <page> <name> <value>   (page: $($optionPages -join ', '); 'options <page>' lists the names)")
    "help" = @(0, "help")
}

function Fail-Usage([string]$problem) {
    if ($Command -eq "help") {
        Fail "$problem. Commands: $(($usages.Keys | Where-Object { $_ -ne 'help' }) -join ', '); 'help' describes them"
    }
    Fail "$problem. Usage: gta.ps1 $($usages[$Command][1])"
}

function Get-Number([string]$parameter, [string]$text, [int]$minimum) {
    $number = 0
    if (-not [int]::TryParse($text, [ref]$number) -or $number -lt $minimum) {
        $expected = if ($minimum -eq [int]::MinValue) { "a whole number" } else { "a whole number of at least $minimum" }
        Fail-Usage "$parameter must be $expected, not '$text'"
    }
    return $number
}

function Test-Arguments {
    if (-not $usages.Contains($Command)) {
        Fail "Unknown command '$Command'. Commands: $(($usages.Keys | Where-Object { $_ -ne 'help' }) -join ', '); 'help' describes them"
    }
    if ($UnexpectedArguments) {
        Fail-Usage "Unexpected arguments: $($UnexpectedArguments -join ' ') (put paths containing spaces in quotes)"
    }
    $given = $boundArgumentCount
    $required = $usages[$Command][0]
    $allowed = if ($usages[$Command][1] -match '\[<') { $required + 1 } else { $required }
    if ($given -lt $required) { Fail-Usage "'$Command' needs $required argument(s), got $given" }
    if ($given -gt $allowed) {
        Fail-Usage "'$Command' takes at most $allowed argument(s), got $given ($((@($Argument, $Name, $Value) | Where-Object { $_ }) -join ', '); put paths containing spaces in quotes)"
    }
    if ($ProcessId) { $null = Get-Number "-ProcessId" $ProcessId 1 }
}

# the process id of the Visual Studio instance used, for describing its dialogs
$visualStudioProcessId = 0

# the dialogs (windows within Visual Studio's windows) Visual Studio shows, with their texts and buttons
function Get-Dialogs([int]$processId) {
    try {
        Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
        $automation = [System.Windows.Automation.AutomationElement]
        $windowType = New-Object System.Windows.Automation.PropertyCondition($automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
        $ofProcess = New-Object System.Windows.Automation.PropertyCondition($automation::ProcessIdProperty, $processId)
        $dialogs = @()
        foreach ($window in $automation::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $ofProcess)) {
            # top-level windows other than the main window (e.g. GTA's release notes), and windows within windows
            $candidates = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $windowType))
            if ($window.Current.Name -notmatch 'Microsoft Visual Studio') { $candidates += $window }
            foreach ($dialog in $candidates) {
                $texts = @(); $buttons = @()
                foreach ($element in $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
                    $type = $element.Current.ControlType
                    if ($type -eq [System.Windows.Automation.ControlType]::Button -and $element.Current.Name) { $buttons += $element.Current.Name }
                    elseif (($type -eq [System.Windows.Automation.ControlType]::Text -or $type -eq [System.Windows.Automation.ControlType]::Edit) -and $element.Current.Name -and $element.Current.Name -notmatch '^_') { $texts += $element.Current.Name }
                }
                $text = ($texts | Select-Object -Unique) -join " "
                if ($text.Length -gt 600) { $text = $text.Substring(0, 600) + "..." }
                $dialogs += "'$($dialog.Current.Name)': $text (buttons: $(($buttons | Select-Object -Unique) -join ', '))"
            }
        }
        return $dialogs
    } catch {
        return @()
    }
}

# Visual Studio rejects calls while it is busy (RPC_E_CALL_REJECTED, RPC_E_SERVERCALL_RETRYLATER), e.g. while it
# loads a solution or shows a modal dialog
function Invoke-Com([scriptblock]$Call) {
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $dialogsChecked = $false
    while ($true) {
        try { return & $Call }
        catch {
            if ($_.Exception.ToString() -match '0x80010001|0x8001010A') {
                # a dialog does not close by itself: the user has to answer it
                if (-not $dialogsChecked -and $visualStudioProcessId -and $stopwatch.Elapsed.TotalSeconds -gt 10) {
                    $dialogsChecked = $true
                    $dialogs = @(Get-Dialogs $visualStudioProcessId)
                    if ($dialogs.Count -gt 0) {
                        Fail "Visual Studio (process $visualStudioProcessId) does not respond since it shows a dialog, which the user has to answer: $($dialogs -join '; ')"
                    }
                }
                if ($stopwatch.Elapsed.TotalSeconds -gt 120) {
                    Fail "Visual Studio (process $visualStudioProcessId) has been busy for 2 minutes (e.g. loading a solution, or showing a dialog, which the user has to answer); try again later"
                }
                Start-Sleep -Milliseconds 500
                continue
            }
            throw
        }
    }
}

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public static class GtaRunningObjectTable
{
    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx context);

    // the DTE objects of the running Visual Studio instances, by display name ("!VisualStudio.DTE.<version>:<pid>")
    public static Dictionary<string, object> GetDteObjects()
    {
        var result = new Dictionary<string, object>();
        IRunningObjectTable table;
        if (GetRunningObjectTable(0, out table) != 0)
            return result;
        IEnumMoniker monikers;
        table.EnumRunning(out monikers);
        var moniker = new IMoniker[1];
        while (monikers.Next(1, moniker, IntPtr.Zero) == 0)
        {
            IBindCtx context;
            CreateBindCtx(0, out context);
            string name;
            moniker[0].GetDisplayName(context, null, out name);
            if (name.StartsWith("!VisualStudio.DTE.", StringComparison.Ordinal))
            {
                object dte;
                if (table.GetObject(moniker[0], out dte) == 0)
                    result[name] = dte;
            }
        }
        return result;
    }
}
"@

function Get-Instances {
    $instances = @()
    foreach ($entry in [GtaRunningObjectTable]::GetDteObjects().GetEnumerator()) {
        if ($entry.Key -notmatch '^!VisualStudio\.DTE\.(\d+\.\d+):(\d+)$') { continue }
        $dte = $entry.Value
        # one attempt only: a busy instance would block the listing
        $solutionName = $null
        $busy = $false
        try { $solutionName = $dte.Solution.FullName } catch { $busy = $_.Exception.ToString() -match '0x80010001|0x8001010A' }
        $instances += [pscustomobject]@{
            dte = $dte
            dteVersion = $Matches[1]
            processId = [int]$Matches[2]
            solution = if ($solutionName) { $solutionName } else { $null }
            busy = $busy
        }
    }
    return $instances
}

function Get-Instance {
    $instances = @(Get-Instances)
    if ($instances.Count -eq 0) { Fail "No Visual Studio (2017 to 2026) is running" }
    $selected = $instances
    if ($ProcessId) {
        $selected = @($instances | Where-Object { $_.processId -eq $ProcessId })
    } elseif ($Solution) {
        $selected = @($instances | Where-Object {
            $_.solution -and ($_.solution -ieq $Solution -or [IO.Path]::GetFileName($_.solution) -ieq $Solution -or
                [IO.Path]::GetFileNameWithoutExtension($_.solution) -ieq $Solution) })
    }
    if ($selected.Count -ne 1) {
        $list = ($instances | ForEach-Object { "process $($_.processId): $(if ($_.solution) { $_.solution } elseif ($_.busy) { 'busy, solution unknown' } else { 'no solution' })" }) -join "; "
        if ($selected.Count -eq 0) { Fail "No running Visual Studio matches the selection ($list)" }
        Fail "Several Visual Studio instances are running, select one with -ProcessId or -Solution ($list)"
    }
    $script:visualStudioProcessId = $selected[0].processId
    return $selected[0]
}

function Get-Gta($instance) {
    try {
        return Invoke-Com { $instance.dte.GetObject("GoogleTestAdapter") }
    } catch {
        Fail "Google Test Adapter 0.21.0 or later is not installed in Visual Studio (process $($instance.processId)): $($_.Exception.Message)"
    }
}

# calls a method of GTA's automation object, which returns a JSON object
function Invoke-Gta($gta, [string]$method, [object[]]$arguments = @()) {
    try {
        $json = Invoke-Com { $gta.GetType().InvokeMember($method, [Reflection.BindingFlags]::InvokeMethod, $null, $gta, $arguments) }
    } catch {
        if ($methodVersions.ContainsKey($method) -and $_.Exception.ToString() -match '0x80020006|DISP_E_UNKNOWNNAME|Unknown name') {
            Fail "$method() requires Google Test Adapter $($methodVersions[$method]) or later"
        }
        throw
    }
    return $serializer.DeserializeObject($json)
}

# relative paths are resolved against the current directory (GTA needs full paths)
function Get-FullPath([string]$path) {
    try {
        return [IO.Path]::GetFullPath([IO.Path]::Combine((Get-Location).ProviderPath, $path))
    } catch {
        Fail-Usage "'$path' is no valid path ($($_.Exception.InnerException.Message))"
    }
}

function Get-Options($dte, [string[]]$pages) {
    $result = [ordered]@{}
    foreach ($page in $pages) {
        if ($optionPages -notcontains $page) { Fail-Usage "Unknown options page '$page' (pages: $($optionPages -join ', '); put names containing spaces in quotes)" }
        # the properties are read in one call: PowerShell would unroll a returned Properties collection
        $values = Invoke-Com {
            $pageValues = [ordered]@{}
            foreach ($property in $dte.Properties("Google Test Adapter", $page)) {
                try { $pageValues[$property.Name] = $property.Value } catch { $pageValues[$property.Name] = $null }
            }
            $pageValues
        }
        foreach ($option in @($values.Keys)) {
            $values[$option] = Get-EnumName $option $values[$option]
        }
        $result[$page] = $values
    }
    return $result
}

function Convert-OptionValue([string]$option, $current, [string]$text) {
    if ($enumOptions.ContainsKey($option)) {
        $names = $enumOptions[$option]
        $match = @($names.Keys | Where-Object { $_ -ieq $text })
        if ($match.Count -eq 1) { return $names[$match[0]] }
        if ($text -match '^\d+$' -and $names.Values -contains [int]$text) { return [int]$text }
        Fail-Usage "Option $option needs one of $(($names.Keys | Sort-Object { $names[$_] }) -join ', '), not '$text'"
    }
    if ($current -is [bool]) {
        if ($text -notmatch '^(true|false)$') { Fail-Usage "Option $option needs true or false, not '$text'" }
        return $text -ieq "true"
    }
    if ($current -is [int]) { return Get-Number "Option $option" $text ([int]::MinValue) }
    return $text
}

Test-Arguments
try {
    switch ($Command) {
        "help" {
            Get-Help $PSCommandPath -Detailed | Out-String | Write-Output
            exit 0
        }
        "instances" {
            Write-Result @{ instances = @(Get-Instances | ForEach-Object {
                [ordered]@{ processId = $_.processId; dteVersion = $_.dteVersion; solution = $_.solution; busy = $_.busy } }) }
        }
        "version" {
            $instance = Get-Instance
            $result = Invoke-Gta (Get-Gta $instance) "GetVersion"
            $result["visualStudio"] = [ordered]@{
                processId = $instance.processId
                dteVersion = $instance.dteVersion
                version = (Invoke-Com { $instance.dte.Version })
                solution = $instance.solution
            }
            Write-GtaResult $result
        }
        "executables" {
            $instance = Get-Instance
            Write-GtaResult (Invoke-Gta (Get-Gta $instance) "GetSolutionExecutables")
        }
        "check" {
            $executable = Get-FullPath $Argument
            $instance = Get-Instance
            Write-GtaResult (Invoke-Gta (Get-Gta $instance) "CheckExecutable" @($executable))
        }
        "settings" {
            $executable = Get-FullPath $Argument
            $instance = Get-Instance
            Write-GtaResult (Invoke-Gta (Get-Gta $instance) "GetEffectiveSettings" @($executable))
        }
        "runsettings" {
            $file = if ($Argument) { Get-FullPath $Argument } else { $null }
            $instance = Get-Instance
            Write-GtaResult (Invoke-Gta (Get-Gta $instance) "GetRunSettings" @($file))
        }
        "discover" {
            $executable = Get-FullPath $Argument
            $maxTestCount = Get-Number "-MaxTests" $MaxTests 0
            $timeout = Get-Number "-TimeoutSeconds" $TimeoutSeconds 1
            $instance = Get-Instance
            $gta = Get-Gta $instance
            $started = Invoke-Gta $gta "StartTestDiscovery" @($executable)
            if (-not $started["discoveryId"]) { Write-GtaResult $started }
            $stopwatch = [Diagnostics.Stopwatch]::StartNew()
            do {
                Start-Sleep -Milliseconds 500
                $result = Invoke-Gta $gta "GetTestDiscoveryResult" @($started["discoveryId"])
                if ($stopwatch.Elapsed.TotalSeconds -gt $timeout) {
                    $result["messages"] = @($result["messages"]) + @(@{ severity = "Warning"; message = "The discovery has not finished within $timeout s (-TimeoutSeconds); it goes on in Visual Studio, and discovering again starts another one" })
                    break
                }
            } while ($result["status"] -eq "Running")
            if ($result.ContainsKey("tests")) {
                $tests = @($result["tests"])
                $result["testCount"] = $tests.Count
                if ($maxTestCount -gt 0 -and $tests.Count -gt $maxTestCount) {
                    $result["tests"] = $tests[0..($maxTestCount - 1)]
                    $result["testsTruncated"] = "only the first $maxTestCount of $($tests.Count) tests are listed; -MaxTests 0 lists all"
                }
            }
            Write-GtaResult $result
        }
        "output" {
            $lines = if ($Argument) { Get-Number "<lines>" $Argument 0 } else { 200 }
            $instance = Get-Instance
            Write-GtaResult (Invoke-Gta (Get-Gta $instance) "GetTestOutput" @($lines))
        }
        "options" {
            $instance = Get-Instance
            $null = Get-Gta $instance  # loads GTA, which provides the options pages
            $pages = if ($Argument) { @($Argument) } else { $optionPages }
            Write-Result @{ options = (Get-Options $instance.dte $pages) }
        }
        "set-option" {
            $instance = Get-Instance
            $null = Get-Gta $instance
            $page = $Argument
            $old = (Get-Options $instance.dte @($page))[$page]
            if (-not $old.Contains($Name)) { Fail-Usage "Page '$page' has no option '$Name' (its options: $($old.Keys -join ', '); 'options' lists all pages)" }
            $newValue = Convert-OptionValue $Name $old[$Name] $Value
            Invoke-Com { $dte = $instance.dte; $dte.Properties("Google Test Adapter", $page).Item($Name).Value = $newValue }
            $new = Get-EnumName $Name (Invoke-Com { $instance.dte.Properties("Google Test Adapter", $page).Item($Name).Value })
            Write-Result ([ordered]@{ page = $page; name = $Name; oldValue = $old[$Name]; newValue = $new })
        }
    }
} catch {
    Fail "'$Command' failed: $($_.Exception.Message)"
}
