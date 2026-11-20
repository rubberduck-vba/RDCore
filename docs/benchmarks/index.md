# Benchmarks
<sup>_Ce document est disponible en [français](index.fr.md)_</sup>

---
`rdc bench` times a set of VBA modules built into `rdc`: the corpus. Each case is a `Public Sub Bench*()` of one of its standard modules, and its class
modules hold what the cases call. The pages below give each case's source, as it runs. Its figure, and how the figures are measured, are on the
[Performance](../performance.md) page.

## The groups

| Group | Module | What its cases measure |
|---|---|---|
| [Calls](calls.md) | `Calls.bas` | Calls to a procedure, to the members of an object, and to the standard library |
| [Statements](statements.md) | `Statements.bas` | `Set`, member and field assignments, `Select Case`, conditions, the `Mid$` statement, `On Error Resume Next` |
| [Arrays](arrays.md) | `Arrays.bas` | Every element of a `Long` array, and of 2-D `Variant` arrays |
| [Strings](strings.md) | `Strings.bas` | Assignment, concatenation and comparison, and strings given to a procedure |
| [Numeric](numeric.md) | `Numeric.bas` | Arithmetic over typed and `Variant` operands |
| [Variables and parameters](variables-and-parameters.md) | `Slots.bas` | Parameters and locals, a module variable, a `Static` local |
| [Standard library](standard-library.md) | `StdLib.bas` | One function of the standard library a case |
| [Excel automation](excel-automation.md) | `ExcelCases.bas` | Cells of a sheet read and written, as a block and cell by cell |

The Excel automation cases are not part of the corpus. They need Excel, so they are measured in the reference run only
(see [Measuring MS-VBA](#measuring-ms-vba)).

## Running the benchmarks

With `rdc` installed (see [Getting started with RDCore](../getting-started.md)), this runs every case of the corpus:

```
rdc bench
```

| Option | What it does |
|---|---|
| `--runs <n>`, `-r <n>` | Runs each case `n` times (3 by default). |
| `--filter <text>`, `-f <text>` | Runs only the cases whose `Module.Procedure` name contains the text, such as `--filter Strings.` |
| `--list`, `-l` | Lists the cases without running them. |
| `--engine <engine>`, `-e <engine>` | Runs the cases with `Compiled` or `Interpreter`, instead of the configured execution engine. |
| `--no-warm-up` | Times each case from its first run. |

Before its timed runs, each case is run untimed until .NET has finished optimizing the code it calls: at least 31 times, then until the JIT compiler
has compiled nothing for 250 ms, for 5 seconds at the most. Until then a run can take several times as long as the runs after it. The report's
`warm-up` line gives how many untimed runs were made over all the cases, or `none` with `--no-warm-up`.

For each case, the report gives its fastest and its median run, what its fastest run allocated, and the garbage collections of each generation made
while it ran. Before the cases, it gives how long the corpus took to parse, load and analyze.

The reference run keeps the fastest of seven runs, as `rdc bench --runs 7` does. Times on another machine differ from the reference machine's, so
compare runs made on the same machine: two builds of `rdc`, or your code before and after a change.

`rdc bench <path>` times the `Public Sub Bench*()` of a workspace's standard modules instead of the corpus (see
[The shell and the rdc commands](../rdc.md)).

## Measuring MS-VBA

The reference run measures MS-VBA and RD-VBA on the same machine, with `rdc bench-msvba`. It needs Windows, Excel, and Excel's "Trust access to the
VBA project object model" setting (File > Options > Trust Center > Trust Center Settings > Macro Settings).

```
rdc bench-msvba
```

It runs the corpus and the Excel automation cases in MS-VBA, in a hidden Excel, and in RD-VBA, in alternated rounds: MS-VBA first in odd rounds,
RD-VBA first in even rounds. RD-VBA warms each case of the corpus up as `rdc bench` does; MS-VBA runs it twice untimed first, since it compiles a
procedure once, the first time it is called. The command keeps the fastest run of each case on each side, over all the rounds, and writes the
figures to `msvba.json`. Nothing is written unless every case has a figure on both sides.

| Option | What it does |
|---|---|
| `--out <file>`, `-o <file>` | Writes the figures to that file instead of `msvba.json`. |
| `--rounds <n>` | Runs `n` rounds (5 by default). |
| `--excel-rounds <n>` | Runs the Excel automation cases in the first `n` rounds (3 by default; 0 runs none). |
| `--runs <n>`, `-r <n>` | Runs each case of the corpus `n` times a round (7 by default). |
| `--excel-runs <n>` | Runs each Excel automation case `n` times a round (5 by default). |

The file's `method` line says how its rounds were run. The site's MS-VBA figures come from `RDCore.CLI/Bench/Reference/msvba.json`.

## See also

- [Performance](../performance.md)
- [The shell and the rdc commands](../rdc.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
