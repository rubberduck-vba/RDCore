# Bancs d'essai : appels
<sup>_This document is available in [English](calls.md)_</sup>

---
Des appels à une procédure du même module, aux membres d'un objet et à la bibliothèque standard. Chaque cas sauf `BenchRecursion` fait ses appels dans une boucle de 100 000 tours. Les modules de classe qu'appellent les cas sont à la fin de cette page.

La source est le module [`Calls.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/Calls.bas) du corpus intégré à `rdc`. Chaque badge donne le temps de RD-VBA en multiple de celui de MS-VBA ([comment les chiffres sont mesurés](../performance.fr.md#comment-les-chiffres-sont-mesurés)).

## Cas

### BenchRecursion

La récursion : `Fib(25)`, 242 785 appels courts.

![Calls.BenchRecursion](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchRecursion.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchRecursion)]

### BenchProcedureCalls

Une fonction du même module, appelée dans une boucle.

![Calls.BenchProcedureCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchProcedureCalls.json)

`Add`, la fonction qu'il appelle :

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#Add)]

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchProcedureCalls)]

### BenchByRefCalls

Une variable passée `ByRef` et affectée par le paramètre.

![Calls.BenchByRefCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchByRefCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchByRefCalls)]

### BenchClassMemberCalls

Un membre d'une classe, appelé par une variable de la classe.
Il appelle [`Counter`](#counter).

![Calls.BenchClassMemberCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchClassMemberCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchClassMemberCalls)]

### BenchInterfaceMemberCalls

Le même membre, appelé par une interface.
Il appelle [`InterfaceCounter`](#interfacecounter) par [`ICounter`](#icounter).

![Calls.BenchInterfaceMemberCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchInterfaceMemberCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchInterfaceMemberCalls)]

### BenchStandardLibraryCalls

`N = Len(CStr(I)) + CLng(I)` dans une boucle.

![Calls.BenchStandardLibraryCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchStandardLibraryCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchStandardLibraryCalls)]

### BenchFactoryCalls

Une méthode de fabrique d'une instance par défaut, un nouvel objet à chaque appel.
Il appelle [`Gadget`](#gadget).

![Calls.BenchFactoryCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchFactoryCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchFactoryCalls)]

### BenchDefaultInstanceCalls

Un membre appelé sur une instance par défaut.
Il appelle [`Gadget`](#gadget).

![Calls.BenchDefaultInstanceCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchDefaultInstanceCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchDefaultInstanceCalls)]

### BenchSiblingCalls

Un membre qui appelle un membre privé de sa propre classe par son seul nom.
Il appelle [`Gadget`](#gadget).

![Calls.BenchSiblingCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchSiblingCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchSiblingCalls)]

## Modules de classe

### Counter

Une classe à un champ, une méthode et une propriété.

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Counter.cls#Counter)]

### ICounter

Une interface : `InterfaceCounter` l'implémente.

[!code-vb[](../../RDCore.CLI/Bench/Corpus/ICounter.cls#ICounter)]

### InterfaceCounter

`Counter`, écrit comme l'implémentation de `ICounter`.

[!code-vb[](../../RDCore.CLI/Bench/Corpus/InterfaceCounter.cls#InterfaceCounter)]

### Gadget

Une classe à instance par défaut (`Attribute VB_PredeclaredId = True`) et à méthode de fabrique.

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Gadget.cls#Gadget)]

## Voir aussi

- [Bancs d'essai](index.fr.md)
- [Performance](../performance.fr.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
