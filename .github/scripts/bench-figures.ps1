# The figures of the bench job: from the reports of bench-rounds.ps1 and the MS-VBA reference run (RDCore.CLI/Bench/Reference/msvba.json), each case's
# ratio of RD-VBA's time to MS-VBA's, live: the reference run's ratio, scaled by how the merged commit's fastest run compares with the reference commit's on
# this machine (ratio = rdvba / msvba * merged / reference). Writes a shields.io endpoint file per case (bench-<case>.json), the fastest case, the median
# and the slowest case of each group (bench-<group>-best.json, bench-<group>-median.json, bench-<group>-worst.json), and bench.json with every figure. The
# Excel automation cases are the reference run's own figures: no Excel runs here. A case added since the reference run has its time on this machine and no
# ratio, and is in no group's figures; a case the merged commit no longer has is left out. A case that failed, or a report with no case in it, fails the
# script. Where the merged commit's report gives them, bench.json also has what the case's fastest run on this machine allocated and the collections made
# while it ran (runnerMergedAllocatedKB, runnerMergedCollections): they do not depend on the machine's speed. They have badges of their own
# (bench-<case>-allocated.json, bench-<case>-collections.json). Each group also has a total (bench-<group>-total.json): the sum of its cases' RD-VBA times
# over the sum of their MS-VBA times, with the sums of what they allocated and collected beside it (bench-<group>-total-allocated.json,
# bench-<group>-total-collections.json). In the Excel automation group, a case's Total phase stands for its other phases, so only it is summed. The run
# that measured the figures is in bench.json (run) and in bench-run.json.
param(
    [Parameter(Mandatory)][string]$Runs,
    [Parameter(Mandatory)][string]$ReferenceData,
    [Parameter(Mandatory)][string]$Out,
    [Parameter(Mandatory)][string]$Commit,
    [string]$Runner = 'windows-latest',
    [string]$RunnerCpu,
    [string]$RunUrl,
    [string]$RunNumber
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

# the fastest run of each case, over every report of a side; into $costs, what that run allocated and the collections made while it ran, when the report
# gives them (an rdc that predates them does not).
function Read-Fastest([string]$side, [hashtable]$costs = @{}) {
    $fastest = @{}
    $reports = @(Get-ChildItem -Path $Runs -Filter "$side-*.txt")
    if ($reports.Count -eq 0) {
        throw "no report for $side"
    }

    foreach ($report in $reports) {
        $found = 0
        foreach ($line in Get-Content $report) {
            if ($line -match '^(?<case>[A-Za-z]\w*\.Bench\w+)\s+\d+\s+(?<min>[0-9.]+) ms\s+[0-9.]+ ms(\s+(?<kb>\d+) KB\s+(?<g0>\d+)/(?<g1>\d+)/(?<g2>\d+))?\s*$') {
                $found++
                $time = [Math]::Max([double]::Parse($Matches.min, $invariant), $resolution)
                if (-not $fastest.ContainsKey($Matches.case) -or $time -lt $fastest[$Matches.case]) {
                    $fastest[$Matches.case] = $time
                    $costs.Remove($Matches.case)
                    if ($Matches.kb) {
                        $costs[$Matches.case] = [ordered]@{
                            allocatedKB = [long]$Matches.kb
                            collections = [ordered]@{ gen0 = [int]$Matches.g0; gen1 = [int]$Matches.g1; gen2 = [int]$Matches.g2 }
                        }
                    }
                }
            }
            elseif ($line -match '^[A-Za-z]\w*\.Bench\w+\s+\d+\s') {
                throw "$($report.Name): a case failed: $line"
            }
        }

        if ($found -eq 0) {
            throw "$($report.Name): no case could be read"
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

# what a run allocated, in KB or MB as fits.
function Format-Size([long]$kilobytes) {
    if ($kilobytes -lt 1024) { return "$kilobytes KB" }
    $megabytes = $kilobytes / 1024.0
    $format = if ($megabytes -ge 100) { '0' } else { '0.0' }
    $megabytes.ToString($format, $invariant) + ' MB'
}

# the collections of each generation, gen 0/1/2.
function Format-Collections([object]$collections) {
    "$($collections.gen0)/$($collections.gen1)/$($collections.gen2)"
}

# a case's allocation badges, when the merged commit's report gives its costs.
function Write-CostBadges([string]$name, [object]$costs) {
    Write-Badge "$name-allocated" '' (Format-Size $costs.allocatedKB) 'blue'
    Write-Badge "$name-collections" '' (Format-Collections $costs.collections) 'blue'
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

# a case in a group's badge: its ratio and its name, without the module and the Bench prefix.
function Format-GroupCase([object]$case) {
    "$(Format-Ratio $case.ratio) $(($case.name -split '\.', 2)[1] -replace '^Bench', '')"
}

# the fastest case, the median and the slowest case of a group, as three badges and a row of bench.json. Cases of the same ratio go by name. The total
# sums the cases of $summed, RD-VBA's times over MS-VBA's; its allocation badges sum the costs of those cases when every one of them has its costs.
function Add-Group([string]$title, [string]$slug, [object[]]$cases, [string]$label, [object[]]$summed) {
    $median = Get-Median ($cases | ForEach-Object ratio)
    $ranked = @($cases | Sort-Object ratio, name)
    $best = $ranked[0]
    $worst = $ranked[-1]
    Write-Badge "bench-$slug-best" $label (Format-GroupCase $best) (Get-Color $best.ratio)
    Write-Badge "bench-$slug-median" $label (Format-Ratio $median) (Get-Color $median)
    Write-Badge "bench-$slug-worst" $label (Format-GroupCase $worst) (Get-Color $worst.ratio)

    $rdvba = ($summed | ForEach-Object { if ($null -ne $_.rdvba) { $_.rdvba } else { $_.rdvbaReference } } | Measure-Object -Sum).Sum
    $msvba = ($summed | Measure-Object -Property msvba -Sum).Sum
    $ratio = [Math]::Round($rdvba / $msvba, 2)
    Write-Badge "bench-$slug-total" $label (Format-Case $ratio $rdvba $msvba) (Get-Color $ratio)
    $total = [ordered]@{ cases = $summed.Count; rdvba = [Math]::Round($rdvba, 3); msvba = [Math]::Round($msvba, 3); ratio = $ratio }

    $costs = @($summed | ForEach-Object { $mergedCosts[$_.name] } | Where-Object { $_ })
    if ($costs.Count -gt 0 -and $costs.Count -eq $summed.Count) {
        $total.runnerMergedAllocatedKB = [long]($costs | ForEach-Object allocatedKB | Measure-Object -Sum).Sum
        $total.runnerMergedCollections = [ordered]@{
            gen0 = [int]($costs | ForEach-Object { $_.collections.gen0 } | Measure-Object -Sum).Sum
            gen1 = [int]($costs | ForEach-Object { $_.collections.gen1 } | Measure-Object -Sum).Sum
            gen2 = [int]($costs | ForEach-Object { $_.collections.gen2 } | Measure-Object -Sum).Sum
        }
        Write-CostBadges "bench-$slug-total" ([pscustomobject]@{ allocatedKB = $total.runnerMergedAllocatedKB; collections = $total.runnerMergedCollections })
    }

    [ordered]@{
        name = $title; cases = $cases.Count; best = [ordered]@{ case = $best.name; ratio = $best.ratio }; median = [Math]::Round($median, 2)
        worst = [ordered]@{ case = $worst.name; ratio = $worst.ratio }; total = $total
    }
}

$data = Get-Content -Raw -Path $ReferenceData | ConvertFrom-Json
$mergedCosts = @{}
$merged = Read-Fastest 'merged' $mergedCosts
$reference = Read-Fastest 'reference'
New-Item -ItemType Directory -Force -Path $Out | Out-Null

# a case's row of bench.json, with what the merged commit's fastest run allocated when its report gives it.
function New-CaseRow([string]$name, [System.Collections.Specialized.OrderedDictionary]$row) {
    if ($mergedCosts[$name]) {
        $row.runnerMergedAllocatedKB = $mergedCosts[$name].allocatedKB
        $row.runnerMergedCollections = $mergedCosts[$name].collections
    }
    [pscustomobject]$row
}

$cases = @()
foreach ($name in ($merged.Keys | Sort-Object)) {
    $measured = $data.cases.$name
    if ($mergedCosts[$name]) {
        Write-CostBadges "bench-$name" $mergedCosts[$name]
    }

    $module = ($name -split '\.', 2)[0]
    $group = if ($groups.Contains($module)) { $groups[$module].Title }

    # a case the reference run did not have: its time on this machine, and no ratio.
    if (-not ($reference.ContainsKey($name) -and $measured.msvba -gt 0 -and $measured.rdvba -gt 0)) {
        $cases += New-CaseRow $name ([ordered]@{ name = $name; group = $group; runnerMerged = $merged[$name]; ratio = $null })
        Write-Badge "bench-$name" '' "$(Format-Time $merged[$name]) · not in the reference run" 'lightgrey'
        continue
    }

    # RD-VBA's time on the reference machine, as the merged commit would take it there.
    $rdvba = $measured.rdvba * $merged[$name] / $reference[$name]
    $ratio = [Math]::Round($rdvba / $measured.msvba, 2)
    $cases += New-CaseRow $name ([ordered]@{
        name = $name; group = $group; msvba = $measured.msvba; rdvbaReference = $measured.rdvba; rdvba = [Math]::Round($rdvba, 3)
        runnerMerged = $merged[$name]; runnerReference = $reference[$name]; ratio = $ratio
    })
    Write-Badge "bench-$name" '' (Format-Case $ratio $rdvba $measured.msvba) (Get-Color $ratio)
}

$summary = @()
foreach ($module in $groups.Keys) {
    $members = @($cases | Where-Object { $null -ne $_.ratio -and $_.name.StartsWith("$module.", [StringComparison]::Ordinal) })
    if ($members.Count -gt 0) {
        $summary += Add-Group $groups[$module].Title $groups[$module].Slug $members '' $members
    }
}

# the Excel automation cases: the reference run's figures, labeled with its date.
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
    # a case's Total phase is the sum of its other phases: where a case has one, only it goes into the group's total.
    $isTotal = { param($name) $name.EndsWith('.Total', [StringComparison]::Ordinal) }
    $totalled = @($excel | Where-Object { & $isTotal $_.name } | ForEach-Object { $_.name.Substring(0, $_.name.LastIndexOf('.')) })
    $summed = @($excel | Where-Object { (& $isTotal $_.name) -or $totalled -notcontains $_.name.Substring(0, $_.name.LastIndexOf('.')) })
    $summary += Add-Group 'Excel automation' 'excel' $excel "reference $($data.reference.date)" $summed
}

# the run that measured the figures: a badge that names it, carried over with the figures when a later bench job fails.
if ($RunNumber) {
    Write-Badge 'bench-run' 'measured in CI run' "#$RunNumber · $($Commit.Substring(0, [Math]::Min(9, $Commit.Length)))" 'blue'
}

$figures = [ordered]@{
    commit = $Commit
    run = if ($RunUrl) { $RunUrl } else { $null }
    runner = $Runner
    runnerCpu = if ($RunnerCpu) { $RunnerCpu } else { $null }
    method = "$(@(Get-ChildItem -Path $Runs -Filter 'merged-*.txt').Count) alternated rounds, fastest run$(if ($mergedCosts.Count -gt 0) { ', the heap collected before each run' })"
    reference = $data.reference
    groups = $summary
    cases = $cases + $excel
}
$figures | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $Out 'bench.json') -Encoding utf8NoBOM
$summary | ForEach-Object {
    Write-Host ("{0,-24} {1,3} cases  fastest {2} {3}  median {4,6}  slowest {5} {6}  total {7}" -f $_.name, $_.cases, $_.best.ratio, $_.best.case,
        $_.median, $_.worst.ratio, $_.worst.case, $_.total.ratio)
}
