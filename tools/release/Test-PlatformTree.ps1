#Requires -Version 7.2
# checks an assembled platform root (PlatformPublish.ps1 output, or an extracted release zip) before it ships.
#
# collects every failure rather than stopping at the first, prints them, and exits 1; exits 0 with a one-line
# summary otherwise. -RequireReleaseFiles adds the files the release scripts write and checks release.json against
# the tree byte for byte, so it runs after New-ReleaseMetadata.ps1 (and again on the extracted zip).
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PlatformRoot,
    [string]$ExpectedCommit,
    [switch]$RequireReleaseFiles
)

$ErrorActionPreference = "Stop"

$Root = (Resolve-Path -LiteralPath $PlatformRoot).Path
$Failures = [Collections.Generic.List[string]]::new()
function Add-Failure([string]$Message) { $Failures.Add($Message) }

# root-relative, forward slashes: how rdcore.json and release.json name files
function Get-RelativePath([string]$Path) { [IO.Path]::GetRelativePath($Root, $Path).Replace("\", "/") }

# a root-relative path from a manifest must stay inside the root
function Test-RelativePath([string]$Path) {
    $Path -and -not [IO.Path]::IsPathRooted($Path) -and -not ($Path -split "[/\\]" -contains "..")
}

# System.Text.Json, as the runtime reads it; ConvertFrom-Json would also turn generatedUtc into a DateTime
function Read-PlatformManifest([string]$Path) {
    $document = [Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($Path))
    try {
        $manifest = [ordered]@{}
        foreach ($property in $document.RootElement.EnumerateObject()) {
            $manifest[$property.Name] = if ($property.Value.ValueKind -eq "String") { $property.Value.GetString() } else { $null }
        }
        $manifest
    }
    finally { $document.Dispose() }
}

function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

# (a) rdcore.json: present, parses, the contract's keys in the contract's order, and every path it names exists
$ManifestKeys = @("platformVersion", "commit", "rid", "generatedUtc", "hostService", "langService", "parseServer", "extensionsDirectory")
$Manifest = $null
$ManifestPath = Join-Path $Root "rdcore.json"
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    Add-Failure "rdcore.json is missing."
}
else {
    $bytes = [IO.File]::ReadAllBytes($ManifestPath)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        Add-Failure "rdcore.json starts with a UTF-8 byte order mark."
    }
    try {
        $Manifest = Read-PlatformManifest $ManifestPath
    }
    catch {
        Add-Failure "rdcore.json does not parse: $($_.Exception.Message)"
    }
}

