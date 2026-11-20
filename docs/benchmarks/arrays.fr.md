# Bancs d'essai : tableaux
<sup>_This document is available in [English](arrays.md)_</sup>

---
Chaque élément d'un tableau écrit, puis lu. Chaque tableau a 30 000 éléments (`Size`) : 300 × 100 pour les tableaux à deux dimensions.

La source est le module [`Arrays.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/Arrays.bas) du corpus intégré à `rdc`. Chaque badge donne le temps de RD-VBA en multiple de celui de MS-VBA ([comment les chiffres sont mesurés](../performance.fr.md#comment-les-chiffres-sont-mesurés)).

## Déclarations

Le module déclare :

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Arrays.bas#Declarations)]

## Cas

### BenchArrayFillAndSum

Écrire, puis lire, chaque élément d'un tableau de `Long`.

![Arrays.BenchArrayFillAndSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Arrays.BenchArrayFillAndSum.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Arrays.bas#BenchArrayFillAndSum)]

### BenchVariantArray2D

Un tableau `Variant` à deux dimensions, chaque élément doublé.

![Arrays.BenchVariantArray2D](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Arrays.BenchVariantArray2D.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Arrays.bas#BenchVariantArray2D)]

### BenchVariantHeldArray2D

La même chose, le tableau contenu dans un `Variant`, comme le donne `V = Range(...).Value`.

![Arrays.BenchVariantHeldArray2D](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Arrays.BenchVariantHeldArray2D.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Arrays.bas#BenchVariantHeldArray2D)]

## Voir aussi

- [Bancs d'essai](index.fr.md)
- [Performance](../performance.fr.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
