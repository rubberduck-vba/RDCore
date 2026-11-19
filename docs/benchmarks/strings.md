# Benchmarks: strings
<sup>_Ce document est disponible en [français](strings.fr.md)_</sup>

---
String assignment, concatenation and comparison, and strings given to a procedure. How many times each loop runs is in its source.

The source is the module [`Strings.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/Strings.bas) of the corpus built into `rdc`. Each badge gives RD-VBA's time as a multiple of MS-VBA's ([how the figures are measured](../performance.md#how-the-figures-are-measured)).

## Cases

### BenchStringAssignment

A `String` assigned in a loop.

![Strings.BenchStringAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringAssignment.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Strings.bas#BenchStringAssignment)]

### BenchStringConcatenation

`S = S & "x"`, a string growing by one character at a time.

![Strings.BenchStringConcatenation](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringConcatenation.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Strings.bas#BenchStringConcatenation)]

### BenchStringComparison

Short strings joined and compared.

![Strings.BenchStringComparison](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringComparison.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Strings.bas#BenchStringComparison)]

### BenchStringBuilding

A line of numbers written as text, with a separator between each.

![Strings.BenchStringBuilding](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringBuilding.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Strings.bas#BenchStringBuilding)]

### BenchStringArguments

Strings passed to a procedure, by value and by reference.

![Strings.BenchStringArguments](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringArguments.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Strings.bas#BenchStringArguments)]

## See also

- [Benchmarks](index.md)
- [Performance](../performance.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
