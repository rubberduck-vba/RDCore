# Benchmarks: numeric
<sup>_Ce document est disponible en [français](numeric.fr.md)_</sup>

---
Arithmetic in a loop of 1,000,000 iterations (`Iterations`), over typed and `Variant` operands.

The source is the module [`Numeric.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/Numeric.bas) of the corpus built into `rdc`. Each badge gives RD-VBA's time as a multiple of MS-VBA's ([how the figures are measured](../performance.md#how-the-figures-are-measured)).

## Declarations

The module declares:

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#Declarations)]

## Cases

### BenchLongCounterDoubleSum

`S = S + I` with a `Long` counter and a `Double` sum.

![Numeric.BenchLongCounterDoubleSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchLongCounterDoubleSum.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#BenchLongCounterDoubleSum)]

### BenchDoubleSum

The same loop over `Double` values.

![Numeric.BenchDoubleSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchDoubleSum.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#BenchDoubleSum)]

### BenchLongArithmetic

`Long` arithmetic, kept in range with `Mod`.

![Numeric.BenchLongArithmetic](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchLongArithmetic.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#BenchLongArithmetic)]

### BenchVariantSum

The same loop with every operand a `Variant`.

![Numeric.BenchVariantSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchVariantSum.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#BenchVariantSum)]

### BenchCurrencyAndDate

A `Currency` sum and a `Date` advanced a thousandth of a day each time.

![Numeric.BenchCurrencyAndDate](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchCurrencyAndDate.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#BenchCurrencyAndDate)]

### BenchVariantTypedMix

A `Long` and a `Double` added into a `Variant` sum.

![Numeric.BenchVariantTypedMix](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchVariantTypedMix.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#BenchVariantTypedMix)]

## See also

- [Benchmarks](index.md)
- [Performance](../performance.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
