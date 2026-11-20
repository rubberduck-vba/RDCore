# Runs `rdc bench` on the built-in corpus with two builds of rdc, in alternated rounds on the same machine, so that the machine's speed and its drift
# weigh on both alike: the merged commit's and the reference commit's (the commit of the MS-VBA reference run). Each round's report is written to
# <Out>/<side>-<round>.txt; the first side alternates from one round to the next. A build that fails to run the corpus fails the script.
# rdc bench warms each case up until the JIT is quiet before its timed runs. A build of rdc that predates the warm-up (its bench has no --no-warm-up) is
# given ColdRuns runs a case instead, so that its fastest run also comes from runs made once its code has settled.
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
$runsOf = @{}
foreach ($side in $sides.Keys) {
    $help = & $sides[$side] bench --help *>&1 | Out-String
    $runsOf[$side] = if ($help -match '--no-warm-up') { $Runs } else { $ColdRuns }
    Write-Host "== $side`: $($runsOf[$side]) runs a case, $(if ($runsOf[$side] -eq $Runs) { 'after its warm-up' } else { 'no warm-up of its own' })"
}

for ($round = 1; $round -le $Rounds; $round++) {
    $order = if ($round % 2 -eq 1) { 'merged', 'reference' } else { 'reference', 'merged' }
    foreach ($side in $order) {
        Write-Host "== round $round, $side"
        $report = Join-Path $Out "$side-$round.txt"
        & $sides[$side] bench --runs $runsOf[$side] --engine Compiled *> $report
        $code = $LASTEXITCODE
        Get-Content $report | Write-Host
        if ($code -ne 0) {
            throw "rdc bench ($side, round $round) exited with $code"
        }
    }
}
