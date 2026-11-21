<#
.SYNOPSIS
Shows how scripts and MCP servers can use Google Test Adapter's automation object from outside Visual Studio.

.DESCRIPTION
Connects to a running Visual Studio (or starts one), optionally opens a solution, and prints the results of the
methods of GTA's automation object DTE.GetObject("GoogleTestAdapter") for the given executable (by default, the first
of the solution's executables GTA would discover), and some of GTA's options (DTE.Properties). Run it with Windows
PowerShell; all results are JSON strings.

For agents and scripts, the agent skill's script agent-plugin\skills\google-test-adapter\scripts\gta.ps1 wraps all of
this: it selects among several running Visual Studio instances, prints one JSON object per command, describes dialogs
which keep Visual Studio from responding, and answers wrong calls with errors saying what to do instead.

.EXAMPLE
.\AutomationExample.ps1 -Solution ..\SampleTests\SampleTests.sln
.EXAMPLE
.\AutomationExample.ps1 -Solution ..\SampleTests\SampleTests.sln -Executable ..\out\binaries\SampleTests\Debug\Tests_gta.exe
#>
param(
    [string]$Solution,
    [string]$Executable,
    [string]$VisualStudioVersion = "18.0",
    # starts Visual Studio if none is running, and quits it at the end
    [switch]$Start
)
$ErrorActionPreference = "Stop"

# Visual Studio rejects calls while it is busy (RPC_E_CALL_REJECTED, RPC_E_SERVERCALL_RETRYLATER)
function Invoke-Com([scriptblock]$Call) {
    for ($i = 0; ; $i++) {
        try { return & $Call }
        catch {
            if ($i -lt 120 -and $_.Exception.ToString() -match '0x80010001|0x8001010A') { Start-Sleep -Milliseconds 500; continue }
            throw
        }
    }
}

# methods added after GTA 0.21.0 are missing from older versions (DISP_E_UNKNOWNNAME)
function Invoke-Gta([string]$Method, [object[]]$Arguments, [string]$Version) {
    try {
        return Invoke-Com { $gta.GetType().InvokeMember($Method, [Reflection.BindingFlags]::InvokeMethod, $null, $gta, $Arguments) }
    } catch {
        if ($_.Exception.ToString() -match '0x80020006|Unknown name') { return "($Method() requires Google Test Adapter $Version or later)" }
        throw
    }
}

function Get-Dte {
    try { return [Runtime.InteropServices.Marshal]::GetActiveObject("VisualStudio.DTE.$VisualStudioVersion") } catch { return $null }
}

$dte = Get-Dte
$process = $null
if (!$dte) {
    if (!$Start) { throw "No Visual Studio $VisualStudioVersion is running (use -Start)" }
    $installation = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -version "[$VisualStudioVersion,$([int]$VisualStudioVersion.Split('.')[0] + 1).0)" -property installationPath | Select-Object -First 1
    $process = Start-Process "$installation\Common7\IDE\devenv.exe" -PassThru
    for ($i = 0; $i -lt 120 -and !$dte; $i++) { Start-Sleep -Seconds 1; $dte = Get-Dte }
    if (!$dte) { throw "Visual Studio did not start" }
}

try {
    if ($Solution) {
        Invoke-Com { $dte.Solution.Open((Resolve-Path $Solution).Path) }
    }

    # loads GTA if necessary
    $gta = Invoke-Com { $dte.GetObject("GoogleTestAdapter") }

    "GetVersion():"
    Invoke-Com { $gta.GetVersion() }
    ""
    "GetRunSettings(`$null):"
    Invoke-Com { $gta.GetRunSettings($null) }
    ""
    # the outputs of the solution's projects as Test Explorer gives them to the test adapters, and what GTA makes of them
    "GetSolutionExecutables():"
    $executables = Invoke-Gta "GetSolutionExecutables" @() "0.23.0"
    if ($executables.StartsWith("{")) {
        $projects = @(($executables | ConvertFrom-Json).projects)
        foreach ($project in $projects) {
            $verdict = if ($project.executable) { "$($project.executable.testFramework), would be discovered: $($project.executable.wouldBeDiscovered)" } else { "no executable" }
            "  $($project.name) ($($project.configuration)): $(if ($project.output) { $project.output } else { $project.error }) - $verdict"
        }
        if (!$Executable) {
            $Executable = ($projects | Where-Object { $_.executable.wouldBeDiscovered } | Select-Object -First 1).output
        }
    } else {
        $executables
    }

    if ($Executable) {
        # GTA needs full paths
        $Executable = [IO.Path]::GetFullPath($Executable)
        ""
        "GetEffectiveSettings('$Executable'):"
        Invoke-Com { $gta.GetEffectiveSettings($Executable) }
        ""
        "CheckExecutable('$Executable'):"
        Invoke-Com { $gta.CheckExecutable($Executable) }
        ""
        # discovery runs the executable in the background; its result is polled
        "StartTestDiscovery('$Executable'):"
        $started = Invoke-Com { $gta.StartTestDiscovery($Executable) }
        $started
        $discoveryId = ($started | ConvertFrom-Json).discoveryId
        if ($discoveryId) {
            do {
                Start-Sleep -Milliseconds 200
                $result = Invoke-Com { $gta.GetTestDiscoveryResult($discoveryId) } | ConvertFrom-Json
            } while ($result.status -eq "Running")
            "GetTestDiscoveryResult('$discoveryId'): status $($result.status) after $($result.durationInSeconds) s, $(@($result.tests).Count) tests"
            $result.discoveryMessages | ForEach-Object { "  $($_.severity): $($_.message)" }
            $result.tests | Select-Object -First 3 | ForEach-Object { "  $($_.fullyQualifiedName) ($($_.kind)) at $($_.codeFilePath):$($_.lineNumber)" }
        }
    }

    ""
    # what Test Explorer and GTA have logged (GTA shows the pane in the Output window if necessary)
    "GetTestOutput(20):"
    $output = Invoke-Gta "GetTestOutput" @(20) "0.23.0"
    if ($output.StartsWith("{")) {
        $output = $output | ConvertFrom-Json
        if ($output.available) { $output.text } else { $output.messages | ForEach-Object { "  $($_.severity): $($_.message)" } }
    } else {
        $output
    }

    ""
    "Options (DTE.Properties):"
    # the properties are looked up in one call: PowerShell would unroll a returned Properties collection
    foreach ($name in "EnableParallelTestExecution", "TestTimeoutInSeconds", "WorkingDir") {
        "  $name = $(Invoke-Com { $dte.Properties("Google Test Adapter", "Test Execution").Item($name).Value })"
    }
} finally {
    if ($process) {
        try { Invoke-Com { $dte.Quit() } } catch { }
    }
}
