#Requires -Version 7.2
# writes SOURCE.md, then release.json, into a platform root that is otherwise complete (THIRD-PARTY-NOTICES.txt
# included): release.json lists the size and SHA-256 of every file, so it is written last.
#
# SOURCE.md is the "clear directions" GPLv3 section 6(d) asks for next to the object code: where the corresponding
# source of this exact build is. release.json is the machine-readable description of the release (schema 1).
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PlatformRoot,
    # owner/repo, e.g. rubberduck-vba/RDCore
    [Parameter(Mandatory)][string]$Repository,
    # e.g. https://github.com
    [Parameter(Mandatory)][string]$ServerUrl,
    # the release tag, on tag builds only
    [string]$Tag,
    # the workflow run that built the tree
    [string]$RunUrl
)

$ErrorActionPreference = "Stop"

$Root = (Resolve-Path -LiteralPath $PlatformRoot).Path
if ($Repository -notmatch "^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$") { throw "Repository '$Repository' is not owner/repo." }
$ServerUrl = $ServerUrl.TrimEnd("/")
if ($ServerUrl -notmatch "^https?://[^\s/]+$") { throw "ServerUrl '$ServerUrl' is not a server URL." }
$RepositoryUrl = "$ServerUrl/$Repository"

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

# UTF-8 without BOM, LF, trailing newline
function Write-TextFile([string]$Path, [string[]]$Lines) {
    [IO.File]::WriteAllText($Path, (($Lines -join "`n").TrimEnd("`n") + "`n"), [Text.UTF8Encoding]::new($false))
}

$Manifest = Read-PlatformManifest (Join-Path $Root "rdcore.json")
foreach ($key in "platformVersion", "commit", "rid", "hostService", "langService", "parseServer", "extensionsDirectory") {
    if (-not $Manifest[$key]) { throw "rdcore.json has no '$key'." }
}
$Version = $Manifest.platformVersion
$Commit = $Manifest.commit
# the tag names the version the binaries were built as, or the release would be mislabelled
if ($Tag -and $Tag -cne "v$Version") { throw "Tag '$Tag' does not match the built version '$Version' (expected 'v$Version')." }

# the framework the language server was built against is the minimum runtime; a self-contained tree has none
$RuntimeConfigPath = Join-Path $Root ([IO.Path]::ChangeExtension($Manifest.langService, ".runtimeconfig.json"))
$RuntimeOptions = (Get-Content -LiteralPath $RuntimeConfigPath -Raw | ConvertFrom-Json).runtimeOptions
$Framework = @($RuntimeOptions.framework) + @($RuntimeOptions.frameworks) | Where-Object { $_ -and $_.name -eq "Microsoft.NETCore.App" } | Select-Object -First 1
if (-not $Framework) { throw "$(Get-RelativePath $RuntimeConfigPath) names no Microsoft.NETCore.App framework; is the tree framework-dependent?" }

$SourceRef = if ($Tag) { $Tag } else { $Commit }
$SourceUrl = "$RepositoryUrl/tree/$SourceRef"
$RepositoryName = $Repository.Split("/")[1]

$English = @(
    "# RDCore ${Version}: source code"
    ""
    "This archive contains RDCore $Version (``$($Manifest.rid)``), built from $RepositoryUrl."
    ""
    "The complete corresponding source code of this build is available at no charge at:"
    ""
    "<$SourceUrl>"
    ""
    "- Commit: ``$Commit``"
    $(if ($Tag) { "- Tag: ``$Tag``" } else { "- Tag: none (this build is not a tagged release)" })
    $(if ($RunUrl) { "- Built by: <$RunUrl>" })
    ""
    "With git:"
    ""
    "    git clone $RepositoryUrl.git"
    "    cd $RepositoryName"
    "    git checkout $Commit"
    ""
    "The scripts that built and packaged it (``PlatformPublish.ps1``, ``tools/release/``, ``.github/workflows/release.yml``) are part of that source."
    ""
    "Both licence texts, ``LICENSE-GPLv3.md`` and ``LICENSE-MIT.md``, are included in this archive; ``NOTICE.md`` says which licence covers which part of RDCore, and ``THIRD-PARTY-NOTICES.txt`` lists the third-party components it contains and their licences."
) | Where-Object { $null -ne $_ }

