#Requires -Version 7.2
# runs the tests of the Wide category, which macOS runs where the whole suite takes too long (Windows and Linux run it
# all): RDCore.Tests/TestCategories.cs says what the category covers. the category is on the test classes, so a test
# that is renamed or moved keeps it.
#
# lists the tests the category picks in RDCore.Tests, already built in the configuration given, and fails if there are
# none or more than the limit, before it runs them.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet("Debug", "Release")][string]$Configuration,
    [int]$Limit = 1000
)

$ErrorActionPreference = "Stop"

$Project = Join-Path $PSScriptRoot "../../RDCore.Tests/RDCore.Tests.csproj"
$Filter = "TestCategory=Wide"

$Listed = @(& dotnet test $Project --configuration $Configuration --no-build --list-tests --filter $Filter)
if ($LASTEXITCODE -ne 0) {
    $Listed | Write-Host
    throw "dotnet test --list-tests exited with $LASTEXITCODE."
}

# the names follow the line that announces them, each indented
$Start = ($Listed | Select-String -SimpleMatch "The following Tests are available:" | Select-Object -First 1).LineNumber
$Tests = if ($Start) { @($Listed | Select-Object -Skip $Start | Where-Object { $_ -match '^\s+\S' } | ForEach-Object { $_.Trim() }) } else { @() }

$Tests | Write-Host
Write-Host "$Filter picks $($Tests.Count) tests (at most $Limit)."
if ($Tests.Count -eq 0) { throw "$Filter picks no test." }
if ($Tests.Count -gt $Limit) { throw "$Filter picks $($Tests.Count) tests, more than $Limit." }

& dotnet test $Project --configuration $Configuration --no-build --verbosity normal --blame-hang-timeout 10m --filter $Filter
exit $LASTEXITCODE
