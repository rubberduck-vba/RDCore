#Requires -Version 7.2
# writes THIRD-PARTY-NOTICES.txt for a published platform tree.
#
# MIT, Apache-2.0 and BSD all require their license text (and, for Apache-2.0 section 4(d), any NOTICE file) to travel
# with redistributed binaries, so every NuGet package the tree ships is listed with its license and the verbatim texts,
# and so is the .NET application host its executables are copies of.
# the release job fails closed: a package whose license is outside the allowlist or can't be identified stops the
# release (e.g. MediatR, which OmniSharp pulls in, is Apache-2.0 up to 12.5 but not from 13.0; that must not ship silently).
#
# canonical texts in licenses/, used when a package declares a license but ships no text:
#   MIT.txt, BSD-3-Clause.txt  spdx/license-list-data v3.29.0 (31ba1a5) text/, verbatim
#   Apache-2.0.txt             https://www.apache.org/licenses/LICENSE-2.0.txt, verbatim (sha256 cfc7749b...,
#                              byte-identical to the LICENSE in Serilog.Extensions.Logging.File 3.0.0)
# upstream texts in licenses/, verbatim, for what ships no text of its own or an incomplete one:
#   Antlr4.Runtime-4.6.6*.txt  tunnelvisionlabs/antlr4cs v4.6.6 (3f15cfb): LICENSE.txt, THIRD-PARTY-NOTICES.txt
#   Testably.Abstractions.FileSystem.Interface-10.3.0.txt
#                              Testably/Testably.Abstractions core/v10.3.0 (9b967ee, the commit its nuspec names): LICENSE
#   dotnet-runtime-*.txt       dotnet/dotnet v10.0.11 (e2f47b0), src/runtime: LICENSE.TXT, THIRD-PARTY-NOTICES.TXT
#                              (the same texts Microsoft.Bcl.AsyncInterfaces 7.0.0 and the 10.0.11 packages ship)
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PlatformRoot,
    [Parameter(Mandatory)][string]$OutFile,
    [string]$PackagesRoot,
    [string[]]$AllowedLicenses = @("MIT", "Apache-2.0", "BSD-3-Clause")
)

$ErrorActionPreference = "Stop"

$LicensesDir = Join-Path $PSScriptRoot "licenses"

# id@version -> license decided by hand, for packages whose metadata can't be resolved automatically.
# keyed by exact version so an upgrade gets looked at again. TextFile and NoticeFile are relative to licenses/;
# a NoticeFile is named <id>-<version>-<upstream file name>.
$Overrides = @{
    # predates nuspec license metadata; its licenseUrl points at antlr4cs master, whose LICENSE.txt is identical to the
    # one at tag v4.6.6 (3f15cfb): 3-clause BSD, (c) 2013 Sam Harwell and Terence Parr. the package ships no text.
    # the netstandard1.1 build also compiles MIT code from Mono (Novell 2009) and the .NET Reference Source (Microsoft),
    # whose notices upstream keeps in THIRD-PARTY-NOTICES.txt; the nupkg omits that file, so it ships from here, whole,
    # as upstream gives it.
    "Antlr4.Runtime@4.6.6" = @{
        License = "BSD-3-Clause"; TextFile = "Antlr4.Runtime-4.6.6.txt"; NoticeFile = "Antlr4.Runtime-4.6.6-THIRD-PARTY-NOTICES.txt"
    }
    # ships no text, and the holder in its nuspec <copyright> (2024- 2026 Testably) is not the one upstream's LICENSE
    # names ((c) 2022 Valentin Breuss), so the canonical text can't be filled in from the nuspec
    "Testably.Abstractions.FileSystem.Interface@10.3.0" = @{
        License = "MIT"; TextFile = "Testably.Abstractions.FileSystem.Interface-10.3.0.txt"
    }
}

# dotnet/runtime's own texts: its license names ".NET Foundation and Contributors", not the nuspecs' "(c) Microsoft
# Corporation". from 10.0 its packages are built in the dotnet/dotnet VMR (src/runtime) and their nuspecs name that.
$DotnetRuntime = @{
    Origin = "dotnet/dotnet v10.0.11 (e2f47b0), src/runtime"
    LicenseFile = "dotnet-runtime-LICENSE.txt"; NoticeFile = "dotnet-runtime-THIRD-PARTY-NOTICES.txt"
    Repository = '^https://github\.com/dotnet/(runtime|dotnet)(\.git)?/?$'
    ProjectUrl = "https://github.com/dotnet/runtime"; Copyright = "Copyright (c) .NET Foundation and Contributors"
}

