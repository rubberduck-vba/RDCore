# Benchmarks: statements
<sup>_Ce document est disponible en [français](statements.fr.md)_</sup>

---
The statements other than the `Let` assignment of a variable. Each case runs its statement 100,000 times.

The source is the module [`Statements.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/Statements.bas) of the corpus built into `rdc`. Each badge gives RD-VBA's time as a multiple of MS-VBA's ([how the figures are measured](../performance.md#how-the-figures-are-measured)).

## Declarations

The module declares a user-defined type:

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#Declarations)]

## Cases

### BenchSetAssignment

`Set D = C` of an object variable.
`Counter` is on the [Calls](calls.md#counter) page.

![Statements.BenchSetAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchSetAssignment.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchSetAssignment)]

### BenchMemberAssignment

`P.X = I`, a property of an object.
It assigns `X` of [`Point`](#point).

![Statements.BenchMemberAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchMemberAssignment.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchMemberAssignment)]

### BenchRecordFieldAssignment

`R.X = I`, a field of a user-defined type.
`Pair` is in the [declarations](#declarations) of the module.

![Statements.BenchRecordFieldAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchRecordFieldAssignment.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchRecordFieldAssignment)]

### BenchSelectCase

`Select Case I Mod 4` with three branches.

![Statements.BenchSelectCase](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchSelectCase.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchSelectCase)]

### BenchConditions

`If`, `ElseIf` and `Do While` conditions: comparisons joined with `And`.

![Statements.BenchConditions](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchConditions.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchConditions)]

### BenchMidStatement

The `Mid$` statement: `Mid$(S, 1, 1) = "r"`.

![Statements.BenchMidStatement](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchMidStatement.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchMidStatement)]

### BenchOnErrorResumeNext

A loop under `On Error Resume Next`, dividing by zero one time in ten.

![Statements.BenchOnErrorResumeNext](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchOnErrorResumeNext.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchOnErrorResumeNext)]

## Class modules

### Point

A class with two public variables.

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Point.cls#Point)]

## See also

- [Benchmarks](index.md)
- [Performance](../performance.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
