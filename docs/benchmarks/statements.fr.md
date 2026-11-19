# Bancs d'essai : instructions
<sup>_This document is available in [English](statements.md)_</sup>

---
Les instructions autres que l'affectation `Let` d'une variable. Chaque cas exécute son instruction 100 000 fois.

La source est le module [`Statements.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/Statements.bas) du corpus intégré à `rdc`. Chaque badge donne le temps de RD-VBA en multiple de celui de MS-VBA ([comment les chiffres sont mesurés](../performance.fr.md#comment-les-chiffres-sont-mesurés)).

## Déclarations

Le module déclare un type défini par l'utilisateur :

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#Declarations)]

## Cas

### BenchSetAssignment

`Set D = C` d'une variable objet.
`Counter` est à la page [Appels](calls.fr.md#counter).

![Statements.BenchSetAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchSetAssignment.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchSetAssignment)]

### BenchMemberAssignment

`P.X = I`, une propriété d'un objet.
Il affecte `X` de [`Point`](#point).

![Statements.BenchMemberAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchMemberAssignment.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchMemberAssignment)]

### BenchRecordFieldAssignment

`R.X = I`, un champ d'un type défini par l'utilisateur.
`Pair` est dans les [déclarations](#déclarations) du module.

![Statements.BenchRecordFieldAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchRecordFieldAssignment.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchRecordFieldAssignment)]

### BenchSelectCase

`Select Case I Mod 4` à trois branches.

![Statements.BenchSelectCase](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchSelectCase.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchSelectCase)]

### BenchConditions

Les conditions de `If`, `ElseIf` et `Do While` : des comparaisons jointes par `And`.

![Statements.BenchConditions](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchConditions.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchConditions)]

### BenchMidStatement

L'instruction `Mid$` : `Mid$(S, 1, 1) = "r"`.

![Statements.BenchMidStatement](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchMidStatement.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchMidStatement)]

### BenchOnErrorResumeNext

Une boucle sous `On Error Resume Next`, qui divise par zéro une fois sur dix.

![Statements.BenchOnErrorResumeNext](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchOnErrorResumeNext.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Statements.bas#BenchOnErrorResumeNext)]

## Modules de classe

### Point

Une classe à deux variables publiques.

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Point.cls#Point)]

## Voir aussi

- [Bancs d'essai](index.fr.md)
- [Performance](../performance.fr.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