# license texts for package families that ship none, matched by id pattern so a patch upgrade keeps working. used only
# for a package that ships no text, declares that license and names that repository; any other match fails.
$UpstreamTexts = @(
    @{
        Id = '^(Microsoft\.Extensions\..+|System\.Diagnostics\.EventLog)$'; Repository = $DotnetRuntime.Repository
        License = "MIT"; TextFile = $DotnetRuntime.LicenseFile; Origin = $DotnetRuntime.Origin
    }
)

# legacy licenseUrl values that name exactly one license; anything else needs an override
$KnownLicenseUrls = @{
    "apache.org/licenses/license-2.0"      = "Apache-2.0"
    "apache.org/licenses/license-2.0.txt"  = "Apache-2.0"
    "apache.org/licenses/license-2.0.html" = "Apache-2.0"
    "opensource.org/licenses/apache-2.0"   = "Apache-2.0"
    "opensource.org/licenses/mit"          = "MIT"
    "opensource.org/licenses/mit-license"  = "MIT"
    "opensource.org/licenses/mit-license.php" = "MIT"
    "opensource.org/license/mit"           = "MIT"
    "opensource.org/licenses/bsd-3-clause" = "BSD-3-Clause"
}

# ordinal, so the output doesn't depend on the machine's culture
function ConvertTo-OrdinalOrder([string[]]$Values) {
    $copy = [string[]]@($Values | Where-Object { $null -ne $_ })
    [Array]::Sort($copy, [StringComparer]::OrdinalIgnoreCase)
    , $copy
}

# strict utf-8 unless a bom says otherwise; normalised so the same text hashes the same whatever its line endings
function Read-Text([string]$Path) {
    $reader = [IO.StreamReader]::new($Path, [Text.UTF8Encoding]::new($false, $true), $true)
    try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $lines = $text.Replace("`r`n", "`n").Replace("`r", "`n").Split("`n") | ForEach-Object { $_.TrimEnd() }
    ($lines -join "`n").Trim("`n")
}

function Get-TextHash([string]$Text) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text)))
}

# whitespace-insensitive, since every copy of MIT is wrapped differently
function Get-LicenseIdsFromText([string]$Text) {
    $flat = ($Text -replace '\s+', ' ').ToLowerInvariant()
    $ids = [Collections.Generic.List[string]]::new()
    if ($flat.Contains("permission is hereby granted, free of charge, to any person obtaining a copy") -and
        $flat.Contains("the above copyright notice and this permission notice shall be included")) { $ids.Add("MIT") }
    if ($flat.Contains("apache license") -and $flat.Contains("version 2.0") -and
        $flat.Contains("terms and conditions for use, reproduction, and distribution")) { $ids.Add("Apache-2.0") }
    if ($flat.Contains("redistribution and use in source and binary forms")) {
        if ($flat.Contains("advertising materials")) { $ids.Add("BSD-4-Clause") }
        elseif ($flat.Contains("endorse or promote")) { $ids.Add("BSD-3-Clause") }
        else { $ids.Add("BSD-2-Clause") }
    }
    , $ids.ToArray()
}

function Get-MetadataValue([Xml.XmlElement]$Metadata, [string]$Name) {
    $node = $Metadata.SelectSingleNode("*[local-name()='$Name']")
    if ($node) { ($node.InnerText -replace '\s+', ' ').Trim() } else { "" }
}

# 'A' or 'A OR B'; anything with AND, WITH, parentheses or '+' is left to an override
function Resolve-Expression([string]$Expression) {
    $e = $Expression.Trim()
    if ($e -match '[()+]' -or $e -cmatch '\s(AND|WITH)\s') {
        return @{ Error = "license expression '$e' is not a single license or a simple OR; add an override" }
    }
    $operands = @($e -csplit '\s+OR\s+')
    foreach ($allowed in $AllowedLicenses) {
        if ($operands -contains $allowed) {
            $note = if ($operands.Count -gt 1) { "chosen from '$e'" } else { "" }
            return @{ License = $allowed; Operands = $operands; Note = $note }
        }
    }
    @{ Error = "license '$e' is not allowed (allowed: $($AllowedLicenses -join ', '))" }
}

