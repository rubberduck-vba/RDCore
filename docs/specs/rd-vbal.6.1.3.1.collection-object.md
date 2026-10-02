# 6.1.3.1 Collection Object

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.3.1** Collection Object](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/31ec9e63-f71e-4521-863b-a7d12007b7cc).

The `Collection` class is represented in the SDK by the interface
[IStdCollectionClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdCollectionClass.html).

A `For Each` statement finds an object's enumeration member through
[VBReturningMemberSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.VBReturningMemberSymbol.html) /
[SymbolProperties](../api/RDCore.SDK.Model.Symbols.Abstract.SymbolProperties.html)`.UserMemId` /
[WellKnownDispIds](../api/RDCore.SDK.Model.WellKnownDispIds.html)`.NewEnum`. This is the same lookup the constructor of
[VBCollectionType](../api/RDCore.SDK.Model.Types.Complex.VBCollectionType.html) uses
([**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md)).

## Instances

A collection is one of many: each `New Collection` is an object of its own, with the members it was given. The implementation is one object for the whole class, and the
standard-library dispatcher tells it which instance a call is on (`IStdLibReceiverBound`) for as long as that call lasts; what is in a collection is kept with the
instance, so it lasts exactly as long as the object does.

`Item` is the default member of the class (`VB_UserMemId = 0`), so `c(1)` is `c.Item(1)`: an index expression whose callee is an object is a call of its class's default member
([**RD-VBAL §5.6.13** Index Expressions](rd-vbal.5.6.13.index-expressions.md)), a class of the workspace's or the library's alike.

An object is a member as itself: it is passed to a `Variant` parameter by `Set`-assignment, and the parameter holds the object, not what the object's default member
returns ([**RD-VBAL §5.3.1.11**](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).

> [!NOTE]
> **Not implemented.** A collection does not count the objects it holds. An object that only a collection holds is not kept alive by it, and one that was held by it is
> not let go of when it is removed; the same gap [`ObjectReferences`](../api/RDCore.Runtime.Execution.ObjectReferences.html) documents for an array element and a field.

## Keys, Positions and Errors

A key is a `String`, and is the same key whatever the case of its letters. A member is reached by its position (from 1 to `Count`) or by its key.

|Situation|Runtime error|
|---|---|
|A position that is not in the collection (`Item`, `Remove`, `Before`, `After`).|9, `Subscript out of range`.|
|A key that no member has.|5, `Invalid procedure call or argument`.|
|A key that a member already has (`Add`).|457, `This key is already associated with an element of this collection`. Nothing is added.|
|Both `Before` and `After` given (`Add`).|5.|
|A key that is not a `String`, an index that is neither a number nor a `String`, a user-defined type as a member.|13, `Type mismatch`.|

## Enumeration

The collection's enumeration member is the hidden `_NewEnum` (`VB_UserMemId = -4`), which VBA source reaches as `[_NewEnum]`. It returns an object of the hidden
`IEnumVARIANT` class ([IStdEnumVariantClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdEnumVariantClass.html)): a position in a snapshot of the members
the collection had when it was asked, which `MoveNext`, `Current` and `Reset` move and read. A member added or removed afterwards is not that enumerator's.

MS-VBAL leaves the enumeration of an object "implementation-defined"; in MS-VBA the enumerator is a COM `IEnumVARIANT`, whose `Next` fills an array that VBA source cannot
call. This is the same enumerator in a shape source can call, and is what a class of the workspace answers its own `_NewEnum` with, as `mItems.[_NewEnum]`.


## 6.1.3.1.1 Public Functions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.3.1.1** Public Functions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3f2c31e1-524e-415c-823c-dc9d51bf9b7e).

|§|Member|Notes|
|---|---|---|
|6.1.3.1.1.1|[Count](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6cfa9558-1337-4871-be64-1dcef32fde8d)||
|6.1.3.1.1.2|[Item](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2cc7e3b8-a9b6-4f1e-9b42-960e67c45abe)||


## 6.1.3.1.2 Public Subroutines

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.3.1.2** Public Subroutines](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/8f97f8a0-e0a8-430a-8601-330d589b8e3b).

|§|Member|Notes|
|---|---|---|
|6.1.3.1.2.1|[Add](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/07e970d8-0fb5-461c-bb6f-7960e8c45699)||
|6.1.3.1.2.2|[Remove](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/93d5dcdd-91a5-4fae-9ba3-4472c55faf69)||

---
> ⏮️ [**RD-VBAL §6.1.3** Predefined Class Modules](rd-vbal.6.1.3.predefined-class-modules.md) | ⏭️ [**RD-VBAL §6.1.3.2** Err Class](rd-vbal.6.1.3.2.err-class.md)