if ($Manifest) {
    $keys = @($Manifest.Keys)
    if (($keys -join ",") -cne ($ManifestKeys -join ",")) {
        Add-Failure "rdcore.json keys are [$($keys -join ", ")], expected [$($ManifestKeys -join ", ")] in that order."
    }
    foreach ($key in $ManifestKeys) {
        if (-not $Manifest[$key]) { Add-Failure "rdcore.json: '$key' is missing, empty or not a string." }
    }
    if ($Manifest.platformVersion -and $Manifest.platformVersion -cnotmatch "^\d+\.\d+\.\d+(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$") {
        Add-Failure "rdcore.json: platformVersion '$($Manifest.platformVersion)' is not a SemVer version without build metadata."
    }
    if ($Manifest.commit -and $Manifest.commit -cnotmatch "^[0-9a-f]{40}$") {
        Add-Failure "rdcore.json: commit '$($Manifest.commit)' is not a full lower-case SHA-1."
    }
    $generated = [DateTime]::MinValue
    if ($Manifest.generatedUtc -and -not ([DateTime]::TryParseExact($Manifest.generatedUtc, "o", [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind, [ref]$generated) -and $generated.Kind -eq [DateTimeKind]::Utc)) {
        Add-Failure "rdcore.json: generatedUtc '$($Manifest.generatedUtc)' is not a UTC round-trip ('o') timestamp."
    }
    foreach ($key in "hostService", "langService", "parseServer") {
        $path = $Manifest[$key]
        if (-not $path) { continue }
        if (-not (Test-RelativePath $path)) { Add-Failure "rdcore.json: $key '$path' is not a path inside the platform root."; continue }
        if (-not (Test-Path -LiteralPath (Join-Path $Root $path) -PathType Leaf)) { Add-Failure "rdcore.json: $key '$path' does not exist." }
    }
    $extensions = $Manifest.extensionsDirectory
    if ($extensions -and -not ((Test-RelativePath $extensions) -and (Test-Path -LiteralPath (Join-Path $Root $extensions) -PathType Container))) {
        Add-Failure "rdcore.json: extensionsDirectory '$extensions' is not an existing folder inside the platform root."
    }
}

# (b) every extension as the language server validates it: folder named after the Title, Name present, and the
# Signature base64(SHA-512) of the exe. describe-ext hashed the exe, so this catches any change made after it.
$ExtensionCount = 0
$ExtensionsRoot = if ($Manifest -and $Manifest.extensionsDirectory) { Join-Path $Root $Manifest.extensionsDirectory }
if ($ExtensionsRoot -and (Test-Path -LiteralPath $ExtensionsRoot -PathType Container)) {
    foreach ($folder in Get-ChildItem -LiteralPath $ExtensionsRoot -Directory) {
        $relative = Get-RelativePath $folder.FullName
        $manifestFile = Join-Path $folder.FullName "extension.manifest.json"
        if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) { Add-Failure "$relative has no extension.manifest.json."; continue }
        try {
            $extension = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
        }
        catch {
            Add-Failure "$relative/extension.manifest.json does not parse: $($_.Exception.Message)"
            continue
        }
        $ExtensionCount++
        if ($extension.Title -cne $folder.Name) { Add-Failure "$relative/extension.manifest.json: Title '$($extension.Title)' does not match its folder name." }
        $name = [string]$extension.Name
        $executable = Join-Path $folder.FullName $name
        if (-not $name -or $name -match "[/\\]" -or -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            Add-Failure "$relative/extension.manifest.json: Name '$name' is not a file in that folder."
            continue
        }
        $sha512 = [Security.Cryptography.SHA512]::Create()
        $stream = [IO.File]::OpenRead($executable)
        try { $signature = [Convert]::ToBase64String($sha512.ComputeHash($stream)) } finally { $stream.Dispose(); $sha512.Dispose() }
        if ($signature -cne $extension.Signature) {
            Add-Failure "$relative/extension.manifest.json: Signature does not match base64(SHA-512) of $name; the exe changed after describe-ext."
        }
    }
    if ($ExtensionCount -eq 0) { Add-Failure "no extension manifests under $(Get-RelativePath $ExtensionsRoot)." }
}

# (c) every first-party assembly carries the same informational version, <platformVersion>+<commit>
$Assemblies = @(Get-ChildItem -LiteralPath $Root -Recurse -File |
    Where-Object { ($_.Name -like "RDCore.*.dll" -or $_.Name -eq "rdc.dll") -and $_.Name -notlike "*.resources.dll" })
if ($Assemblies.Count -eq 0) {
    Add-Failure "no first-party assemblies (RDCore.*.dll, rdc.dll) found."
}
if ($Manifest -and $Manifest.platformVersion -and $Manifest.commit) {
    $expected = "$($Manifest.platformVersion)+$($Manifest.commit)"
    foreach ($assembly in $Assemblies) {
        $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($assembly.FullName).ProductVersion
        if ($productVersion -cne $expected) {
            Add-Failure "$(Get-RelativePath $assembly.FullName): product version '$productVersion', expected '$expected'."
        }
    }
}
if ($ExpectedCommit -and $Manifest -and $Manifest.commit -cne $ExpectedCommit.ToLowerInvariant()) {
    Add-Failure "rdcore.json: commit '$($Manifest.commit)' is not the expected commit '$ExpectedCommit'."
}

# (d) the documents every tree ships, and with -RequireReleaseFiles the release files and release.json's inventory
$Required = @("LICENSE-GPLv3.md", "LICENSE-MIT.md", "NOTICE.md")
if ($RequireReleaseFiles) { $Required += @("THIRD-PARTY-NOTICES.txt", "SOURCE.md", "release.json") }
foreach ($file in $Required) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root $file) -PathType Leaf)) { Add-Failure "$file is missing from the platform root." }
}

