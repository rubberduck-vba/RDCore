# Benchmarks: Excel automation
<sup>_Ce document est disponible en [français](excel-automation.fr.md)_</sup>

---
VBA code that drives Excel through its object model, late-bound: a block of cells read into a `Variant` at once, worked on, and written to another
sheet at once, then the same work done cell by cell. Each case gives the time of each phase on its own.

The source is the module [`ExcelCases.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Excel/ExcelCases.bas), embedded in
`rdc`. These cases need Excel, so they are measured in the reference run only (see [Measuring MS-VBA](index.md#measuring-ms-vba)), and their badges
give the reference run's date instead of a live figure. Each badge gives RD-VBA's time as a multiple of MS-VBA's
([how the figures are measured](../performance.md#how-the-figures-are-measured)).

## How the cases run

With `rdc bench-msvba`, both sides run the cases against an Excel instance of their own, in another process, so that every call to Excel's object
model crosses a process boundary. RD-VBA runs them from the program `Program.bas`, which starts that instance and gives the number of runs from the command line:

[!code-vb[](../../RDCore.CLI/Bench/Excel/Program.bas#Main)]

MS-VBA runs `RunExcelCases` from a workbook in Excel, against a second Excel instance it starts. `RunExcelCases` turns off screen updating, events
and automatic calculation, fills a sheet with 100,000 × 10 cells, and runs the cases in this order:

[!code-vb[](../../RDCore.CLI/Bench/Excel/ExcelCases.bas#RunExcelCases)]

## Declarations

The module declares the counter its times are read from, in microseconds:

[!code-vb[](../../RDCore.CLI/Bench/Excel/ExcelCases.bas#Declarations)]

[!code-vb[](../../RDCore.CLI/Bench/Excel/ExcelCases.bas#Since)]

The source sheet holds numbers, and a blank cell where the sum of the row and the column is a multiple of 7:

[!code-vb[](../../RDCore.CLI/Bench/Excel/ExcelCases.bas#Fill)]

## Cases

### Block

The cells read into a `Variant` with one call (`read`), every element that is not `Empty` doubled with no call to Excel (`pass`), and the array written
to the other sheet with one call (`write`). `total` is the three together. The case runs on 1,000 × 10 cells, then on 100,000 × 10.

| Phase | 1,000 × 10 | 100,000 × 10 |
|---|---|---|
| Read | ![Excel.Block1000x10.Read](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Read.json) | ![Excel.Block100000x10.Read](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Read.json) |
| Pass | ![Excel.Block1000x10.Pass](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Pass.json) | ![Excel.Block100000x10.Pass](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Pass.json) |
| Write | ![Excel.Block1000x10.Write](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Write.json) | ![Excel.Block100000x10.Write](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Write.json) |
| Total | ![Excel.Block1000x10.Total](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Total.json) | ![Excel.Block100000x10.Total](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Total.json) |

[!code-vb[](../../RDCore.CLI/Bench/Excel/ExcelCases.bas#Block)]

### CellByCell

The same work on 1,000 × 10 cells, one cell at a time: a call to Excel to read each cell and another to write it, 20,000 calls a run.

![Excel.CellByCell1000x10.Total](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.CellByCell1000x10.Total.json)

[!code-vb[](../../RDCore.CLI/Bench/Excel/ExcelCases.bas#CellByCell)]

## See also

- [Benchmarks](index.md)
- [Performance](../performance.md#excel-automation)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
