# Bancs d'essai : calcul numérique
<sup>_This document is available in [English](numeric.md)_</sup>

---
De l'arithmétique dans une boucle de 1 000 000 de tours (`Iterations`), sur des opérandes typés et `Variant`.

La source est le module [`Numeric.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/Numeric.bas) du corpus intégré à `rdc`. Chaque badge donne le temps de RD-VBA en multiple de celui de MS-VBA ([comment les chiffres sont mesurés](../performance.fr.md#comment-les-chiffres-sont-mesurés)).

## Déclarations

Le module déclare :

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#Declarations)]

## Cas

### BenchLongCounterDoubleSum

`S = S + I`, un compteur `Long` et une somme `Double`.

![Numeric.BenchLongCounterDoubleSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchLongCounterDoubleSum.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#BenchLongCounterDoubleSum)]

### BenchDoubleSum

La même boucle sur des `Double`.

![Numeric.BenchDoubleSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchDoubleSum.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#BenchDoubleSum)]

### BenchLongArithmetic

De l'arithmétique sur des `Long`, gardée dans les bornes par `Mod`.

![Numeric.BenchLongArithmetic](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchLongArithmetic.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#BenchLongArithmetic)]

### BenchVariantSum

La même boucle, chaque opérande un `Variant`.

![Numeric.BenchVariantSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchVariantSum.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#BenchVariantSum)]

### BenchCurrencyAndDate

Une somme `Currency` et une `Date` avancée d'un millième de jour à chaque tour.

![Numeric.BenchCurrencyAndDate](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchCurrencyAndDate.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#BenchCurrencyAndDate)]

### BenchVariantTypedMix

Un `Long` et un `Double` ajoutés à une somme `Variant`.

![Numeric.BenchVariantTypedMix](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchVariantTypedMix.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Numeric.bas#BenchVariantTypedMix)]

## Voir aussi

- [Bancs d'essai](index.fr.md)
- [Performance](../performance.fr.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