$ReleaseFileCount = 0
$ReleasePath = Join-Path $Root "release.json"
if ($RequireReleaseFiles -and (Test-Path -LiteralPath $ReleasePath -PathType Leaf)) {
    $release = $null
    try {
        $release = Get-Content -LiteralPath $ReleasePath -Raw | ConvertFrom-Json
    }
    catch {
        Add-Failure "release.json does not parse: $($_.Exception.Message)"
    }
    if ($release) {
        if ($release.schema -ne 1) { Add-Failure "release.json: schema '$($release.schema)', expected 1." }
        if ($release.hashAlgorithm -cne "sha256") { Add-Failure "release.json: hashAlgorithm '$($release.hashAlgorithm)', expected 'sha256'." }
        if ($Manifest) {
            foreach ($pair in @(@("version", "platformVersion"), @("commit", "commit"), @("rid", "rid"))) {
                if ([string]$release.($pair[0]) -cne $Manifest[$pair[1]]) {
                    Add-Failure "release.json: $($pair[0]) '$($release.($pair[0]))' does not match rdcore.json $($pair[1]) '$($Manifest[$pair[1]])'."
                }
            }
            foreach ($key in "hostService", "langService", "parseServer", "extensionsDirectory") {
                if ([string]$release.entryPoints.$key -cne $Manifest[$key]) { Add-Failure "release.json: entryPoints.$key does not match rdcore.json." }
            }
        }

        $listed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $previous = $null
        foreach ($entry in @($release.files)) {
            $path = [string]$entry.path
            $ReleaseFileCount++
            if (-not (Test-RelativePath $path) -or $path.Contains("\")) { Add-Failure "release.json: '$path' is not a root-relative path with forward slashes."; continue }
            if (-not $listed.Add($path)) { Add-Failure "release.json: '$path' is listed twice."; continue }
            if ($null -ne $previous -and [string]::CompareOrdinal($previous, $path) -gt 0) { Add-Failure "release.json: files are not in ordinal order at '$path'." }
            $previous = $path
            $file = Join-Path $Root $path
            if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { Add-Failure "release.json lists '$path', which does not exist."; continue }
            $size = (Get-Item -LiteralPath $file).Length
            if ($size -ne $entry.size) { Add-Failure "release.json: '$path' is $size bytes, listed as $($entry.size)." }
            elseif ((Get-Sha256 $file) -cne $entry.sha256) { Add-Failure "release.json: '$path' does not match its listed sha256." }
        }

        # Logs/ is reported by (e) below
        foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File -Force) {
            $path = Get-RelativePath $file.FullName
            if ($path -ceq "release.json" -or $path.StartsWith("Logs/", [StringComparison]::OrdinalIgnoreCase)) { continue }
            if (-not $listed.Contains($path)) { Add-Failure "'$path' is not listed in release.json." }
        }
    }
}

# (e) a shipped tree has never run: Logs/ is created (and written) on first launch
$LogsPath = Join-Path $Root "Logs"
if (Test-Path -LiteralPath $LogsPath -PathType Container) {
    $logs = @(Get-ChildItem -LiteralPath $LogsPath -Recurse -File -Force)
    if ($logs.Count -gt 0) { Add-Failure "Logs/ holds $($logs.Count) file(s); the tree has been run. Publish it again." }
}

if ($Failures.Count -gt 0) {
    Write-Host "Platform tree check FAILED for ${Root}:"
    # annotations surface the failures on the workflow run summary
    $prefix = if ($env:GITHUB_ACTIONS -eq "true") { "::error title=Test-PlatformTree::" } else { "  ❌ " }
    foreach ($failure in $Failures) { Write-Host "$prefix$failure" }
    exit 1
}

$summary = "RDCore $($Manifest.platformVersion) ($($Manifest.commit), $($Manifest.rid)): $($Assemblies.Count) first-party assemblies, $ExtensionCount extension(s)"
if ($RequireReleaseFiles) { $summary += ", $ReleaseFileCount files match release.json" }
Write-Host "✅ Platform tree OK: $summary."
exit 0
