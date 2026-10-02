[CmdletBinding()]
param(
    [switch]$Silent,
    [string]$Configuration = "Debug",
    [string]$PlatformRoot = "artifacts\rdcore-dev",
    # e.g. win-x64: a framework-dependent publish for that runtime; omitted, the build is portable (no RID).
    [string]$RuntimeIdentifier,
    # appended to the Directory.Build.props VersionPrefix, e.g. rc.1 -> 0.1.0-rc.1.
    [string]$VersionSuffix
)

$ErrorActionPreference = "Stop"

Write-Host "
                                    -+-+--++--
                                --+----+----------
                              ++-----+-    +--+----+
                             -----             +------
                            -----         ----   -------------
                            +---          -----   +-----------+
                            ----          +--+   ----++ +-----
             +-+            ----                ----- --+----
            ------+          ----               -----------
            --------++        ---+-              -----+-™
           ---+ -+--------     -----             +--
           +--+    ----------+------+            +---+
          ----+         +--+-----+--+             +----+
          ----                                      -----
          ---+                                       ---+-
          ----                                        -----
          +---+                                        +---
           ----                                        ----
           -...                                        .--.
         ====================================================
                      V I V A T  ❤️  C U C U M I S ™
         ====================================================
"
Write-Host "RDCore Platform Local Publish Script"
Write-Host "©2026 Copyright 9562-7303 Québec inc."
Write-Host ""

# GetFullPath also expands 8.3 short names, so paths below compare like with like
$RepoRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $PSScriptRoot).Path)
$StagingRoot = Join-Path $RepoRoot "artifacts\staging"

# a relative platform root resolves against the repo, not the caller's working directory.
if (-not [IO.Path]::IsPathRooted($PlatformRoot))
{
    $PlatformRoot = Join-Path $RepoRoot $PlatformRoot
}
$PlatformRoot = [IO.Path]::GetFullPath($PlatformRoot)

# the platform root is emptied and rebuilt: refuse anything that does not already look like one,
# so a mistyped -PlatformRoot cannot take an unrelated folder with it.
if ((Test-Path -LiteralPath $PlatformRoot) `
    -and (Get-ChildItem -LiteralPath $PlatformRoot -Force | Select-Object -First 1) `
    -and -not (Test-Path -LiteralPath (Join-Path $PlatformRoot "rdcore.json") -PathType Leaf))
{
    throw "Refusing to replace '$PlatformRoot': it is not empty and has no rdcore.json at its top level."
}

