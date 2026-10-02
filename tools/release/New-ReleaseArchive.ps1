#Requires -Version 7.2
# zips a finished platform root into <OutDir>/rdcore-<version>-<rid>.zip, copies its release.json next to the zip, and
# writes SHA256SUMS (sha256sum format) for both.
#
# the zip root is the platform root: rdcore.json sits at its top level, and the zip is used as extracted. entries are in
# ordinal path order with forward slashes, and all carry the commit time from rdcore.json rather than the build
# machine's file times. that makes the entry list and metadata a function of the tree; the zip's digest is still only
# an integrity check, not a reproducibility claim, since it also depends on the compressor.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PlatformRoot,
    [Parameter(Mandatory)][string]$OutDir
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.ZipFile

# both through GetFullPath, which also expands 8.3 short names, so the containment check compares like with like
$Root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $PlatformRoot).ProviderPath)
$OutDir = [IO.Path]::GetFullPath($OutDir, (Get-Location).ProviderPath)
if (($OutDir.TrimEnd("\") + "\").StartsWith($Root.TrimEnd("\") + "\", [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutDir '$OutDir' is inside the platform root; the zip would end up in the tree."
}

function Get-RelativePath([string]$Path) { [IO.Path]::GetRelativePath($Root, $Path).Replace("\", "/") }

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

$Manifest = Read-PlatformManifest (Join-Path $Root "rdcore.json")
foreach ($key in "platformVersion", "rid", "generatedUtc") {
    if (-not $Manifest[$key]) { throw "rdcore.json has no '$key'." }
}
# zip entries store local clock time without a zone; the UTC commit time keeps them independent of the runner's zone
$Timestamp = [DateTimeOffset]::ParseExact($Manifest.generatedUtc, "o", [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AssumeUniversal).ToUniversalTime()

# the zip carries exactly the inventory release.json describes, plus release.json itself
$ReleasePath = Join-Path $Root "release.json"
if (-not (Test-Path -LiteralPath $ReleasePath -PathType Leaf)) { throw "release.json is missing; run New-ReleaseMetadata.ps1 first." }
$Listed = [string[]]@((Get-Content -LiteralPath $ReleasePath -Raw | ConvertFrom-Json).files.path)
$Paths = [string[]]@(Get-ChildItem -LiteralPath $Root -Recurse -File -Force | ForEach-Object { Get-RelativePath $_.FullName })
[Array]::Sort($Paths, [StringComparer]::Ordinal)
$unlisted = @($Paths | Where-Object { $_ -cne "release.json" -and $Listed -cnotcontains $_ })
$absent = @($Listed | Where-Object { $Paths -cnotcontains $_ })
if ($unlisted.Count -or $absent.Count) {
    throw "The tree does not match release.json. Not listed: [$($unlisted -join ", ")]. Listed but absent: [$($absent -join ", ")]."
}

New-Item $OutDir -ItemType Directory -Force | Out-Null
$ZipName = "rdcore-$($Manifest.platformVersion)-$($Manifest.rid).zip"
$ZipPath = Join-Path $OutDir $ZipName
# ZipArchiveMode.Create refuses to open an existing file
Remove-Item -LiteralPath $ZipPath -Force -ErrorAction SilentlyContinue

$Archive = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($path in $Paths) {
        $entry = $Archive.CreateEntry($path, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $Timestamp
        $source = [IO.File]::OpenRead((Join-Path $Root $path))
        $target = $entry.Open()
        try { $source.CopyTo($target) } finally { $target.Dispose(); $source.Dispose() }
    }
}
finally { $Archive.Dispose() }

Copy-Item -LiteralPath $ReleasePath -Destination (Join-Path $OutDir "release.json") -Force

$Assets = [string[]]@($ZipName, "release.json")
[Array]::Sort($Assets, [StringComparer]::Ordinal)
$Sums = ($Assets | ForEach-Object { "$(Get-Sha256 (Join-Path $OutDir $_))  $_`n" }) -join ""
[IO.File]::WriteAllText((Join-Path $OutDir "SHA256SUMS"), $Sums, [Text.UTF8Encoding]::new($false))

Write-Host "📦 $ZipPath ($($Paths.Count) entries, $((Get-Item -LiteralPath $ZipPath).Length) bytes)"
Write-Host $Sums.TrimEnd()