$French = @(
    "# RDCore $Version : code source"
    ""
    "Cette archive contient RDCore $Version (``$($Manifest.rid)``), compilé à partir de $RepositoryUrl."
    ""
    "Le code source correspondant complet de cette compilation est disponible gratuitement à l'adresse :"
    ""
    "<$SourceUrl>"
    ""
    "- Commit : ``$Commit``"
    $(if ($Tag) { "- Étiquette : ``$Tag``" } else { "- Étiquette : aucune (cette compilation n'est pas une version étiquetée)" })
    $(if ($RunUrl) { "- Compilé par : <$RunUrl>" })
    ""
    "Avec git :"
    ""
    "    git clone $RepositoryUrl.git"
    "    cd $RepositoryName"
    "    git checkout $Commit"
    ""
    "Les scripts qui l'ont compilée et empaquetée (``PlatformPublish.ps1``, ``tools/release/``, ``.github/workflows/release.yml``) font partie de ce code source."
    ""
    "Les deux textes de licence, ``LICENSE-GPLv3.md`` et ``LICENSE-MIT.md``, sont inclus dans cette archive; ``NOTICE.md`` précise quelle licence couvre quelle partie de RDCore, et ``THIRD-PARTY-NOTICES.txt`` énumère les composants tiers qu'elle contient et leurs licences."
) | Where-Object { $null -ne $_ }

Write-TextFile (Join-Path $Root "SOURCE.md") ($English + @("", "---", "") + $French)

# every file of the tree except release.json itself, the checksums file and anything a run left under Logs/
$Paths = [string[]]@(Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
    ForEach-Object { Get-RelativePath $_.FullName } |
    Where-Object { $_ -cne "release.json" -and $_ -cne "SHA256SUMS" -and -not $_.StartsWith("Logs/", [StringComparison]::OrdinalIgnoreCase) })
[Array]::Sort($Paths, [StringComparer]::Ordinal)

$Files = foreach ($path in $Paths) {
    $file = Get-Item -LiteralPath (Join-Path $Root $path) -Force
    [ordered]@{
        path   = $path
        size   = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$LanguageServer = $Manifest.langService
$Release = [ordered]@{
    schema        = 1
    name          = "RDCore"
    version       = $Version
    commit        = $Commit
    rid           = $Manifest.rid
    deployment    = "framework-dependent"
    runtime       = [ordered]@{
        name           = "Microsoft.NETCore.App"
        minimumVersion = [string]$Framework.version
    }
    entryPoints   = [ordered]@{
        hostService         = $Manifest.hostService
        langService         = $LanguageServer
        parseServer         = $Manifest.parseServer
        extensionsDirectory = $Manifest.extensionsDirectory
    }
    launch        = [ordered]@{
        languageServer = [ordered]@{
            path             = $LanguageServer
            # appsettings.json is read from the working directory
            workingDirectory = [IO.Path]::GetDirectoryName($LanguageServer).Replace("\", "/")
            # without the client's process ID the server, and the processes it starts, never exit when the client dies
            arguments        = @("--client-process-id", "{clientProcessId}", "--pipe-name", "{pipe}", "--workspace", "{workspaceUri}")
        }
    }
    source        = [ordered]@{
        repository = $RepositoryUrl
        tag        = if ($Tag) { $Tag } else { $null }
        commit     = $Commit
    }
    hashAlgorithm = "sha256"
    files         = @($Files)
}

# ConvertTo-Json indents with 2 spaces but ends lines with the platform's newline
$Json = ($Release | ConvertTo-Json -Depth 10).Replace("`r`n", "`n")
Write-TextFile (Join-Path $Root "release.json") @($Json)

Write-Host "📄 SOURCE.md: $SourceUrl"
Write-Host "📄 release.json: RDCore $Version ($Commit, $($Manifest.rid)), $($Paths.Count) files, runtime $($Framework.name) $($Framework.version)+"
