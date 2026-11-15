# The figures of the bench job: from the reports of bench-rounds.ps1 and the MS-VBA reference run (RDCore.CLI/Bench/Reference/msvba.json), each case's
# ratio of RD-VBA's time to MS-VBA's, live: the reference run's ratio, scaled by how the merged commit's fastest run compares with the reference commit's on
# this machine (ratio = rdvba / msvba * merged / reference). Writes a shields.io endpoint file per case (bench-<case>.json), the median and the worst of
# each group (bench-<group>-median.json, bench-<group>-worst.json), and bench.json with every figure. The Excel automation cases are the reference run's
# own figures: no Excel runs here. A case that failed, or is missing from either side or from the reference, fails the script.
param(
    [Parameter(Mandatory)][string]$Runs,
    [Parameter(Mandatory)][string]$ReferenceData,
    [Parameter(Mandatory)][string]$Out,
    [Parameter(Mandatory)][string]$Commit,
    [string]$Runner = 'windows-latest'
)

$ErrorActionPreference = 'Stop'
$invariant = [Globalization.CultureInfo]::InvariantCulture

# rdc bench writes times to a tenth of a millisecond: a case under that resolution is counted at half of it.
$resolution = 0.05

$groups = [ordered]@{
    Calls      = @{ Slug = 'calls'; Title = 'Calls' }
    Statements = @{ Slug = 'statements'; Title = 'Statements' }
    Arrays     = @{ Slug = 'arrays'; Title = 'Arrays' }
    Strings    = @{ Slug = 'strings'; Title = 'Strings' }
    Numeric    = @{ Slug = 'numeric'; Title = 'Numeric' }
    Slots      = @{ Slug = 'slots'; Title = 'Variables and parameters' }
    StdLib     = @{ Slug = 'stdlib'; Title = 'Standard library' }
}

# the fastest run of each case, over every report of a side.
function Read-Fastest([string]$side) {
    $fastest = @{}
    $reports = @(Get-ChildItem -Path $Runs -Filter "$side-*.txt")
    if ($reports.Count -eq 0) {
        throw "no report for $side"
    }

    foreach ($report in $reports) {
        foreach ($line in Get-Content $report) {
            if ($line -match '^(?<case>[A-Za-z]\w*\.Bench\w+)\s+\d+\s+(?<min>[0-9.]+) ms\s+[0-9.]+ ms\s*$') {
                $time = [Math]::Max([double]::Parse($Matches.min, $invariant), $resolution)
                if (-not $fastest.ContainsKey($Matches.case) -or $time -lt $fastest[$Matches.case]) {
                    $fastest[$Matches.case] = $time
                }
            }
            elseif ($line -match '^[A-Za-z]\w*\.Bench\w+\s+\d+\s') {
                throw "$($report.Name): a case failed: $line"
            }
        }
    }

    $fastest
}

function Format-Ratio([double]$ratio) {
    if ($ratio -ge 10) { return $ratio.ToString('0', $invariant) + '×' }
    $ratio.ToString('0.0', $invariant) + '×'
}

function Format-Time([double]$milliseconds) {
    $format = if ($milliseconds -ge 100) { '0' } elseif ($milliseconds -ge 10) { '0.0' } else { '0.00' }
    $milliseconds.ToString($format, $invariant) + ' ms'
}

# a case's badge: its ratio, then RD-VBA's time against MS-VBA's on the reference machine.
function Format-Case([double]$ratio, [double]$rdvba, [double]$msvba) {
    "$(Format-Ratio $ratio) · $(Format-Time $rdvba) vs $(Format-Time $msvba)"
}

function Get-Color([double]$ratio) {
    if ($ratio -le 1) { 'brightgreen' }
    elseif ($ratio -le 2) { 'green' }
    elseif ($ratio -le 5) { 'yellowgreen' }
    elseif ($ratio -le 10) { 'yellow' }
    elseif ($ratio -le 30) { 'orange' }
    else { 'red' }
}

function Write-Badge([string]$name, [string]$label, [string]$message, [string]$color) {
    $badge = [ordered]@{ schemaVersion = 1; label = $label; message = $message; color = $color }
    $badge | ConvertTo-Json -Compress | Set-Content -Path (Join-Path $Out "$name.json") -Encoding utf8NoBOM
}

