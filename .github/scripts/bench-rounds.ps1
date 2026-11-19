# Runs `rdc bench` on the built-in corpus with two builds of rdc, in alternated rounds on the same machine, so that the machine's speed and its drift
# weigh on both alike: the merged commit's and the reference commit's (the commit of the MS-VBA reference run). Each round's report is written to
# <Out>/<side>-<round>.txt; the first side alternates from one round to the next. A build that fails to run the corpus fails the script.
param(
    [Parameter(Mandatory)][string]$Merged,
    [Parameter(Mandatory)][string]$Reference,
    [Parameter(Mandatory)][string]$Out,
    [int]$Rounds = 3,
    [int]$Runs = 7
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$sides = [ordered]@{ merged = $Merged; reference = $Reference }
for ($round = 1; $round -le $Rounds; $round++) {
    $order = if ($round % 2 -eq 1) { 'merged', 'reference' } else { 'reference', 'merged' }
    foreach ($side in $order) {
        Write-Host "== round $round, $side"
        $report = Join-Path $Out "$side-$round.txt"
        & $sides[$side] bench --runs $Runs --engine Compiled *> $report
        $code = $LASTEXITCODE
        Get-Content $report | Write-Host
        if ($code -ne 0) {
            throw "rdc bench ($side, round $round) exited with $code"
        }
    }
}
