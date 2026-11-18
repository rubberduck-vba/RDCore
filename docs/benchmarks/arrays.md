# Benchmarks: arrays
<sup>_Ce document est disponible en [français](arrays.fr.md)_</sup>

---
Every element of an array written, then read. Each array has 30,000 elements (`Size`): 300 × 100 for the 2-D arrays.

The source is the module [`Arrays.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/Arrays.bas) of the corpus built into `rdc`. Each badge gives RD-VBA's time as a multiple of MS-VBA's ([how the figures are measured](../performance.md#how-the-figures-are-measured)).

## Declarations

The module declares:

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Arrays.bas#Declarations)]

## Cases

### BenchArrayFillAndSum

Writing, then reading, every element of a `Long` array.

![Arrays.BenchArrayFillAndSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Arrays.BenchArrayFillAndSum.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Arrays.bas#BenchArrayFillAndSum)]

### BenchVariantArray2D

A 2-D `Variant` array, every element doubled.

![Arrays.BenchVariantArray2D](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Arrays.BenchVariantArray2D.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Arrays.bas#BenchVariantArray2D)]

### BenchVariantHeldArray2D

The same, with the array held in a `Variant`, as `V = Range(...).Value` gives it.

![Arrays.BenchVariantHeldArray2D](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Arrays.BenchVariantHeldArray2D.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Arrays.bas#BenchVariantHeldArray2D)]

## See also

- [Benchmarks](index.md)
- [Performance](../performance.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
