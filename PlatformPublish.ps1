[CmdletBinding()]
param(
    [switch]$Silent,
    [string]$Configuration = "Debug",
    [string]$PlatformRoot = "artifacts\rdcore-dev"
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

$RepoRoot = Resolve-Path $PSScriptRoot
$StagingRoot = Join-Path $RepoRoot "artifacts\staging"
$PlatformRoot = Join-Path $RepoRoot "artifacts\rdcore-dev"

Write-Host "Validate paths:"
Write-Host "📁 Staging: " $StagingRoot
Write-Host "📁 Platform:" $PlatformRoot

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

Remove-Item $StagingRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $PlatformRoot -Recurse -Force -ErrorAction SilentlyContinue

New-Item $StagingRoot -ItemType Directory -Force | Out-Null
New-Item $PlatformRoot -ItemType Directory -Force | Out-Null

Write-Host "✅ Done."

Write-Host ""
Write-Host "Publishing projects..."
Write-Host ""

foreach ($project in $Projects)
{
    $publishPath = Join-Path $StagingRoot $project.Name
    $projPath = Join-Path $RepoRoot $project.Project

    New-Item $publishPath -ItemType Directory -Force | Out-Null

    dotnet publish $projPath --configuration $Configuration --output $publishPath

    if ($LASTEXITCODE -ne 0)
    {
        throw "Publish failed for $($project.Name)"
    }

    Write-Host "🚀 $($project.Name) was published successfully."
}

Write-Host ""
Write-Host "🧩 Assembling platform..."
Write-Host ""

$PlatformFolders = @(
    "Config",
    "Extensions",
    "Logs"
)

foreach ($folder in $PlatformFolders)
{
    New-Item `
        (Join-Path $PlatformRoot $folder) `
        -ItemType Directory `
        -Force | Out-Null
}

foreach ($project in $Projects)
{
    $source = Join-Path $StagingRoot $project.Name
    $target = Join-Path $PlatformRoot $project.Name

    New-Item $target -ItemType "directory"
    Copy-Item -Path "$source\*" -Destination "$target\" -Recurse
}

$Manifest = @{
    platformVersion = "0.1"
    generatedUtc = (Get-Date).ToUniversalTime().ToString("O")

    hostService = "RDCore.CLI/rdc.exe"
    langService = "RDCore.LanguageServer/RDCore.LanguageServer.exe"
    parseServer = "RDCore.Parsing/RDCore.ParseServer.exe"

    extensionsDirectory = "Extensions"
} | ConvertTo-Json -Depth 10

$ManifestPath = Join-Path $PlatformRoot "rdcore.json"
$Manifest | Set-Content $ManifestPath -Encoding UTF8

Write-Host ""
Write-Host "🧩 Generating extension manifests..."
Write-Host ""

# each extension gets an extension.manifest.json, reflected off its executable by `rdc.exe describe-ext`
# (command mode, unsafe dev signing). The language server discovers extensions by this manifest.
$Rdc = Join-Path $PlatformRoot "RDCore.CLI\rdc.exe"

$Extensions = @(
    @{ Folder = "RDCore.Diagnostics"; Executable = "RDCore.Diagnostics.exe"; Description = "RDCore core inspection extension." }
)

foreach ($extension in $Extensions)
{
    $extensionFolder = Join-Path $PlatformRoot (Join-Path "Extensions" $extension.Folder)

    Push-Location $extensionFolder
    try
    {
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