# staging is wiped too, so neither folder may sit inside the other.
$PlatformPrefix = $PlatformRoot.TrimEnd('\') + '\'
$StagingPrefix = $StagingRoot.TrimEnd('\') + '\'
if ($PlatformPrefix.StartsWith($StagingPrefix, [StringComparison]::OrdinalIgnoreCase) `
    -or $StagingPrefix.StartsWith($PlatformPrefix, [StringComparison]::OrdinalIgnoreCase))
{
    throw "The platform root '$PlatformRoot' and the staging folder '$StagingRoot' must not contain one another."
}

# shipped at the top of every platform root; checked now so a missing file fails before a long publish.
$RootDocuments = @("LICENSE-GPLv3.md", "LICENSE-MIT.md", "NOTICE.md")
foreach ($document in $RootDocuments)
{
    if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $document) -PathType Leaf))
    {
        throw "$document is missing from the repository root."
    }
}

# the trimmed first line a git command prints for this repo, or $null when git is missing or fails.
# stderr is merged and dropped, and only the exit code decides: under Windows PowerShell 5.1 a redirected
# stderr line throws while $ErrorActionPreference is Stop, even when git succeeds (a warning, say).
function Invoke-Git([string[]]$Arguments)
{
    $ErrorActionPreference = "Continue"
    try
    {
        $output = @(& git -C $RepoRoot @Arguments 2>&1 | Where-Object { $_ -is [string] })
    }
    catch
    {
        # git is not on PATH
        return $null
    }
    if ($LASTEXITCODE -ne 0 -or $output.Count -eq 0)
    {
        return $null
    }
    return $output[0].Trim()
}

# the manifest records the commit and its date, both from git: checked now so a source download without .git,
# or a machine without git, fails before the platform root is wiped rather than after a long publish.
$HeadCommit = Invoke-Git "rev-parse", "HEAD"
if ($HeadCommit -notmatch '^[0-9a-f]{40}$')
{
    throw "Could not read the commit of '$RepoRoot' (git rev-parse HEAD): this script needs git on PATH and a git clone, not a source download."
}

Write-Host "Validate paths:"
Write-Host "📁 Staging: " $StagingRoot
Write-Host "📁 Platform:" $PlatformRoot
Write-Host "⚙️ Configuration:" $Configuration
if ($RuntimeIdentifier) { Write-Host "⚙️ Runtime:" $RuntimeIdentifier }
if ($VersionSuffix) { Write-Host "⚙️ Version suffix:" $VersionSuffix }

if (-not $Silent) {
    $Confirmation = Read-Host "(Y/y)es to proceed: "
    if ($Confirmation -ne "Y"){
      throw "Operation was cancelled by the user."

    }
}
Write-Host "✅ User confirmation cleared."

$Projects = @(
    @{
        Name = "RDCore.CLI"
        Project = "RDCore.CLI\RDCore.CLI.csproj"
    },
    @{
        Name = "RDCore.LanguageServer"
        Project = "RDCore.LanguageServer\RDCore.LanguageServer.csproj"
    },
    @{
        Name = "RDCore.Parsing"
        Project = "RDCore.Parsing\RDCore.Parsing.csproj"
    },
    @{
        Name = "Extensions\RDCore.Diagnostics"
        Project = "RDCore.Diagnostics\RDCore.Diagnostics.csproj"
    }
)

Write-Host ""
Write-Host "Cleaning previous outputs..."

# -LiteralPath throughout: -Path would read [ ] in a folder name as a wildcard and delete a sibling instead.
Remove-Item -LiteralPath $StagingRoot -Recurse -Force -ErrorAction SilentlyContinue

# emptied with rdcore.json last: a file held open by a running process (a client using this tree's language
# server, say) stops the wipe with the manifest still in place, so the guard above accepts the next run.
if (Test-Path -LiteralPath $PlatformRoot)
{
    Get-ChildItem -LiteralPath $PlatformRoot -Force | Where-Object Name -ne "rdcore.json" |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Get-ChildItem -LiteralPath $PlatformRoot -Force | Where-Object Name -ne "rdcore.json"))
    {
        Remove-Item -LiteralPath (Join-Path $PlatformRoot "rdcore.json") -Force -ErrorAction SilentlyContinue
    }
    if (Get-ChildItem -LiteralPath $PlatformRoot -Force | Select-Object -First 1)
    {
        throw "Could not empty '$PlatformRoot': close any process running from it, then run this script again."
    }
}

New-Item $StagingRoot -ItemType Directory -Force | Out-Null
New-Item $PlatformRoot -ItemType Directory -Force | Out-Null

Write-Host "✅ Done."

Write-Host ""
Write-Host "Publishing projects..."
Write-Host ""

$PublishArguments = @("--configuration", $Configuration)
if ($RuntimeIdentifier)
{
    $PublishArguments += @("-r", $RuntimeIdentifier, "--self-contained", "false")
}
if ($VersionSuffix)
{
    $PublishArguments += "-p:VersionSuffix=$VersionSuffix"
}
# deterministic source paths (/_/...) in the PDBs; only meaningful on a CI checkout.
if ($env:GITHUB_ACTIONS -eq "true")
{
    $PublishArguments += "-p:ContinuousIntegrationBuild=true"
}

foreach ($project in $Projects)
{
    $publishPath = Join-Path $StagingRoot $project.Name
    $projPath = Join-Path $RepoRoot $project.Project

    New-Item $publishPath -ItemType Directory -Force | Out-Null

    dotnet publish $projPath --output $publishPath @PublishArguments

    if ($LASTEXITCODE -ne 0)
    {
        throw "Publish failed for $($project.Name)"
    }

    Write-Host "🚀 $($project.Name) was published successfully."
}

Write-Host ""
Write-Host "🧩 Assembling platform..."
Write-Host ""

# Logs is created on demand by the runtime, and nothing reads a Config folder.
New-Item (Join-Path $PlatformRoot "Extensions") -ItemType Directory -Force | Out-Null

foreach ($project in $Projects)
{
    $source = Join-Path $StagingRoot $project.Name
    $target = Join-Path $PlatformRoot $project.Name

    New-Item $target -ItemType "directory" | Out-Null
    Get-ChildItem -LiteralPath $source | Copy-Item -Destination $target -Recurse
}

foreach ($document in $RootDocuments)
{
    Copy-Item -LiteralPath (Join-Path $RepoRoot $document) -Destination $PlatformRoot
}

# a JSON string literal: quotes, backslashes and control characters escaped, everything else as-is.
function ConvertTo-JsonStringLiteral([string]$Value)
{
    $escaped = $Value.Replace('\', '\\').Replace('"', '\"')
    $escaped = [regex]::Replace($escaped, '[\x00-\x1f]', { param($match) '\u{0:x4}' -f [int][char]$match.Value })
    return '"' + $escaped + '"'
}

# written by hand rather than with ConvertTo-Json, whose indentation and escaping differ between
# Windows PowerShell 5.1 and pwsh 7: a flat object of strings, 2-space indent, LF, trailing newline.
function ConvertTo-ManifestJson([System.Collections.Specialized.OrderedDictionary]$Properties)
{
    $members = foreach ($key in $Properties.Keys)
    {
        "  " + (ConvertTo-JsonStringLiteral $key) + ": " + (ConvertTo-JsonStringLiteral $Properties[$key])
    }
    return "{`n" + ($members -join ",`n") + "`n}`n"
}

# the version is the one the build stamped (VersionPrefix[-VersionSuffix]+<commit sha>), not recomputed here;
# the sha it carries must be the HEAD read before publishing, or the manifest would name a commit the tree was not built from.
$LanguageServerAssembly = Join-Path $PlatformRoot "RDCore.LanguageServer\RDCore.LanguageServer.dll"
$ProductVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($LanguageServerAssembly).ProductVersion
$VersionParts = @($ProductVersion -split '\+', 2)
$PlatformVersion = $VersionParts[0]
$Commit = $HeadCommit
if (-not $PlatformVersion)
{
    throw "Could not read the version this platform was built from (product version '$ProductVersion')."
}
if ($VersionParts.Count -gt 1 -and $VersionParts[1] -ne $Commit)
{
    throw "The platform was built from a different commit ('$ProductVersion') than the one read before publishing ($Commit)."
}

# the commit time rather than the wall clock, so the manifest (and the release zip's timestamps) depend only on the commit.
$CommitDate = Invoke-Git "log", "-1", "--format=%cI", $Commit
$GeneratedUtc = if ($CommitDate) { [DateTimeOffset]::Parse($CommitDate, [Globalization.CultureInfo]::InvariantCulture).UtcDateTime } else { (Get-Date).ToUniversalTime() }

$Rid = if ($RuntimeIdentifier) { $RuntimeIdentifier } else { "any" }

$Manifest = ConvertTo-ManifestJson ([ordered]@{
    platformVersion = $PlatformVersion
    commit = $Commit
    rid = $Rid
    generatedUtc = $GeneratedUtc.ToString("o", [Globalization.CultureInfo]::InvariantCulture)

    hostService = "RDCore.CLI/rdc.exe"
    langService = "RDCore.LanguageServer/RDCore.LanguageServer.exe"
    parseServer = "RDCore.Parsing/RDCore.ParseServer.exe"

    extensionsDirectory = "Extensions"
})

$ManifestPath = Join-Path $PlatformRoot "rdcore.json"
[IO.File]::WriteAllText($ManifestPath, $Manifest, (New-Object Text.UTF8Encoding $false))

Write-Host ""
Write-Host "🧩 Generating extension manifests..."
Write-Host ""

# each extension gets an extension.manifest.json, reflected off its executable by `rdc.exe describe-ext`
# (command mode, unsafe dev signing). The language server discovers extensions by this manifest.
# relative to the extension folder (Extensions\<Folder>) it runs in: Windows PowerShell 5.1's call operator reads
# [ ] in an absolute path as a wildcard, so under a root like rdcore[1] it would run rdcore1's rdc.exe instead.
$Rdc = "..\..\RDCore.CLI\rdc.exe"

$Extensions = @(
    @{ Folder = "RDCore.Diagnostics"; Executable = "RDCore.Diagnostics.exe"; Description = "RDCore core inspection extension." }
)

foreach ($extension in $Extensions)
{
    $extensionFolder = Join-Path $PlatformRoot (Join-Path "Extensions" $extension.Folder)

    Push-Location -LiteralPath $extensionFolder
    try
    {
        # --unsafe-dev-mode only lets describe-ext run without a signing setup. the manifest Signature it
        # records is base64(SHA-512) of this exact exe, so nothing may modify the exe after this step.
        & $Rdc describe-ext $extension.Executable --description $extension.Description --overwrite --unsafe-dev-mode
        if ($LASTEXITCODE -ne 0)
        {
            throw "describe-ext failed for $($extension.Folder) (exit $LASTEXITCODE)."
        }
    }
    finally
    {
        Pop-Location
    }

    Write-Host "🧩 $(Join-Path $extensionFolder 'extension.manifest.json')"
}

Write-Host ""
Write-Host "Platform root assembled:"
Write-Host "  $PlatformRoot"
Write-Host ""
Write-Host "Manifest:"
Write-Host "  $ManifestPath"
Write-Host ""
Write-Host "✅ Done."