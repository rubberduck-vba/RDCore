#Requires -Version 7.2
# the versions of the RDCore.SDK package a platform tree was built with, as the build resolved them: every component's .deps.json
# lists the packages it loads, by id and version. writes each version once, in ordinal order; a release has exactly one.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PlatformRoot
)

$ErrorActionPreference = "Stop"

$Package = "RDCore.SDK"
$Versions = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in Get-ChildItem -LiteralPath $PlatformRoot -Recurse -File -Filter "*.deps.json" -Force) {
    $document = [Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($file.FullName))
    try {
        $libraries = [Text.Json.JsonElement]::new()
        if (-not $document.RootElement.TryGetProperty("libraries", [ref]$libraries)) { continue }
        foreach ($library in $libraries.EnumerateObject()) {
            # "RDCore.SDK/0.1.1-alpha": { "type": "package", ... }; a project of that name is no package
            $name, $version = $library.Name.Split("/", 2)
            $type = [Text.Json.JsonElement]::new()
            if ($name -ceq $Package -and $version -and $library.Value.TryGetProperty("type", [ref]$type) -and $type.GetString() -ceq "package") {
                [void]$Versions.Add($version)
            }
        }
    }
    finally { $document.Dispose() }
}

$Versions
