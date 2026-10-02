# 5.4.2.4 For Each Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.4** For Each Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b132463a-fd25-4143-8fc7-a443930e0651).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[ForEachStatementNode](../api/RDCore.SDK.Model.AST.Statements.ForEachStatementNode.html)|`ForEachOpener`|The `For Each` opener. Its `End` is the offset right past the `Next` closer.|
|— (synthesized)|`ForEachNext`|The `Next` closer; `Node` is `null`. Its `Target` is the body's first instruction; its `Matching` is the offset of its `ForEachOpener`.|

The resolved targets have the same shape as a `For` loop's
([**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md);
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

## Static Semantics

MS-VBAL requires the `For Each` collection to be an array or an enumeration-capable object reference.

A [VBCollectionType](../api/RDCore.SDK.Model.Types.Complex.VBCollectionType.html) can be efficiently iterated using
a `For Each...Next` loop. `For Each...Next` enumeration is intended to be used with collections containing objects
([**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md)).

## Runtime Semantics

`For Each` is implemented for arrays and for objects that expose an enumeration member.

A language-level condition in a well-formed program never reports `InternalError`; such a condition raises a
run-time error.

### ForEachOpener

1. Evaluate the collection expression once.
2. When the collection is a `Variant` holding an array, unwrap it: `For Each x In v` enumerates a `Variant` holding an
   array in the same way as a declared array.
3. When the collection is a [VBArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBArrayValue.html):
   1. If the array was never initialized (`VBArrayValue.IsInitialized` is false: a `Dim` with no bounds, never
      `ReDim`'d), raise error 92.
   2. If the array's declared bounds hold zero elements, skip the body entirely by branching to
      [Instruction](../api/RDCore.SDK.Semantics.Instructions.Instruction.html)`.End`, per **MS-VBAL §5.4.2.4**'s
      "if the array has no elements".
   3. Otherwise, assign the first element to the control variable, store a
      [ForEachState](../api/RDCore.SDK.Runtime.Shared.ForEachState.html), and fall through into the body. The
      assignment is a Set-assignment when the array's item type is `Object`, and a Let-assignment otherwise
      (**MS-VBAL §5.4.2.4**'s rule).
4. When the collection is `Nothing`, raise error 91: enumerating it would invoke `_NewEnum` on an unset reference.
5. When the collection is a live object whose class exposes a member with `VB_UserMemId = -4` (commonly `_NewEnum`),
   invoke that member and store a `ForEachState` holding the *enumerator* object it returns (see *Object enumeration*
   below), then ask the enumerator for its first member as an array's first element is taken, skipping the body when
   it has none. The member's declared type may be `IUnknown`, `Object` or the enumerator's own class.
6. When the collection is a live object with no such member, raise error 438.
7. When the collection is anything else (a scalar), raise error 13, `TypeMismatch`.

### ForEachNext

1. Read the `ForEachState` back via `Instruction.Matching`.
2. Advance the index (an array), or ask the enumerator for its next member (an object). When the array or the
   enumerator is exhausted, fall through past the loop.
3. Otherwise, assign the next element to the control variable, and branch back to the body's first instruction
   (`Instruction.Target`).

### Object enumeration

MS-VBAL leaves the enumeration of an object *implementation-defined*. RDCore follows the shape of COM's
`IEnumVARIANT`, reduced to the two members a VBA enumerator is written with:

|Member|Contract|
|---|---|
|`MoveNext() As Boolean`|Advances to the next member; `False` when there is none.|
|`Current As Variant`|The member the enumerator is at.|

The enumerator of a [Collection](rd-vbal.6.1.3.1.collection-object.md) (`IEnumVARIANT`) is a library class that has
them, and so is an enumerator written in VBA: a class whose `_NewEnum` (`VB_UserMemId = -4`) returns `Me`, or any other
object that has the two. Each loop has an enumerator of its own, so loops over the same object, nested or in turn, do
not disturb one another; a member added to a `Collection` while a loop runs is not the loop's.

A member is Set-assigned to the control variable when it is an object and the control variable can hold one, and
Let-assigned otherwise: a `Long` control is Let-coerced from a member that is a number.

`IUnknown` is a library class (`IStdUnknownClass`), the root interface: an object of any class is one in a `Set`
assignment, as it is an `Object`, which is what lets `Property Get NewEnum() As IUnknown` be written as it always has
been. The `VB_UserMemId` of a class module's member travels in the symbol descriptor (`SymbolDescriptor.UserMemId`), so
the host finds the enumeration member and the default member of a workspace class as it does those of the library's.

### Errors

|Condition|Run-time error|
|---|---|
|The collection is an array that was never initialized (`VBArrayValue.IsInitialized` is false).|92 — For loop not initialized|
|`ForEachNext` finds no stored `ForEachState` for its `Matching` offset (a `GoTo` landed directly on the closer).|92 — For loop not initialized|
|The collection is `Nothing`.|91 — Object variable or With block variable not set|
|The collection is a live object with no `VB_UserMemId = -4` member, or the enumerator it returns has no `MoveNext` and `Current`.|438 — Object doesn't support this property or method|
|The collection is anything else (a scalar).|13 — Type mismatch|

MS-VBAL does not separately call out an array that was never initialized. MS-VBA raises error 92 for it, because
the collection itself was never set up; a declared array with zero elements is the "no elements" case instead.

MS-VBAL requires the collection to be an array or an enumeration-capable object reference, so errors 91, 438 and 13
are the specified outcomes. The error identifiers are listed in
[**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md).

## 5.4.2.4.1 Array Enumeration Order

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.4.1** Array Enumeration Order](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ce63be3a-aa7a-4f5f-bcf8-a2d266bf7cfc).

The element block of an array value is stored and addressed in column-major order, which is the traversal order
**MS-VBAL §5.4.2.4.1** mandates: the first subscript varies fastest, matching an OLE `SAFEARRAY`
([**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)).

`VBArrayValue.ElementAt(flatIndex)` therefore walks the array in enumeration order directly, with no per-dimension
subscript arithmetic. The index a `ForEachState` holds is this flat index.

## Implementation

- `ProcedureExecutor.ExecuteForEachOpener` unwraps a
  [VBVariantValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html) before matching a wrapped array
  ([**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md), Let-coercion to Variant).
- `ForEachOpener` stores a `ForEachState` holding the control symbol and expression, the array, and a flat index.
  The state is read via [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.TryGetForEachState`, and
  written via `CallStackFrame.SetForEachState`.
- A `For Each` loop's state is an enumeration cursor, so it has its own type, `ForEachState`
  ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
- `ForEachNext` reads the state back via `Instruction.Matching`, the same field that `ForNext` and `Case` headers use.
- The enumeration-member lookup uses
  [VBReturningMemberSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.VBReturningMemberSymbol.html),
  [SymbolProperties](../api/RDCore.SDK.Model.Symbols.Abstract.SymbolProperties.html)`.UserMemId` and
  [WellKnownDispIds](../api/RDCore.SDK.Model.WellKnownDispIds.html)`.NewEnum`: the same lookup `VBCollectionType`'s
  constructor uses ([**RD-VBAL §6.1.3.1** Collection Object](rd-vbal.6.1.3.1.collection-object.md)).
- A plain array-typed variable read back as an expression
  ([SimpleNameExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.SimpleNameExpressionNode.html), the ordinary
  shape of `arr` in `For Each item In arr`) yields the array value stored in it:
  [VBArrayType](../api/RDCore.SDK.Model.Types.VBArrayType.html)`.CreateValue` unboxes the
  [VBRuntimeArrayValue](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeArrayValue.html) that `SymbolAddressTable`
  boxed for the variable, and returns the same instance with its cells intact
  ([**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)).

---
> ⏮️ [**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md) | ⏭️ [**RD-VBAL §5.4.2.5** Exit For Statement](rd-vbal.5.4.2.5.exit-for-statement.md)
