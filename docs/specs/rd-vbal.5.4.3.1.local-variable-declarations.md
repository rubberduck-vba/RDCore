# 5.4.3.1 Local Variable Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.3.1** Local Variable Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7e93afc7-de6f-4c25-a139-164e92271d00).

## Syntax

|Declaration|AST node|Symbol on the procedure's `Locals`|
|---|---|---|
|`Dim`|[VariableDeclarationNode](../api/RDCore.SDK.Model.AST.Declarations.VariableDeclarationNode.html)|[VBLocalVariableSymbol](../api/RDCore.SDK.Model.Symbols.VBLocalVariableSymbol.html)|
|`Static`|`VariableDeclarationNode` (`IsStatic`)|`VBLocalVariableSymbol` (`IsStatic`)|

A local `Const` is listed on `Locals` too, as a
[VBLocalConstantSymbol](../api/RDCore.SDK.Model.Symbols.VBLocalConstantSymbol.html); see
[**RD-VBAL §5.4.3.2** Local Constant Declarations](rd-vbal.5.4.3.2.local-constant-declarations.md).

## Static Semantics

`Dim` and `Static` locals are hoisted:
[VBProcedureMemberSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBProcedureMemberSymbol.html)`.Locals` and
[VBReturningMemberSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.VBReturningMemberSymbol.html)`.Locals` carry
them on the procedure symbol, as `Parameters` are carried.

- `VBProcedureMemberSymbol.Locals` and `VBReturningMemberSymbol.Locals` list every `Dim`, `Static` and `Const`
  declared in the procedure body.
- The `Locals` property of a procedure member symbol mirrors its `Parameters` property.
- [ScopeTreeBuilder](../api/RDCore.SDK.Model.Symbols.ScopeTreeBuilder.html) extracts a procedure symbol's `Locals`
  the same way it extracts its `Parameters`.

By design, a procedure's parameters and its own `Dim` / `Static` / `Const` locals are carried on the member
symbol, not registered as separate entries. A local therefore needs no second, flat registration of its own to
resolve by name. See
[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).

### Array declarations

The declaration pass binds the declared type of an array symbol from the array-dim clause alone
([**MS-VBAL §5.2.3.1.3** Array Dimensions and Bounds](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7c97ad30-4b76-45d9-9827-7455f98a5503)),
before any bound is evaluated:

|Array-dim clause|Example|Declared type|
|---|---|---|
|One or more bounds|`Dim g(1 To 3, 0 To 4) As Long`|[VBFixedSizeArrayType](../api/RDCore.SDK.Model.Types.VBFixedSizeArrayType.html)|
|An empty `()` clause|`Dim b() As Long`|[VBResizableArrayType](../api/RDCore.SDK.Model.Types.VBResizableArrayType.html)|
|A trailing `()` on the `As` clause|`Dim b As Long()`|`VBResizableArrayType`|
|An empty `()` clause or a trailing `()` on the `As` clause, with item type `Byte`|—|[VBResizableByteArrayType](../api/RDCore.SDK.Model.Types.VBResizableByteArrayType.html), instead of `VBResizableArrayType`|

See [**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md). An array declaration creates an array
value of the appropriate array type in the scope of the declaration; see
[**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md).

## Runtime Semantics

Hoisting of procedure-local variables is governed by
[**MS-VBAL §5.4.3** Data Manipulation Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ee62ca0d-bf15-4679-8d11-6e411b37901b)
([**RD-VBAL §5.4.3** Data Manipulation Statements](rd-vbal.5.4.3.data-manipulation-statements.md)). Step 4 of
MS-VBAL's procedure invocation reads: "create the function result variable and any procedure extent local variables
declared within the procedure". RD-VBA implements step 4 for `Dim` and `Static` locals as well as for the function
result variable.

When a procedure is invoked
([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)):

1. The procedure's parameters are bound.
2. `RuntimeProcedureInvoker.HoistLocals` hoists the procedure's `Dim` and `Static` locals: it walks the procedure's
   `Locals` immediately after parameter binding, before the body runs.
3. Each `Dim` local gets a fresh `CallStackFrame.Push`, seeded to its declared type's default value.
4. Each `Static` local that has no allocated storage gets it from
   [ISymbolResolver](../api/RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver.html)`.TryAllocate` (see
   [Static locals](#static-locals)).
5. The procedure body runs.

|Local|Extent|Storage|On each call|When the call returns|
|---|---|---|---|---|
|`Dim`|Procedure extent.|A fresh `CallStackFrame.Push` on every call.|Seeded to its declared type's default value on every call. A `Dim` local never sees a previous call's value.|Freed with the rest of the frame.|
|`Static`|Module extent: it needs storage that outlives the call.|Reserved once by `ISymbolResolver.TryAllocate`, in the session's module-level heap.|Sees whatever the previous call's body last wrote to it.|Kept.|

### Static locals

`ISymbolResolver.TryAllocate` allocates a `Static` local's own module-extent storage: it reserves the storage in
the session's module-level heap. A `Static` local's storage lives in the same heap tier a module field uses; see
[**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md).

- The one-time `TryAllocate` for a `Static` local is guarded by `TryGetAddress`, so only the first call allocates.
- Every later call to the procedure sees whatever the previous call's body last wrote to a `Static` local.
- By design, a `Static` local's storage is not reserved through
  [ISessionSymbols](../api/RDCore.SDK.Runtime.Abstract.Execution.ISessionSymbols.html)`.TryDefine`.
  `ISessionSymbols.TryDefine`'s bucket-add would register a `Static` local's symbol a second time, since the
  procedure's `Locals` already carries the name. A second registration would risk an ambiguous name, not only
  duplicate work.

Reading a `Static` local needs no dedicated mechanism. `CallStackAwareSymbolResolver` falls through to
session-level storage for any `Local`-scoped symbol that the current frame does not itself declare. A name that
refers to a symbol defined in the static locals heap resolves to a symbol that is locally scoped but preserves its
value between calls ([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).

The one-time guarded `TryAllocate`, with `CallStackAwareSymbolResolver`'s fall-through for reads, is the entire
mechanism for `Static` locals.

A local variable also has module extent when its procedure is declared `Static`
([**RD-VBAL §5.3.1.2** Static Procedures](rd-vbal.5.3.1.2.static-procedures.md)): it is marked `Static` as if the keyword were written.

## Implementation

|Type or member|Role|
|---|---|
|`VBProcedureMemberSymbol.Locals`, `VBReturningMemberSymbol.Locals`|The procedure's `Dim`, `Static` and `Const` locals, carried on the procedure symbol (**RDCore.SDK**).|
|`ScopeTreeBuilder`|Extracts `Locals` for name resolution, the same way as `Parameters` (**RDCore.SDK**).|
|`RDCore.Runtime.Execution.RuntimeProcedureInvoker.HoistLocals`|Hoists the `Dim` and `Static` locals when the procedure is invoked (**RDCore.Runtime**).|
|`CallStackFrame.Push`|Binds a `Dim` local on the call's frame.|
|`ISymbolResolver.TryAllocate`|Allocates a `Static` local's module-extent storage.|
|`ISymbolResolver.TryGetAddress`|Guards the one-time `TryAllocate`.|
|`CallStackAwareSymbolResolver`|Falls through to session-level storage when reading a `Static` local.|

---
> ⏮️ [**RD-VBAL §5.4.3** Data Manipulation Statements](rd-vbal.5.4.3.data-manipulation-statements.md) | ⏭️ [**RD-VBAL §5.4.3.2** Local Constant Declarations](rd-vbal.5.4.3.2.local-constant-declarations.md)