function Get-Median([double[]]$values) {
    $sorted = $values | Sort-Object
    $middle = [Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

# the median and the worst of a group's cases, as two badges and a row of bench.json.
function Add-Group([string]$title, [string]$slug, [object[]]$cases, [string]$label) {
    $median = Get-Median ($cases | ForEach-Object ratio)
    $worst = $cases | Sort-Object ratio -Descending | Select-Object -First 1
    $worstName = ($worst.name -split '\.', 2)[1] -replace '^Bench', ''
    Write-Badge "bench-$slug-median" $label (Format-Ratio $median) (Get-Color $median)
    Write-Badge "bench-$slug-worst" $label "$(Format-Ratio $worst.ratio) $worstName" (Get-Color $worst.ratio)
    [ordered]@{ name = $title; cases = $cases.Count; median = [Math]::Round($median, 2); worst = [ordered]@{ case = $worst.name; ratio = $worst.ratio } }
}

$data = Get-Content -Raw -Path $ReferenceData | ConvertFrom-Json
$merged = Read-Fastest 'merged'
$reference = Read-Fastest 'reference'
New-Item -ItemType Directory -Force -Path $Out | Out-Null

$cases = @()
foreach ($name in ($merged.Keys + $reference.Keys | Sort-Object -Unique)) {
    if (-not $merged.ContainsKey($name) -or -not $reference.ContainsKey($name)) {
        throw "$name was run by one build only"
    }

    $measured = $data.cases.$name
    if (-not ($measured.msvba -gt 0 -and $measured.rdvba -gt 0)) {
        throw "$name has no MS-VBA reference figure"
    }

    $module = ($name -split '\.', 2)[0]
    if (-not $groups.Contains($module)) {
        throw "$name is in no group"
    }

    # RD-VBA's time on the reference machine, as the merged commit would take it there.
    $rdvba = $measured.rdvba * $merged[$name] / $reference[$name]
    $ratio = [Math]::Round($rdvba / $measured.msvba, 2)
    $cases += [pscustomobject][ordered]@{
        name = $name; group = $groups[$module].Title; msvba = $measured.msvba; rdvbaReference = $measured.rdvba; rdvba = [Math]::Round($rdvba, 3)
        runnerMerged = $merged[$name]; runnerReference = $reference[$name]; ratio = $ratio
    }
    Write-Badge "bench-$name" '' (Format-Case $ratio $rdvba $measured.msvba) (Get-Color $ratio)
}

$summary = @()
foreach ($module in $groups.Keys) {
    $members = @($cases | Where-Object { $_.name.StartsWith("$module.", [StringComparison]::Ordinal) })
    if ($members.Count -gt 0) {
        $summary += Add-Group $groups[$module].Title $groups[$module].Slug $members ''
    }
}

# the Excel automation cases: the reference run's figures, labelled with its date.
$excel = @()
foreach ($property in @(if ($data.excel -is [pscustomobject]) { $data.excel.PSObject.Properties })) {
    if (-not ($property.Value.msvba -gt 0 -and $property.Value.rdvba -gt 0)) {
        throw "$($property.Name) has no reference figure"
    }

    $ratio = [Math]::Round($property.Value.rdvba / $property.Value.msvba, 2)
    $excel += [pscustomobject][ordered]@{ name = $property.Name; group = 'Excel automation'; msvba = $property.Value.msvba; rdvbaReference = $property.Value.rdvba; ratio = $ratio }
    Write-Badge "bench-$($property.Name)" "reference $($data.reference.date)" (Format-Case $ratio $property.Value.rdvba $property.Value.msvba) (Get-Color $ratio)
}

if ($excel.Count -gt 0) {
    $summary += Add-Group 'Excel automation' 'excel' $excel "reference $($data.reference.date)"
}

$figures = [ordered]@{
    commit = $Commit
    runner = $Runner
    method = "$(@(Get-ChildItem -Path $Runs -Filter 'merged-*.txt').Count) alternated rounds, fastest run"
    reference = $data.reference
    groups = $summary
    cases = $cases + $excel
}
$figures | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $Out 'bench.json') -Encoding utf8NoBOM
$summary | ForEach-Object { Write-Host ("{0,-20} {1,3} cases  median {2,6}  worst {3} {4}" -f $_.name, $_.cases, $_.median, $_.worst.ratio, $_.worst.case) }
