#Requires -Version 7.4
# archives a finished platform root into <OutDir>/rdcore-<version>-<rid>.zip for a Windows RID, or
# <OutDir>/rdcore-<version>-<rid>.tar.gz for any other, copies its release.json next to it as
# rdcore-<version>-<rid>.release.json, and writes SHA256SUMS (sha256sum format) for both.
#
# the archive root is the platform root: rdcore.json sits at its top level, and the archive is used as extracted. entries are
# in ordinal path order with forward slashes, and all carry the commit time from rdcore.json rather than the build machine's
# file times. that makes the entry list and metadata a function of the tree; the archive's digest is still only an integrity
# check, not a reproducibility claim, since it also depends on the compressor.
#
# a zip keeps no Unix file modes that every tool reads back, so the archive of a Linux or macOS tree is a tar.gz: its
# executables (ELF or Mach-O files) are 0755 and every other file is 0644, owned by uid and gid 0, whatever the machine.
# the release.json beside the archive is named after it, so that the archives of several RIDs can be assets of one release.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PlatformRoot,
    [Parameter(Mandatory)][string]$OutDir
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.ZipFile, System.Formats.Tar

# both through GetFullPath, which also expands 8.3 short names, so the containment check compares like with like
$Root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $PlatformRoot).ProviderPath)
$OutDir = [IO.Path]::GetFullPath($OutDir, (Get-Location).ProviderPath)
$Separator = [string][IO.Path]::DirectorySeparatorChar
if (($OutDir.TrimEnd($Separator) + $Separator).StartsWith($Root.TrimEnd($Separator) + $Separator, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutDir '$OutDir' is inside the platform root; the archive would end up in the tree."
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

# an ELF (Linux) or Mach-O (macOS) executable, by its first four bytes: what must keep its execute bit once extracted
function Test-NativeExecutable([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $magic = [byte[]]::new(4)
        if ($stream.Read($magic, 0, 4) -ne 4) { return $false }
    }
    finally { $stream.Dispose() }
    # the Mach-O magic numbers are UInt32 literals (u): without it, PowerShell reads a hex literal whose top bit is set as a negative
    # Int32, which no UInt32 equals
    ($magic[0] -eq 0x7F -and $magic[1] -eq 0x45 -and $magic[2] -eq 0x4C -and $magic[3] -eq 0x46) -or [BitConverter]::ToUInt32($magic, 0) -in 0xFEEDFACFu, 0xCFFAEDFEu, 0xCAFEBABEu, 0xBEBAFECAu
}

$Manifest = Read-PlatformManifest (Join-Path $Root "rdcore.json")
foreach ($key in "platformVersion", "rid", "generatedUtc") {
    if (-not $Manifest[$key]) { throw "rdcore.json has no '$key'." }
}
# zip entries store local clock time without a zone; the UTC commit time keeps them independent of the runner's zone
$Timestamp = [DateTimeOffset]::ParseExact($Manifest.generatedUtc, "o", [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AssumeUniversal).ToUniversalTime()

# the archive carries exactly the inventory release.json describes, plus release.json itself
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
$BaseName = "rdcore-$($Manifest.platformVersion)-$($Manifest.rid)"
$IsZip = $Manifest.rid -like "win*"
$ArchiveName = if ($IsZip) { "$BaseName.zip" } else { "$BaseName.tar.gz" }
$ArchivePath = Join-Path $OutDir $ArchiveName
# ZipArchiveMode.Create refuses to open an existing file, and a tar.gz is written over from the start
Remove-Item -LiteralPath $ArchivePath -Force -ErrorAction SilentlyContinue

if ($IsZip) {
    $Archive = [IO.Compression.ZipFile]::Open($ArchivePath, [IO.Compression.ZipArchiveMode]::Create)
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
}
else {
    # ustar, which every tar reads: unlike pax and gnu entries, it records no access or change time, which would be the clock's.
    # each folder comes before what is in it, as its own entry, so that an extracted folder is 0755 whatever the umask.
    $Folders = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $Paths) {
        $parts = $path.Split("/")
        for ($i = 1; $i -lt $parts.Count; $i++) { [void]$Folders.Add((($parts[0..($i - 1)]) -join "/") + "/") }
    }
    $Entries = [string[]]@($Paths + @($Folders))
    [Array]::Sort($Entries, [StringComparer]::Ordinal)

    $Directory = [IO.UnixFileMode]"UserRead, UserWrite, UserExecute, GroupRead, GroupExecute, OtherRead, OtherExecute"
    $Executable = $Directory
    $Regular = [IO.UnixFileMode]"UserRead, UserWrite, GroupRead, OtherRead"

    $file = [IO.File]::Create($ArchivePath)
    $gzip = [IO.Compression.GZipStream]::new($file, [IO.Compression.CompressionLevel]::Optimal)
    $writer = [Formats.Tar.TarWriter]::new($gzip, [Formats.Tar.TarEntryFormat]::Ustar, $false)
    try {
        foreach ($name in $Entries) {
            $folder = $name.EndsWith("/")
            $type = if ($folder) { [Formats.Tar.TarEntryType]::Directory } else { [Formats.Tar.TarEntryType]::RegularFile }
            $entry = [Formats.Tar.UstarTarEntry]::new($type, $name)
            $entry.ModificationTime = $Timestamp
            $entry.Uid = 0
            $entry.Gid = 0
            if ($folder) {
                $entry.Mode = $Directory
                $writer.WriteEntry($entry)
                continue
            }
            $source = Join-Path $Root $name
            $entry.Mode = if (Test-NativeExecutable $source) { $Executable } else { $Regular }
            $stream = [IO.File]::OpenRead($source)
            try {
                $entry.DataStream = $stream
                $writer.WriteEntry($entry)
            }
            finally { $stream.Dispose() }
        }
    }
    finally { $writer.Dispose(); $gzip.Dispose(); $file.Dispose() }
}

$ReleaseAsset = "$BaseName.release.json"
Copy-Item -LiteralPath $ReleasePath -Destination (Join-Path $OutDir $ReleaseAsset) -Force

$Assets = [string[]]@($ArchiveName, $ReleaseAsset)
[Array]::Sort($Assets, [StringComparer]::Ordinal)
$Sums = ($Assets | ForEach-Object { "$(Get-Sha256 (Join-Path $OutDir $_))  $_`n" }) -join ""
[IO.File]::WriteAllText((Join-Path $OutDir "SHA256SUMS"), $Sums, [Text.UTF8Encoding]::new($false))

Write-Host "📦 $ArchivePath ($($Paths.Count) files, $((Get-Item -LiteralPath $ArchivePath).Length) bytes)"
Write-Host $Sums.TrimEnd()
