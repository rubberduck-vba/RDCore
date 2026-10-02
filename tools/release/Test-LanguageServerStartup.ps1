#Requires -Version 7.2
# smoke-launches the language server of a platform root the way a client does, and waits for it to validate and
# register every extension the tree ships.
#
# runs on a temporary copy: a launch writes Logs/ into its platform root, and the tree being tested must stay
# shippable. the environment is scrubbed of RDCORE_PLATFORM_ROOT and configuration overrides so that the tree's own
# layout and appsettings.json are what gets tested. passes once the log shows each extension validated, the
# registration done and the pipe listening; fails on timeout, early exit, an extension the server refused, or any
# [ERR]/[FTL] line. the whole process tree and the copy are removed either way.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PlatformRoot,
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"

$Source = (Resolve-Path -LiteralPath $PlatformRoot).Path
$Manifest = Get-Content -LiteralPath (Join-Path $Source "rdcore.json") -Raw | ConvertFrom-Json

# the server logs "Validating discovered platform extension: <Title>" for each extension that passed validation
$Titles = @(Get-ChildItem -LiteralPath (Join-Path $Source $Manifest.extensionsDirectory) -Directory |
    ForEach-Object { Join-Path $_.FullName "extension.manifest.json" } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    ForEach-Object { (Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json).Title })
if ($Titles.Count -eq 0) { throw "No extension manifests under $($Manifest.extensionsDirectory); nothing to validate." }

# a refused extension is only a warning in the log. its text comes from the resources, where {$NAME} may or may not
# have been substituted, so it matches as a wildcard.
$Resources = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot "../../RDCore.SDK/Exceptions.resx") -Raw)
$InvalidExtension = ($Resources.root.data | Where-Object name -eq "InvalidExtension_Message").value
if (-not $InvalidExtension) { throw "InvalidExtension_Message not found in RDCore.SDK/Exceptions.resx." }
$FailurePatterns = @(
    "\[(ERR|FTL)\]",
    (($InvalidExtension -split [regex]::Escape('{$NAME}') | ForEach-Object { [regex]::Escape($_) }) -join ".*"),
    # ExtensionsClient.Discover, not a resource
    "No manifest was found for extension"
)

$Work = Join-Path ([IO.Path]::GetTempPath()) "rdcore-smoke-$([guid]::NewGuid().ToString('n'))"
$Tree = Join-Path $Work "platform"
$Workspace = Join-Path $Work "workspace"
$PipeName = "rdcore-smoke-$([guid]::NewGuid().ToString('n'))"
$Logs = Join-Path $Tree "Logs"

