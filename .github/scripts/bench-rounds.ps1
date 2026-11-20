# Runs `rdc bench` on the built-in corpus with two builds of rdc, in alternated rounds on the same machine, so that the machine's speed and its drift
# weigh on both alike: the merged commit's and the reference commit's (the commit of the MS-VBA reference run). Each round's report is written to
# <Out>/<side>-<round>.txt; the first side alternates from one round to the next. A build that fails to run the corpus fails the script.
# rdc bench warms each case up until the JIT is quiet before its timed runs. Both builds are run the same way: warmed up, when both can be; when a build
# predates the warm-up (its bench has no --no-warm-up), neither is, and each case is run ColdRuns times instead, so that the fastest run of each build
# comes from runs made once its code has settled.
param(
    [Parameter(Mandatory)][string]$Merged,
    [Parameter(Mandatory)][string]$Reference,
    [Parameter(Mandatory)][string]$Out,
    [int]$Rounds = 3,
    [int]$Runs = 7,
    [int]$ColdRuns = 40
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$sides = [ordered]@{ merged = $Merged; reference = $Reference }
$warms = @{}
foreach ($side in $sides.Keys) {
    $warms[$side] = (& $sides[$side] bench --help *>&1 | Out-String) -match '--no-warm-up'
}

$both = $warms.merged -and $warms.reference
$arguments = @{}
foreach ($side in $sides.Keys) {
    $arguments[$side] = if ($both) { @('--runs', $Runs) } elseif ($warms[$side]) { @('--runs', $ColdRuns, '--no-warm-up') } else { @('--runs', $ColdRuns) }
}

Write-Host "== $(if ($both) { "both builds warm each case up, then $Runs timed runs" } else { "a build predates the warm-up: no warm-up on either, $ColdRuns runs a case" })"

for ($round = 1; $round -le $Rounds; $round++) {
    $order = if ($round % 2 -eq 1) { 'merged', 'reference' } else { 'reference', 'merged' }
    foreach ($side in $order) {
        Write-Host "== round $round, $side"
        $report = Join-Path $Out "$side-$round.txt"
        $each = $arguments[$side]
        & $sides[$side] bench @each --engine Compiled *> $report
        $code = $LASTEXITCODE
        Get-Content $report | Write-Host
        if ($code -ne 0) {
            throw "rdc bench ($side, round $round) exited with $code"
        }
    }
}
