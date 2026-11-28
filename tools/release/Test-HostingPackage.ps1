#Requires -Version 7.2
# checks the RDCore.Runtime.Hosting package before nuget.org gets it: what a host references must hold neither the parser
# (RDCore.Parsing) nor ANTLR, whose third-party GPL parts NOTICE's grant does not cover.
#
# the folder dotnet pack wrote holds the package of -Version and, at most, its symbols. In the package, no file is one of
# the parser's or ANTLR's assemblies, and the nuspec names neither as a dependency. Then a throwaway project restores the
# package from that folder and nuget.org, as a host would, and the closure the restore resolves holds neither: that covers
# what the package's dependencies bring in turn. Collects every failure, prints them, and exits 1; exits 0 with a
# summary otherwise.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Packages,
    [Parameter(Mandatory)][string]$Version,
    [string]$Id = "RDCore.Runtime.Hosting",
    [string]$TargetFramework = "net11.0"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$Folder = (Resolve-Path -LiteralPath $Packages).Path
$Failures = [Collections.Generic.List[string]]::new()
function Add-Failure([string]$Message) { $Failures.Add($Message) }

# the parser and ANTLR, as an assembly's file name or a package id starts
$Excluded = '^(RDCore\.Parsing|Antlr4)(\.|$)'

$expected = @("$Id.$Version.nupkg", "$Id.$Version.snupkg")
$found = @(Get-ChildItem -LiteralPath $Folder -File | ForEach-Object Name)
foreach ($name in $found) {
    if ($name -notin $expected) { Add-Failure "$name is in the package folder: only $($expected[0]) and its symbols belong there." }
}
$package = Join-Path $Folder $expected[0]

if (-not (Test-Path -LiteralPath $package)) {
    Add-Failure "There is no $($expected[0]) in $Folder."
}
else {
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $nuspec = @($zip.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.Name -like '*.nuspec' })
        if ($nuspec.Count -ne 1) { Add-Failure "$($expected[0]) has $($nuspec.Count) nuspec files at its root, not one." }
        else {
            $reader = [IO.StreamReader]::new($nuspec[0].Open())
            try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $metadata = $manifest.package.metadata
            if ($metadata.id -cne $Id) { Add-Failure "The nuspec's id is '$($metadata.id)', not '$Id'." }
            if ($metadata.version -cne $Version) { Add-Failure "The nuspec's version is '$($metadata.version)', not '$Version'." }
            foreach ($dependency in $manifest.SelectNodes("//*[local-name()='dependency']")) {
                if ($dependency.id -match $Excluded) { Add-Failure "The nuspec names $($dependency.id) $($dependency.version) as a dependency." }
            }
        }
        foreach ($entry in $zip.Entries) {
            if ($entry.Name -match $Excluded) { Add-Failure "$($expected[0]) holds $($entry.FullName)." }
        }
        $assemblies = @($zip.Entries | Where-Object { $_.FullName -like 'lib/*' -and $_.Name -like '*.dll' } | ForEach-Object FullName)
        if ($assemblies.Count -eq 0) { Add-Failure "$($expected[0]) holds no assembly under lib/." }
    }
    finally { $zip.Dispose() }
}

# a host's restore: this folder for the package, nuget.org for what it depends on, and a packages folder of its own, so the
# machine's cache gets nothing of it
if ($Failures.Count -eq 0) {
    $consumer = Join-Path ([IO.Path]::GetTempPath()) ("rdcore-hosting-consumer-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $consumer | Out-Null
    try {
        Set-Content -LiteralPath (Join-Path $consumer 'Host.csproj') -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$TargetFramework</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$Id" Version="[$Version]" />
  </ItemGroup>
</Project>
"@
        Set-Content -LiteralPath (Join-Path $consumer 'nuget.config') -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="package" value="$([Security.SecurityElement]::Escape($Folder))" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
        $restore = dotnet restore (Join-Path $consumer 'Host.csproj') --packages (Join-Path $consumer 'packages') 2>&1
        if ($LASTEXITCODE -ne 0) {
            $restore | ForEach-Object { Write-Output "  $_" }
            Add-Failure "A host can't restore $Id $Version from the package and nuget.org."
        }
        else {
            $assets = Get-Content -LiteralPath (Join-Path $consumer 'obj' 'project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
            $libraries = @($assets.libraries.Keys | Sort-Object)
            foreach ($library in $libraries) {
                if ($library.Split('/')[0] -match $Excluded) { Add-Failure "The package's closure holds the package $library." }
            }
            $target = @($assets.targets.Keys)[0]
            $closure = [Collections.Generic.SortedSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($library in $assets.targets[$target].Values) {
                foreach ($file in @($library.runtime.Keys) + @($library.compile.Keys)) {
                    if ($file -and $file.EndsWith('.dll')) { [void]$closure.Add([IO.Path]::GetFileNameWithoutExtension($file)) }
                }
            }
            foreach ($name in $closure) { if ($name -match $Excluded) { Add-Failure "The package's closure holds the assembly $name." } }
            if ($Failures.Count -eq 0) {
                Write-Output ("$Id $Version restores as a host's reference: $($libraries.Count) packages, $($closure.Count) assemblies, " +
                    "none of them the parser's or ANTLR's.")
                Write-Output "  $($closure -join ', ')"
            }
        }
    }
    finally { Remove-Item -LiteralPath $consumer -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($Failures.Count) {
    $Failures | ForEach-Object { Write-Output "::error::$_" }
    exit 1
}