function Read-LanguageServerLog {
    if (-not (Test-Path -LiteralPath $Logs)) { return "" }
    $text = [Text.StringBuilder]::new()
    foreach ($file in Get-ChildItem -LiteralPath $Logs -Filter "RDCore.LanguageServer-*.log" -File | Sort-Object Name) {
        # the server keeps the file open for writing
        $stream = [IO.FileStream]::new($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        $reader = [IO.StreamReader]::new($stream, [Text.UTF8Encoding]::new($false))
        try { [void]$text.Append($reader.ReadToEnd()) } finally { $reader.Dispose() }
    }
    $text.ToString()
}

# Windows never clears a ParentProcessId, and a reused PID inherits the orphans of the process that had it: only a
# process created at or after its parent's start is its child (as Process.Kill($true) decides). CreationDate is $null
# for processes this user can't query, which are not ours either.
function Get-DescendantProcessId([int]$ParentId, [datetime]$ParentStart) {
    foreach ($child in Get-CimInstance Win32_Process -Filter "ParentProcessId = $ParentId") {
        if ($null -eq $child.CreationDate -or $child.CreationDate -lt $ParentStart) { continue }
        Get-DescendantProcessId $child.ProcessId $child.CreationDate
        $child.ProcessId
    }
}

$Process = $null
# nothing started after MaxValue, so the walk finds no children if the start time was never read
$ServerStart = [datetime]::MaxValue
$Verdict = $null
$Log = ""
try {
    New-Item $Workspace -ItemType Directory -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Tree -Recurse
    # stale logs would satisfy (or fail) the checks below on their own
    Remove-Item -LiteralPath $Logs -Recurse -Force -ErrorAction SilentlyContinue

    $executable = Join-Path $Tree $Manifest.langService
    $startInfo = [Diagnostics.ProcessStartInfo]::new($executable)
    # appsettings.json is read from the working directory
    $startInfo.WorkingDirectory = Split-Path -Parent $executable
    # the launch contract of release.json (launch.languageServer.arguments). the client process ID is what lets the
    # server and its children exit when the client dies; the server only acts on it at LSP initialize, which this
    # test doesn't send, so it's passed to match the contract rather than checked.
    foreach ($argument in "--client-process-id", "$PID", "--pipe-name", $PipeName, "--workspace", $Workspace) { $startInfo.ArgumentList.Add($argument) }
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    # the generic host maps Configuration__X (and DOTNET_Configuration__X) onto appsettings keys
    foreach ($name in @($startInfo.Environment.Keys)) {
        if ($name -eq "RDCORE_PLATFORM_ROOT" -or $name -match "^(DOTNET_)?Configuration(__|:)") { [void]$startInfo.Environment.Remove($name) }
    }

    Write-Host "Starting $($Manifest.langService) from a copy at $Tree (pipe $PipeName, timeout ${TimeoutSeconds}s)..."
    $Process = [Diagnostics.Process]::Start($startInfo)
    # read now: the walk below needs it even if the server has exited by then
    $ServerStart = $Process.StartTime
    $StandardOutput = $Process.StandardOutput.ReadToEndAsync()
    $StandardError = $Process.StandardError.ReadToEndAsync()

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $settleUntil = $null
    while (-not $Verdict) {
        $Log = Read-LanguageServerLog
        $failed = @($Log -split "\r?\n" | Where-Object { $line = $_; $FailurePatterns | Where-Object { $line -match $_ } })
        $missing = @($Titles | Where-Object { -not $Log.Contains("Validating discovered platform extension: $_") })
        $registered = $Log.Contains("Registered RDCore platform extensions")
        $listening = [IO.Directory]::GetFiles("\\.\pipe\") -contains "\\.\pipe\$PipeName"

        if ($failed.Count -gt 0) {
            $Verdict = "the log reports a failure:`n  " + ($failed -join "`n  ")
        }
        elseif ($Process.HasExited) {
            $Verdict = "the language server exited early with code $($Process.ExitCode)."
        }
        elseif ($missing.Count -eq 0 -and $registered -and $listening) {
            # a moment more, so an error or crash right after registration still fails the test
            if (-not $settleUntil) { $settleUntil = [DateTime]::UtcNow.AddSeconds(2) }
            elseif ([DateTime]::UtcNow -ge $settleUntil) { $Verdict = "ok" }
        }
        elseif ([DateTime]::UtcNow -ge $deadline) {
            $waiting = @($missing | ForEach-Object { "extension '$_' validated" })
            if (-not $registered) { $waiting += "extensions registered" }
            if (-not $listening) { $waiting += "pipe $PipeName listening" }
            $Verdict = "timed out after ${TimeoutSeconds}s waiting for: $($waiting -join ", ")."
        }
        if (-not $Verdict) { Start-Sleep -Milliseconds 250 }
    }
}
finally {
    if ($Process) {
        # snapshot the children before the parent goes: they are found by ParentProcessId
        $children = @(Get-DescendantProcessId $Process.Id $ServerStart)
        if (-not $Process.HasExited) { $Process.Kill($true) }
        foreach ($id in $children) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
        [void]$Process.WaitForExit(10000)
    }
    # handles are released asynchronously after a kill
    for ($attempt = 1; (Test-Path -LiteralPath $Work) -and $attempt -le 20; $attempt++) {
        try { Remove-Item -LiteralPath $Work -Recurse -Force } catch { Start-Sleep -Milliseconds 500 }
    }
    if (Test-Path -LiteralPath $Work) { Write-Warning "Could not delete $Work." }
}

Write-Host "--- RDCore.LanguageServer log ---"
Write-Host $Log.TrimEnd()
Write-Host "---"

if ($Verdict -ne "ok") {
    foreach ($stream in @(@("stdout", $StandardOutput), @("stderr", $StandardError))) {
        if ($stream[1] -and $stream[1].Wait(5000) -and $stream[1].Result) {
            Write-Host "--- $($stream[0]) (last 40 lines) ---"
            Write-Host (($stream[1].Result.TrimEnd() -split "\r?\n" | Select-Object -Last 40) -join "`n")
        }
    }
    $prefix = if ($env:GITHUB_ACTIONS -eq "true") { "::error title=Test-LanguageServerStartup::" } else { "❌ " }
    Write-Host "${prefix}Language server smoke test failed: $Verdict"
    exit 1
}

Write-Host "✅ Language server started, validated $($Titles -join ", ") and registered its extensions."
exit 0