function Get-CopyrightLine([string]$Copyright) {
    # [char]0xA9 is the copyright sign; kept out of the source so the file stays ASCII
    if ($Copyright.ToLowerInvariant() -cmatch "^(copyright|$([char]0xA9)|\(c\))") { return $Copyright }
    "Copyright (c) $Copyright"
}

# sha-256 of one section of a PE file, or $null if it isn't one or has no such section
function Get-PeSectionHash([string]$Path, [string]$Name) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $reader = [Reflection.PortableExecutable.PEReader]::new([IO.MemoryStream]::new($bytes))
    try {
        $section = $reader.PEHeaders.SectionHeaders | Where-Object { $_.Name -ceq $Name } | Select-Object -First 1
        if (-not $section) { return $null }
        # a copy, not a stream: HashData(Stream) is .NET 7, and #Requires allows 7.2 (.NET 6)
        $data = [byte[]]::new($section.SizeOfRawData)
        [Array]::Copy($bytes, $section.PointerToRawData, $data, 0, $section.SizeOfRawData)
        [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($data))
    }
    catch [BadImageFormatException] { $null }
    finally { $reader.Dispose() }
}

$PlatformRoot = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($PlatformRoot)
$OutFile = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($OutFile)
if (-not (Test-Path -LiteralPath $PlatformRoot -PathType Container)) {
    throw "Platform root not found: $PlatformRoot"
}

