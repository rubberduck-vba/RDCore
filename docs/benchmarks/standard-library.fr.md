# Bancs d'essai : bibliothèque standard
<sup>_This document is available in [English](standard-library.md)_</sup>

---
Chaque cas appelle une fonction 100 000 fois (`Iterations`), avec les arguments indiqués, et affecte ce qu'elle renvoie à une variable de son type.

La source est le module [`StdLib.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/StdLib.bas) du corpus intégré à `rdc`. Chaque badge donne le temps de RD-VBA en multiple de celui de MS-VBA ([comment les chiffres sont mesurés](../performance.fr.md#comment-les-chiffres-sont-mesurés)).

## Déclarations

Le module déclare :

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#Declarations)]

## Cas

### BenchCLng

`CLng(X)` d'un `Double`.

![StdLib.BenchCLng](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchCLng.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchCLng)]

### BenchCDbl

`CDbl(I)` d'un `Long`.

![StdLib.BenchCDbl](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchCDbl.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchCDbl)]

### BenchCStr

`CStr(I)` d'un `Long`.

![StdLib.BenchCStr](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchCStr.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchCStr)]

### BenchVal

`Val(S)` de `"123.5"`.

![StdLib.BenchVal](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchVal.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchVal)]

### BenchLen

`Len(S)` d'une `String`.

![StdLib.BenchLen](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchLen.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchLen)]

### BenchLeft

`Left$(S, 2)`.

![StdLib.BenchLeft](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchLeft.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchLeft)]

### BenchMidFunction

`Mid$(S, 4, 3)`.

![StdLib.BenchMidFunction](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchMidFunction.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchMidFunction)]

### BenchInStr

`InStr(S, "V")`.

![StdLib.BenchInStr](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchInStr.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchInStr)]

### BenchReplace

`Replace(S, "-", "_")`.

![StdLib.BenchReplace](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchReplace.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchReplace)]

### BenchTrim

`Trim$(S)`.

![StdLib.BenchTrim](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchTrim.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchTrim)]

### BenchUCase

`UCase$(S)`.

![StdLib.BenchUCase](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchUCase.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchUCase)]

### BenchStrComp

`StrComp(S, "rd-vba", vbTextCompare)`.

![StdLib.BenchStrComp](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchStrComp.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchStrComp)]

### BenchAsc

`Asc(S)`.

![StdLib.BenchAsc](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchAsc.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchAsc)]

### BenchChr

`Chr$(65 + I Mod 26)`.

![StdLib.BenchChr](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchChr.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchChr)]

### BenchFormat

`Format$(D, "0.00")`.

![StdLib.BenchFormat](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchFormat.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchFormat)]

### BenchIsNumeric

`IsNumeric(S)` de `"123.5"`.

![StdLib.BenchIsNumeric](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchIsNumeric.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchIsNumeric)]

### BenchIsEmpty

`IsEmpty(V)` d'un `Variant` `Empty`.

![StdLib.BenchIsEmpty](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchIsEmpty.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchIsEmpty)]

### BenchVarType

`VarType(V)` d'un `Variant` qui contient un `Double`.

![StdLib.BenchVarType](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchVarType.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchVarType)]

### BenchTypeName

`TypeName(V)` du même.

![StdLib.BenchTypeName](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchTypeName.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchTypeName)]

### BenchAbs

`Abs(-I)`.

![StdLib.BenchAbs](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchAbs.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchAbs)]

### BenchRound

`Round(D, 2)`.

![StdLib.BenchRound](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchRound.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchRound)]

### BenchSqr

`Sqr(I)`.

![StdLib.BenchSqr](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchSqr.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchSqr)]

### BenchInt

`Int(D)`.

![StdLib.BenchInt](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchInt.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchInt)]

### BenchDateAdd

`DateAdd("d", 1, T)`.

![StdLib.BenchDateAdd](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchDateAdd.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchDateAdd)]

### BenchYear

`Year(T)`.

![StdLib.BenchYear](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchYear.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchYear)]

### BenchNow

`Now`.

![StdLib.BenchNow](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchNow.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchNow)]

### BenchUBound

`UBound(A)` d'un tableau.

![StdLib.BenchUBound](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchUBound.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/StdLib.bas#BenchUBound)]

## Voir aussi

- [Bancs d'essai](index.fr.md)
- [Performance](../performance.fr.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
