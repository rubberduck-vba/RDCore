# 5.4.3.3 ReDim Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.3.3** ReDim Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/22b5d372-0a54-4617-9462-4934b5edc88c).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[RedimDeclarationNode](../api/RDCore.SDK.Model.AST.Declarations.RedimDeclarationNode.html)|`Simple`|One node per comma-separated `ReDim` target. The node is a declaration and a statement at once: it declares the name it may introduce, and it executes where it appears. Its `Target` is an expression: a `SimpleNameExpressionNode` (`a`), or a `MemberAccessExpressionNode` (`obj.Buffer`, `Me.Buffer`, `.Buffer`) with the owner as the expression it is.|
|[RedimBoundsNode](../api/RDCore.SDK.Model.AST.Declarations.RedimBoundsNode.html)|—|The dimension clause of one target, holding one [RedimDimensionNode](../api/RDCore.SDK.Model.AST.Declarations.RedimDimensionNode.html) per dimension.|

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md).

## Static Semantics

`ReDim` may be used to declare a `VBResizableArrayValue`, or to redimension an already-declared
`VBResizableArrayValue`. It is invalid to use `ReDim` with a `VBFixedSizeArrayValue`. See
[**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md).

The target name of a `ReDim` statement decides what the statement is:

|Target name|The `ReDim` statement is|
|---|---|
|Resolves to a local, a parameter, or a module field|A re-dimension of an existing array.|
|Unqualified, and resolves to nothing|An implicit declaration.|

For an implicit `ReDim` declaration:

- The declaration pass introduces a procedure-local
  [VBResizableArrayType](../api/RDCore.SDK.Model.Types.VBResizableArrayType.html) symbol.
- The procedure-local symbol is marked as `ReDim`-introduced.
- The implicit declaration is legal under `Option Explicit`; see
  [**RD-VBAL §5.2.1** Option Directives](rd-vbal.5.2.1.option-directives.md).
- A later analysis pass raises a semantic flag at the site of the implicit declaration; see
  [**RD-VBAL §1.1.3** Core Semantic Flags](rd-vbal.1.1.3.core-semantic-flags.md).

`ReDim` bounds are ordinary run-time expressions, not constant expressions. A `ReDim` bound is therefore an
expression node, and not the verbatim text a declared array bound keeps
([**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md)). `ReDim
Grid(1 To n)` is legal, and `n` is not knowable before the statement runs.

A dimension declared without a lower bound has no lower-bound node. The effective lower bound is the module's
`Option Base` ([**RD-VBAL §5.2.1** Option Directives](rd-vbal.5.2.1.option-directives.md)), which is a run-time
dial rather than something the statement wrote.

## Runtime Semantics

`ReDim` evaluates each bound where the statement stands, then re-allocates the array.

|Target|Effect|
|---|---|
|A resizable array|Re-dimensioned.|
|A `Variant` holding an array|Re-dimensioned. The variable keeps storing a `Variant`.|
|A `Variant` holding nothing yet|Becomes an array. The static rule admits a `Variant` target so that it can become an array, so the `Empty` a `Variant` starts as is not the error below.|
|A `Variant` holding anything else|Runtime error 13, `Type mismatch`.|

Each bound is Let-coerced to `Integer`, as any other subscript is. An upper bound below its lower bound is
runtime error 9, `Subscript out of range`.

Without `Preserve`, every element of the re-dimensioned array is its element type's default value.

### Preserve

`Preserve` keeps the elements that still fit. It may change the upper bound of the last dimension, and nothing
else:

|Change|Result|
|---|---|
|The upper bound of the last dimension|Allowed.|
|The upper bound of any other dimension|Runtime error 9, `Subscript out of range`.|
|The lower bound of any dimension|Runtime error 9, `Subscript out of range`.|
|The number of dimensions|Runtime error 9, `Subscript out of range`.|

An element the resized array has and the original did not is its element type's default value. An element at an
index now outside the array's bounds is discarded.

> [!NOTE]
> **Not implemented.** A `ReDim` whose target is currently aliased by a `ByRef` parameter does not raise runtime
> error 10, `This array is fixed or temporarily locked`. Nothing models that lock yet.

A target that is a member access (`obj.Buffer`, `.Buffer`) is an expression: the array is read from it, and the new array is written back through it, as an assignment to
it is - the public variable of the object, or the field of the record. The element type of an array that has no dimensions yet is the one the member is declared with.

## Implementation

|Type or member|Role|
|---|---|
|`RedimDeclarationNode`|One `ReDim` target (**RDCore.SDK**).|
|`RedimBoundsNode`, `RedimDimensionNode`|The dimension clause, as expression nodes (**RDCore.SDK**).|
|[VBLocalVariableSymbol](../api/RDCore.SDK.Model.Symbols.VBLocalVariableSymbol.html)`.DeclaredBy`|[LocalDeclarationKind](../api/RDCore.SDK.Model.Symbols.LocalDeclarationKind.html)`.ReDim` marks a local introduced by an implicit `ReDim` declaration.|
|[ModuleDirectives](../api/RDCore.SDK.Model.Symbols.ModuleDirectives.html)`.Base`|The module's `Option Base`, which an omitted lower bound takes (**RDCore.SDK**).|
|`RDCore.Runtime.Semantics.Statements.ArrayStatementRuntimeSemantics`|Evaluates the bounds and re-allocates the array; see [**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md).|

---
> ⏮️ [**RD-VBAL §5.4.3.2** Local Constant Declarations](rd-vbal.5.4.3.2.local-constant-declarations.md) | ⏭️ [**RD-VBAL §5.4.3.4** Erase Statement](rd-vbal.5.4.3.4.erase-statement.md)
