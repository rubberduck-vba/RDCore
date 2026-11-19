# Bancs d'essai : variables et paramètres
<sup>_This document is available in [English](variables-and-parameters.md)_</sup>

---
Là où une procédure garde ses valeurs : ses paramètres et ses variables locales, une variable de son module, une variable locale `Static`, et des valeurs `Currency` et `Boolean`. Chaque boucle fait 100 000 tours (`Iterations`).

La source est le module [`Slots.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/Slots.bas) du corpus intégré à `rdc`. Chaque badge donne le temps de RD-VBA en multiple de celui de MS-VBA ([comment les chiffres sont mesurés](../performance.fr.md#comment-les-chiffres-sont-mesurés)).

## Déclarations

Le module déclare :

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#Declarations)]

## Cas

### BenchWideActivations

Un appel à six paramètres typés et quatre variables locales.

![Slots.BenchWideActivations](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchWideActivations.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#BenchWideActivations)]

### BenchModuleVariableLoop

Une variable de module, lue et écrite à chaque tour.

![Slots.BenchModuleVariableLoop](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchModuleVariableLoop.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#BenchModuleVariableLoop)]

### BenchLocalVariableLoop

La même boucle sur une variable locale.

![Slots.BenchLocalVariableLoop](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchLocalVariableLoop.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#BenchLocalVariableLoop)]

### BenchStaticVariableLoop

La même boucle sur une variable locale `Static`.

![Slots.BenchStaticVariableLoop](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchStaticVariableLoop.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#BenchStaticVariableLoop)]

### BenchCurrencySum

Une somme `Currency`.

![Slots.BenchCurrencySum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchCurrencySum.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#BenchCurrencySum)]

### BenchBooleanFlag

Un indicateur `Boolean` inversé et testé à chaque tour.

![Slots.BenchBooleanFlag](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchBooleanFlag.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Slots.bas#BenchBooleanFlag)]

## Voir aussi

- [Bancs d'essai](index.fr.md)
- [Performance](../performance.fr.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
