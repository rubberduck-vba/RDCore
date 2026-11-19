# Benchmarks: variables and parameters
<sup>_Ce document est disponible en [français](variables-and-parameters.fr.md)_</sup>

---
Where a procedure keeps its values: its parameters and locals, a variable of its module, a `Static` local, and `Currency` and `Boolean` values. Each loop runs 100,000 times (`Iterations`).

The source is the module [`Slots.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/Slots.bas) of the corpus built into `rdc`. Each badge gives RD-VBA's time as a multiple of MS-VBA's ([how the figures are measured](../performance.md#how-the-figures-are-measured)).

## Declarations

The module declares:

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#Declarations)]

## Cases

### BenchWideActivations

A call with six typed parameters and four locals.

![Slots.BenchWideActivations](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchWideActivations.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#BenchWideActivations)]

### BenchModuleVariableLoop

A module variable, read and written each time round.

![Slots.BenchModuleVariableLoop](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchModuleVariableLoop.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#BenchModuleVariableLoop)]

### BenchLocalVariableLoop

The same loop over a local variable.

![Slots.BenchLocalVariableLoop](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchLocalVariableLoop.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#BenchLocalVariableLoop)]

### BenchStaticVariableLoop

The same loop over a `Static` local.

![Slots.BenchStaticVariableLoop](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchStaticVariableLoop.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#BenchStaticVariableLoop)]

### BenchCurrencySum

A `Currency` sum.

![Slots.BenchCurrencySum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchCurrencySum.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#BenchCurrencySum)]

### BenchBooleanFlag

A `Boolean` flag flipped and tested each time round.

![Slots.BenchBooleanFlag](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchBooleanFlag.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#BenchBooleanFlag)]

## See also

- [Benchmarks](index.md)
- [Performance](../performance.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
