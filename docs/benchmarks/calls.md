# Benchmarks: calls
<sup>_Ce document est disponible en [français](calls.fr.md)_</sup>

---
Calls to a procedure of the same module, to the members of an object, and to the standard library. Each case but `BenchRecursion` makes its calls in a loop that runs 100,000 times. The class modules the cases call are at the end of this page.

The source is the module [`Calls.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Corpus/Calls.bas) of the corpus built into `rdc`. Each badge gives RD-VBA's time as a multiple of MS-VBA's ([how the figures are measured](../performance.md#how-the-figures-are-measured)).

## Cases

### BenchRecursion

Recursion: `Fib(25)`, 242,785 short calls.

![Calls.BenchRecursion](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchRecursion.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchRecursion)]

### BenchProcedureCalls

A function of the same module, called in a loop.

![Calls.BenchProcedureCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchProcedureCalls.json)

`Add`, the function it calls:

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#Add)]

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchProcedureCalls)]

### BenchByRefCalls

A variable passed `ByRef` and assigned through the parameter.

![Calls.BenchByRefCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchByRefCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchByRefCalls)]

### BenchClassMemberCalls

A member of a class, called through a variable of the class.
It calls [`Counter`](#counter).

![Calls.BenchClassMemberCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchClassMemberCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchClassMemberCalls)]

### BenchInterfaceMemberCalls

The same member, called through an interface.
It calls [`InterfaceCounter`](#interfacecounter) through [`ICounter`](#icounter).

![Calls.BenchInterfaceMemberCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchInterfaceMemberCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchInterfaceMemberCalls)]

### BenchStandardLibraryCalls

`N = Len(CStr(I)) + CLng(I)` in a loop.

![Calls.BenchStandardLibraryCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchStandardLibraryCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchStandardLibraryCalls)]

### BenchFactoryCalls

A factory method of a default instance, a new object on every call.
It calls [`Gadget`](#gadget).

![Calls.BenchFactoryCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchFactoryCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchFactoryCalls)]

### BenchDefaultInstanceCalls

A member called on a default instance.
It calls [`Gadget`](#gadget).

![Calls.BenchDefaultInstanceCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchDefaultInstanceCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchDefaultInstanceCalls)]

### BenchSiblingCalls

A member that calls a private member of its own class by its bare name.
It calls [`Gadget`](#gadget).

![Calls.BenchSiblingCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchSiblingCalls.json)

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Calls.bas#BenchSiblingCalls)]

## Class modules

### Counter

A class with a field, a method and a property.

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Counter.cls#Counter)]

### ICounter

An interface: `InterfaceCounter` implements it.

[!code-vb[](../../RDCore.CLI/Bench/Corpus/ICounter.cls#ICounter)]

### InterfaceCounter

`Counter`, written as the implementation of `ICounter`.

[!code-vb[](../../RDCore.CLI/Bench/Corpus/InterfaceCounter.cls#InterfaceCounter)]

### Gadget

A class with a default instance (`Attribute VB_PredeclaredId = True`) and a factory method.

[!code-vb[](../../RDCore.CLI/Bench/Corpus/Gadget.cls#Gadget)]

## See also

- [Benchmarks](index.md)
- [Performance](../performance.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
