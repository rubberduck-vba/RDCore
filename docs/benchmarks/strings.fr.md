# Bancs d'essai : chaînes
<sup>_This document is available in [English](strings.md)_</sup>

---
L'affectation, la concaténation et la comparaison de chaînes, et des chaînes passées à une procédure. Le nombre de tours de chaque boucle est dans sa source.

La source est le module [`Strings.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/Strings.bas) du corpus intégré à `rdc`. Chaque badge donne le temps de RD-VBA en multiple de celui de MS-VBA ([comment les chiffres sont mesurés](../performance.fr.md#comment-les-chiffres-sont-mesurés)).

## Cas

### BenchStringAssignment

Une `String` affectée dans une boucle.

![Strings.BenchStringAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringAssignment.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Strings.bas#BenchStringAssignment)]

### BenchStringConcatenation

`S = S & "x"`, une chaîne qui grandit d'un caractère à la fois.

![Strings.BenchStringConcatenation](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringConcatenation.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Strings.bas#BenchStringConcatenation)]

### BenchStringComparison

Des chaînes courtes jointes et comparées.

![Strings.BenchStringComparison](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringComparison.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Strings.bas#BenchStringComparison)]

### BenchStringBuilding

Une ligne de nombres écrits en texte, un séparateur entre chacun.

![Strings.BenchStringBuilding](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringBuilding.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Strings.bas#BenchStringBuilding)]

### BenchStringArguments

Des chaînes passées à une procédure, par valeur et par référence.

![Strings.BenchStringArguments](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringArguments.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Strings.bas#BenchStringArguments)]

## Voir aussi

- [Bancs d'essai](index.fr.md)
- [Performance](../performance.fr.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