if (-not $PackagesRoot) { $PackagesRoot = $env:NUGET_PACKAGES }
if (-not $PackagesRoot -and (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $listed = & dotnet nuget locals global-packages --list 2>$null
    if ($LASTEXITCODE -eq 0) {
        $match = $listed | Select-String -CaseSensitive -Pattern '^\s*global-packages:\s*(.+?)\s*$' | Select-Object -First 1
        if ($match) { $PackagesRoot = $match.Matches[0].Groups[1].Value }
    }
}
if (-not $PackagesRoot) { $PackagesRoot = Join-Path $HOME ".nuget/packages" }
$PackagesRoot = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($PackagesRoot)
if (-not (Test-Path -LiteralPath $PackagesRoot -PathType Container)) {
    throw "NuGet packages folder not found: $PackagesRoot (pass -PackagesRoot or set NUGET_PACKAGES)"
}

$Failures = [Collections.Generic.List[string]]::new()

# every package any component's deps.json lists; the shared framework isn't a 'package' library, so it's not here
$Packages = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
$Components = [Collections.Generic.List[string]]::new()
$DepsFiles = Get-ChildItem -LiteralPath $PlatformRoot -Recurse -File -Filter "*.deps.json"
if (-not $DepsFiles) { throw "No *.deps.json under $PlatformRoot" }
foreach ($depsPath in (ConvertTo-OrdinalOrder $DepsFiles.FullName)) {
    $component = [IO.Path]::GetRelativePath($PlatformRoot, [IO.Path]::GetDirectoryName($depsPath)).Replace('\', '/')
    $Components.Add($component)
    $deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in (ConvertTo-OrdinalOrder $deps.libraries.Keys)) {
        $library = $deps.libraries[$key]
        if ($library.type -eq "project") { continue }
        if ($library.type -eq "runtimepack") {
            $Failures.Add("$key ($component): self-contained build; the .NET runtime's notices are not collected here")
            continue
        }
        if ($library.type -ne "package") {
            $Failures.Add("$key ($component): '$($library.type)' library has no NuGet license metadata")
            continue
        }
        $id, $version = $key.Split('/', 2)
        if (-not $Packages.ContainsKey($key)) {
            $Packages[$key] = [pscustomobject]@{
                Id = $id; Version = $version; Path = $library.path
                Components = [Collections.Generic.List[string]]::new()
            }
        }
        if (-not $Packages[$key].Components.Contains($component)) { $Packages[$key].Components.Add($component) }
    }
}

# one section per distinct text, keyed by hash of the normalised text
$Sections = @{}
function Add-Section([string]$Kind, [string]$Title, [string]$Text, [string]$Source, $Package) {
    $hash = Get-TextHash $Text
    if (-not $Sections.ContainsKey($hash)) {
        $Sections[$hash] = [pscustomobject]@{
            Kind = $Kind; Title = $Title; Text = $Text
            Packages = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            Sources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        }
    }
    [void]$Sections[$hash].Packages.Add("$($Package.Id) $($Package.Version)")
    [void]$Sections[$hash].Sources.Add($Source)
}

$UsedOverrides = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$Rows = [Collections.Generic.List[object]]::new()
$PackageKeys = ConvertTo-OrdinalOrder ($Packages.Values | ForEach-Object { "$($_.Id) $($_.Version)" })
foreach ($packageKey in $PackageKeys) {
    $id, $version = $packageKey.Split(' ', 2)
    $package = $Packages["$id/$version"]
    $name = "$id $version"
    $relative = if ($package.Path) { $package.Path } else { "$($id.ToLowerInvariant())/$($version.ToLowerInvariant())" }
    $packageDir = Join-Path $PackagesRoot $relative
    $nuspec = Join-Path $packageDir "$($id.ToLowerInvariant()).nuspec"
    if (-not (Test-Path -LiteralPath $nuspec)) {
        $Failures.Add("$($name): no nuspec at $nuspec; restore the solution or pass -PackagesRoot")
        continue
    }

    $doc = [Xml.XmlDocument]::new()
    $doc.Load($nuspec)
    $metadata = $doc.DocumentElement.SelectSingleNode("*[local-name()='metadata']")
    $package | Add-Member -NotePropertyMembers @{ Copyright = Get-MetadataValue $metadata "copyright" }
    $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
    $repositoryUrl = if ($repository) { $repository.GetAttribute("url") } else { "" }
    $projectUrl = Get-MetadataValue $metadata "projectUrl"
    if (-not $projectUrl -and $repositoryUrl) { $projectUrl = $repositoryUrl -replace '\.git$', '' }
    if (-not $projectUrl) { $projectUrl = "https://www.nuget.org/packages/$id/$version" }

    $rootFiles = @{}
    Get-ChildItem -LiteralPath $packageDir -File | ForEach-Object { $rootFiles[$_.Name] = $_.FullName }
    $rootNames = ConvertTo-OrdinalOrder @($rootFiles.Keys)
    $licenseFiles = [Collections.Generic.List[string]]::new()
    $rootNames | Where-Object { $_.ToLowerInvariant() -cmatch '^(licen[cs]e|copying)(\.(txt|md))?$' } | ForEach-Object { $licenseFiles.Add($rootFiles[$_]) }

    # decide the license: override, nuspec expression, nuspec file, then legacy licenseUrl
    $license = $null; $operands = @(); $note = ""; $textFile = $null
    $licenseNode = $metadata.SelectSingleNode("*[local-name()='license']")
    $override = $Overrides["$id@$version"]
    if ($override) {
        [void]$UsedOverrides.Add("$id@$version")
        $license = $override.License; $operands = @($license); $note = "override"
        if ($override.TextFile) { $textFile = Join-Path $LicensesDir $override.TextFile }
    }
    elseif ($licenseNode -and $licenseNode.GetAttribute("type") -eq "expression") {
        $resolved = Resolve-Expression $licenseNode.InnerText
        if ($resolved.Error) { $Failures.Add("$($name): $($resolved.Error)"); continue }
        $license = $resolved.License; $operands = $resolved.Operands; $note = $resolved.Note
    }
    elseif ($licenseNode -and $licenseNode.GetAttribute("type") -eq "file") {
        $declared = Join-Path $packageDir $licenseNode.InnerText.Trim()
        if (-not (Test-Path -LiteralPath $declared -PathType Leaf)) {
            $Failures.Add("$($name): license file '$($licenseNode.InnerText.Trim())' named by the nuspec is missing")
            continue
        }
        if (-not ($licenseFiles | Where-Object { $_ -eq $declared })) { $licenseFiles.Insert(0, $declared) }
        $ids = Get-LicenseIdsFromText (Read-Text $declared)
        if ($ids.Count -ne 1) {
            $reads = if ($ids.Count) { "reads as $($ids -join ' and ')" } else { "is not a license this script recognises" }
            $Failures.Add("$($name): license file '$($licenseNode.InnerText.Trim())' $reads; add an override")
            continue
        }
        $license = $ids[0]; $operands = @($license)
    }
    elseif ($licenseNode) {
        $Failures.Add("$($name): unknown nuspec license type '$($licenseNode.GetAttribute("type"))'")
        continue
    }
    else {
        $url = Get-MetadataValue $metadata "licenseUrl"
        $normalised = ($url.ToLowerInvariant() -replace '^https?://', '' -replace '^www\.', '').TrimEnd('/')
        if ($normalised -cmatch '^licenses\.nuget\.org/(.+)$') {
            $resolved = Resolve-Expression ([Uri]::UnescapeDataString($Matches[1]))
            if ($resolved.Error) { $Failures.Add("$($name): $($resolved.Error)"); continue }
            $license = $resolved.License; $operands = $resolved.Operands; $note = $resolved.Note
        }
        elseif ($KnownLicenseUrls.ContainsKey($normalised)) {
            $license = $KnownLicenseUrls[$normalised]; $operands = @($license)
        }
        else {
            $shown = if ($url) { "licenseUrl '$url' is not a known license" } else { "nuspec declares no license" }
            $Failures.Add("$($name): $shown; add an override")
            continue
        }
    }
    if ($AllowedLicenses -notcontains $license) {
        $Failures.Add("$($name): license '$license' is not allowed (allowed: $($AllowedLicenses -join ', '))")
        continue
    }

    # the package's own texts are shipped verbatim, and each must read as a license it declares: one that reads as
    # another license, or as none this script knows, means the metadata can't be trusted. it's a heuristic (a few
    # phrases of MIT, Apache-2.0 and BSD in root LICENSE/COPYING files), not a scan of everything the package holds.
    $coversLicense = $false
    if ($textFile) {
        if (-not (Test-Path -LiteralPath $textFile -PathType Leaf)) {
            $Failures.Add("$($name): override text $textFile is missing")
            continue
        }
        Add-Section -Kind "license" -Title $license -Text (Read-Text $textFile) -Source "tools/release/licenses/$($override.TextFile) (override)" -Package $package
        $coversLicense = $true
        if ($override.NoticeFile) {
            $noticePath = Join-Path $LicensesDir $override.NoticeFile
            if (-not (Test-Path -LiteralPath $noticePath -PathType Leaf)) {
                $Failures.Add("$($name): override notice $noticePath is missing")
                continue
            }
            $noticeTitle = $override.NoticeFile -replace "^$([regex]::Escape("$id-$version-"))", ""
            Add-Section -Kind "notice" -Title $noticeTitle -Text (Read-Text $noticePath) -Source "tools/release/licenses/$($override.NoticeFile) (override)" -Package $package
        }
    }
    else {
        $unreadable = $false
        foreach ($file in $licenseFiles) {
            $fileName = [IO.Path]::GetRelativePath($packageDir, $file).Replace('\', '/')
            try { $text = Read-Text $file }
            catch { $Failures.Add("$($name): $fileName is not valid UTF-8; add an override with a text file"); $unreadable = $true; break }
            $ids = Get-LicenseIdsFromText $text
            if ($ids.Count -eq 0) {
                $Failures.Add("$($name): declares '$license' but ships $fileName, which is not a license this script recognises; add an override with a text file")
                $unreadable = $true; break
            }
            if (-not ($ids | Where-Object { $operands -contains $_ })) {
                $Failures.Add("$($name): declares '$license' but ships $fileName, which reads as $($ids -join ' and ')")
                $unreadable = $true; break
            }
            if ($ids -contains $license) { $coversLicense = $true }
            Add-Section -Kind "license" -Title $license -Text $text -Source "$fileName shipped in the package" -Package $package
        }
        if ($unreadable) { continue }
    }
    if (-not $coversLicense) {
        $upstream = $UpstreamTexts | Where-Object { $id -match $_.Id } | Select-Object -First 1
        if ($upstream) {
            if ($license -ne $upstream.License -or $repositoryUrl -notmatch $upstream.Repository) {
                $Failures.Add("$($name): ships no license text; its id matches the $($upstream.License) text tools/release/licenses/$($upstream.TextFile), but it declares '$license' from repository '$repositoryUrl'; add an override")
                continue
            }
            $text = Read-Text (Join-Path $LicensesDir $upstream.TextFile)
            Add-Section -Kind "license" -Title $license -Text $text -Source "tools/release/licenses/$($upstream.TextFile), from $($upstream.Origin)" -Package $package
        }
        else {
            $canonical = Join-Path $LicensesDir "$license.txt"
            if (-not (Test-Path -LiteralPath $canonical -PathType Leaf)) {
                $Failures.Add("$($name): ships no $license text and tools/release/licenses/$license.txt does not exist")
                continue
            }
            $text = Read-Text $canonical
            $source = "canonical text, tools/release/licenses/$license.txt"
            if ($text -match '<year>') {
                # MIT and BSD templates carry a placeholder copyright line; fill it from the nuspec. a notice made up
                # from <authors> would not be the one the license asks to keep, so without <copyright> it's an override.
                if (-not $package.Copyright) {
                    $Failures.Add("$($name): ships no $license text and its nuspec states no copyright; add an override with upstream's license text")
                    continue
                }
                $line = Get-CopyrightLine $package.Copyright
                $text = ($text.Split("`n") | ForEach-Object { if ($_ -match '<year>') { $line } else { $_ } }) -join "`n"
                $source += ", with the package's copyright line"
            }
            Add-Section -Kind "license" -Title $license -Text $text -Source $source -Package $package
        }
    }

    # Apache-2.0 section 4(d) NOTICE files and Microsoft's THIRD-PARTY-NOTICES travel too
    foreach ($fileName in ($rootNames | Where-Object { $_.ToLowerInvariant() -cmatch '^(notice|third[-_]?party[-_]?notices)(\.(txt|md))?$' })) {
        try { $text = Read-Text $rootFiles[$fileName] }
        catch { $Failures.Add("$($name): $fileName is not valid UTF-8"); continue }
        Add-Section -Kind "notice" -Title $fileName -Text $text -Source "$fileName shipped in the package" -Package $package
    }

    $shippedIn = if ($package.Components.Count -eq $Components.Count) { "all" }
                 else { (ConvertTo-OrdinalOrder $package.Components) -join ", " }
    $Rows.Add([pscustomobject]@{
        Package = $id; Version = $version
        License = if ($note) { "$license ($note)" } else { $license }
        Copyright = if ($package.Copyright) { $package.Copyright } else { "-" }
        ProjectUrl = $projectUrl; ShippedIn = $shippedIn
    })
}

foreach ($key in (ConvertTo-OrdinalOrder @($Overrides.Keys))) {
    if (-not $UsedOverrides.Contains($key)) { Write-Warning "override for $key matched no shipped package; remove it" }
}

# the .NET application host. a framework-dependent app's <name>.exe is the SDK's apphost.exe from a
# Microsoft.NETCore.App.Host.<rid> pack, with the app's dll name patched into .data and its version resources added,
# and no deps.json lists it. so every .exe in the tree must be one: its .text is byte-identical to that pack's
# apphost.exe, and the pack folder the match is found in gives the version. the packs come with the SDK
# (<dotnet root>/packs, which has no license files) or from NuGet, so this runs where the tree was published.
# release.yml builds win-x64 only; a non-Windows apphost has no .exe extension and isn't looked for.
$HostRows = [Collections.Generic.List[object]]::new()
$HostFiles = [Collections.Generic.List[string]]::new()
$Executables = @(Get-ChildItem -LiteralPath $PlatformRoot -Recurse -File -Filter "*.exe")
if ($Executables.Count) {
    $packRoots = [Collections.Generic.List[string]]::new()
    if ($env:DOTNET_ROOT) { $packRoots.Add((Join-Path $env:DOTNET_ROOT "packs")) }
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($dotnet) {
        $target = [IO.File]::ResolveLinkTarget($dotnet.Source, $true)
        $packRoots.Add((Join-Path ([IO.Path]::GetDirectoryName($(if ($target) { $target.FullName } else { $dotnet.Source }))) "packs"))
    }
    $packRoots.Add($PackagesRoot)
    $apphosts = @{}
    foreach ($packRoot in ($packRoots | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $packRoot -PathType Container)) { continue }
        foreach ($pack in Get-ChildItem -LiteralPath $packRoot -Directory | Where-Object { $_.Name -like "Microsoft.NETCore.App.Host.*" }) {
            $packId = "Microsoft.NETCore.App.Host." + $pack.Name.Substring("Microsoft.NETCore.App.Host.".Length)
            foreach ($candidate in Get-ChildItem -LiteralPath $pack.FullName -Recurse -File -Filter "apphost.exe") {
                $hash = Get-PeSectionHash $candidate.FullName ".text"
                if (-not $hash) { continue }
                $packVersion = [IO.Path]::GetRelativePath($pack.FullName, $candidate.FullName).Replace('\', '/').Split('/')[0]
                if (-not $apphosts.ContainsKey($hash)) { $apphosts[$hash] = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase) }
                [void]$apphosts[$hash].Add("$packId $packVersion")
            }
        }
    }
    # pack and version -> the components (folders) its copies are in
    $found = @{}
    foreach ($exe in $Executables) {
        $relative = [IO.Path]::GetRelativePath($PlatformRoot, $exe.FullName).Replace('\', '/')
        $hash = Get-PeSectionHash $exe.FullName ".text"
        $matched = @(if ($hash -and $apphosts.ContainsKey($hash)) { $apphosts[$hash] })
        if ($matched.Count -ne 1) {
            $why = if ($matched.Count) { "matches several packs ($($matched -join ', '))" } else { "is not the apphost.exe of any Microsoft.NETCore.App.Host pack under $(($packRoots | Select-Object -Unique) -join ', ')" }
            $Failures.Add("$($relative): $why; run this where the tree was published, with that SDK, or its license is unknown")
            continue
        }
        if (-not $found.ContainsKey($matched[0])) { $found[$matched[0]] = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase) }
        [void]$found[$matched[0]].Add([IO.Path]::GetRelativePath($PlatformRoot, $exe.DirectoryName).Replace('\', '/'))
        $HostFiles.Add($relative)
    }
    foreach ($key in (ConvertTo-OrdinalOrder @($found.Keys))) {
        $hostId, $hostVersion = $key.Split(' ', 2)
        $hostPackage = [pscustomobject]@{ Id = $hostId; Version = $hostVersion }
        # the NuGet host packs carry dotnet/runtime's LICENSE.TXT and THIRD-PARTY-NOTICES.TXT; the SDK's copies don't.
        # the vendored ones go with whatever version matched, since release.yml's SDK floats (10.0.x); keying them to
        # the exact version would stop every servicing release. the source line names the version they're from.
        Add-Section -Kind "license" -Title "MIT" -Text (Read-Text (Join-Path $LicensesDir $DotnetRuntime.LicenseFile)) -Source "tools/release/licenses/$($DotnetRuntime.LicenseFile), from $($DotnetRuntime.Origin)" -Package $hostPackage
        Add-Section -Kind "notice" -Title "THIRD-PARTY-NOTICES.TXT" -Text (Read-Text (Join-Path $LicensesDir $DotnetRuntime.NoticeFile)) -Source "tools/release/licenses/$($DotnetRuntime.NoticeFile), from $($DotnetRuntime.Origin)" -Package $hostPackage
        $hostComponents = ConvertTo-OrdinalOrder @($found[$key])
        $HostRows.Add([pscustomobject]@{
            Package = $hostId; Version = $hostVersion; License = "MIT"; Copyright = $DotnetRuntime.Copyright
            ProjectUrl = $DotnetRuntime.ProjectUrl
            ShippedIn = if (-not (Compare-Object $hostComponents @($Components))) { "all" } else { $hostComponents -join ", " }
        })
    }
}

if ($Failures.Count) {
    $Host.UI.WriteErrorLine("Third-party license check failed for $($Failures.Count) item(s):")
    foreach ($failure in $Failures) { $Host.UI.WriteErrorLine("  $failure") }
    exit 1
}

$out = [Text.StringBuilder]::new()
function Add-Line([string]$Line = "") { [void]$out.Append($Line.TrimEnd()).Append("`n") }

Add-Line "RDCore third-party notices"
Add-Line "=========================="
Add-Line
Add-Line "This build of RDCore ships the third-party components listed below, each used under the license shown."
Add-Line "The license texts, and the notices their authors ask to be passed on, follow the summary."
Add-Line
Add-Line "RDCore is framework-dependent: the .NET shared framework (Microsoft.NETCore.App) is not part of this package and"
if ($HostRows.Count) {
    Add-Line "is not listed here. The .NET application host is part of it: RDCore's .exe files are copies of it, listed below."
}
else { Add-Line "is not listed here." }
Add-Line "RDCore's own license terms are in NOTICE.md and the LICENSE-*.md files at the root of this package."
Add-Line
Add-Line "Platform components: $((ConvertTo-OrdinalOrder $Components) -join ', ')"
Add-Line "Third-party packages: $($Rows.Count)"
if ($HostRows.Count) { Add-Line ".NET application host: $((ConvertTo-OrdinalOrder $HostFiles) -join ', ')" }
Add-Line

$TableRows = [Collections.Generic.List[object]]::new()
foreach ($row in @($Rows) + @($HostRows)) { $TableRows.Add($row) }
$TableRows.Sort([Comparison[object]] { param($a, $b) [StringComparer]::OrdinalIgnoreCase.Compare("$($a.Package) $($a.Version)", "$($b.Package) $($b.Version)") })
$columns = [ordered]@{
    Package = "Package"; Version = "Version"; License = "License"; Copyright = "Copyright"
    ProjectUrl = "Project URL"; ShippedIn = "Shipped in"
}
$widths = @{}
foreach ($column in $columns.Keys) {
    $lengths = @($TableRows | ForEach-Object { $_.$column.Length }) + $columns[$column].Length
    $widths[$column] = ($lengths | Measure-Object -Maximum).Maximum
}
$header = ($columns.Keys | ForEach-Object { $columns[$_].PadRight($widths[$_]) }) -join "  "
Add-Line $header
Add-Line (($columns.Keys | ForEach-Object { "-" * $widths[$_] }) -join "  ")
foreach ($row in $TableRows) {
    Add-Line (($columns.Keys | ForEach-Object { $row.$_.PadRight($widths[$_]) }) -join "  ")
}

# licenses first, then notices; each by title, then by the first package it applies to, then by hash
# (a package can ship two texts under one title)
$sortKeys = foreach ($hash in $Sections.Keys) {
    $section = $Sections[$hash]
    $kindOrder = if ($section.Kind -eq "license") { "0" } else { "1" }
    "$kindOrder`t$($section.Title)`t$((ConvertTo-OrdinalOrder @($section.Packages))[0])`t$hash"
}
foreach ($sortKey in (ConvertTo-OrdinalOrder @($sortKeys))) {
    $section = $Sections[$sortKey.Split("`t")[-1]]
    Add-Line
    Add-Line
    Add-Line ("=" * 100)
    $kindLabel = if ($section.Kind -eq "license") { "License" } else { "Notice" }
    Add-Line "$($kindLabel): $($section.Title)"
    Add-Line ("=" * 100)
    Add-Line "Applies to:"
    foreach ($packageName in (ConvertTo-OrdinalOrder @($section.Packages))) { Add-Line "    $packageName" }
    Add-Line "Text from:"
    foreach ($source in (ConvertTo-OrdinalOrder @($section.Sources))) { Add-Line "    $source" }
    Add-Line ("-" * 100)
    foreach ($line in $section.Text.Split("`n")) { Add-Line $line }
}

$outDir = [IO.Path]::GetDirectoryName($OutFile)
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) { New-Item $outDir -ItemType Directory -Force | Out-Null }
[IO.File]::WriteAllText($OutFile, $out.ToString(), [Text.UTF8Encoding]::new($false))

$breakdown = $Rows | Group-Object { $_.License -replace ' \(.*$', '' } | ForEach-Object { "$($_.Name): $($_.Count)" }
$hostSummary = if ($HostRows.Count) { " and the .NET apphost ($(($HostRows | ForEach-Object { "$($_.Package) $($_.Version)" }) -join ', '))" } else { "" }
Write-Host "Wrote $OutFile"
Write-Host "  $($Rows.Count) packages ($((ConvertTo-OrdinalOrder @($breakdown)) -join ', '))$hostSummary; $($Sections.Count) license/notice texts"
