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

The target name of a `ReDim` statement "is first matched as a simple name expression" (**MS-VBAL §5.4.3.3**), and
what it matches decides what the statement is. A match is a variable or a constant - of the procedure, of the module,
of another module, of a library - a member of an enumeration, or the result of the function the `ReDim` is in. A
procedure, a function of a library, a module or a project is no match, as **MS-VBA** has it (recorded in Excel):

|Target name|Example|The `ReDim` statement is|
|---|---|---|
|A resizable array or a `Variant`, of the procedure, the module or another module, bare or qualified|`ReDim Items(3)` of another module's `Public Items() As Long`|A re-dimension of that array.|
|A fixed-size array|`ReDim Grid(3)` of `Public Grid(2) As Long`|[VBC09357](../diagnostics/vbc09357.md) _Array already dimensioned_.|
|A variable of another type, a constant, a member of an enumeration, the function's own result|`ReDim Count(1)` of a `Long`, `ReDim vbCrLf(1)`, `ReDim Red(1)`|[VBC09307](../diagnostics/vbc09307.md) _Expected array_.|
|Nothing that matches: no name, a procedure, a function of a library, a module|`ReDim Totals(12)`, `ReDim Left(1)`|An implicit declaration of a new array, which hides what the name otherwise is.|
|A name with no declaration that the procedure uses before the `ReDim`: written, read, or on a line that never runs|`y = 5`, then `ReDim y(2)`|[VBC09306](../diagnostics/vbc09306.md) _Invalid ReDim_: the earlier use declared `y` implicitly, as a `Variant`.|

**MS-VBA** decides between the last two rows by the text of the procedure, not by what runs: a name used anywhere before
the first `ReDim` of it is a variable declared implicitly there, which no `ReDim` re-dimensions; a name used only after
it is the array the `ReDim` declared (recorded in Excel, `oracle\findings-labels` R01-R10).

For an implicit `ReDim` declaration:

- The declaration pass introduces a procedure-local
  [VBResizableArrayType](../api/RDCore.SDK.Model.Types.VBResizableArrayType.html) symbol.
- The procedure-local symbol is marked as `ReDim`-introduced.
- The implicit declaration is legal under `Option Explicit`; see
  [**RD-VBAL §5.2.1** Option Directives](rd-vbal.5.2.1.option-directives.md).
- A later analysis pass raises a semantic flag at the site of the implicit declaration; see
  [**RD-VBAL §1.1.3** Core Semantic Flags](rd-vbal.1.1.3.core-semantic-flags.md). The declaration says so
  (`DeclarationFact.DeclaredBy` is `ReDim`), and [RDC00114](../diagnostics/rdc00114.md) _Implicit array declaration_
  suggests a `Dim` before it.
- The declaration pass composes the workspace twice. In the second pass, the local the first pass declared for the
  `ReDim` is the first thing its name resolves to; what else it matches is looked up from the module, past the
  procedure's locals, so that a `ReDim` of another module's array is a re-dimension of it.

### The `As` clause

A `ReDim` that re-dimensions a declared resizable array may restate its element type in an `As` clause. The type it
states must be the element type the array is declared with, `As New` included: a `ReDim … As New` of an array that is
not declared `As New`, or a plain `As` of one that is, changes the type of its elements too. A `ReDim` that changes
the element type is compile error [`VBC09369`](../diagnostics/vbc09369.md) _Can't change data types of array
elements_, as it is in **MS-VBA**:

|Declaration|`ReDim`|Outcome|
|---|---|---|
|`Dim a() As Long`|`ReDim a(1) As Long`|Valid.|
|`Dim a() As Long`|`ReDim a(1) As String`|`VBC09369`.|
|`Dim a() As Collection`|`ReDim a(1) As New Collection`|`VBC09369`.|
|`Dim a() As New Collection`|`ReDim a(1) As Collection`|`VBC09369`.|
|`Dim a() As Collection`|`ReDim a(1) As Collection`|Valid.|
|`Dim a() As New Collection`|`ReDim a(1) As New Collection`|Valid.|

> [!IMPORTANT]
> **MS-VBAL §5.4.3.3** says the `As` clause of a `ReDim` MUST NOT be an `As New` clause. **MS-VBA** compiles
> `ReDim … As New` of an array declared `As New` of the same class, and runs it (recorded in Excel); so does
> **RD-VBA**. Its elements are automatic instantiation variables, as the declaration's are
> ([**RD-VBAL §5.2.3** Module Declarations](rd-vbal.5.2.3.module-declarations.md), §5.2.3.1.4).

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
|A `Variant` holding anything else|Becomes an array: a number, a `String`, an object or `Null`, and the `Empty` a `Variant` starts as.|
|A `Variant` holding anything but an array, with `Preserve`|Runtime error 13, `Type mismatch`, the `Empty` a `Variant` starts as included. The `Variant` keeps what it held.|

**MS-VBAL §5.4.3.3** says "Runtime Error 13 is raised if the declared type of a redimensioned variable is `Variant` and its value type is not an array". RD-VBA
reads the sentence as **MS-VBA** does (recorded in Excel): as a `ReDim Preserve`'s, the one that has the elements of an array to keep. A `ReDim` without
`Preserve` keeps nothing of what the `Variant` held, and makes it an array whatever that was.

Each bound is Let-coerced to `Long`, as any other subscript is ([**RD-VBAL §5.6.13**](rd-vbal.5.6.13.index-expressions.md#subscripts)). An upper bound below its
lower bound is runtime error 9, `Subscript out of range`.

Without `Preserve`, every element of the re-dimensioned array is its element type's default value.

### Preserve

`Preserve` keeps the elements that still fit. It may change the upper bound of the last dimension, and nothing
else:

|Change|Result|
|---|---|
|Any, of an array that was never dimensioned|Allowed: there is nothing to preserve, and the array is dimensioned, as by a `ReDim` without `Preserve` (**MS-VBA**, recorded in Excel).|
|The upper bound of the last dimension|Allowed.|
|The upper bound of any other dimension|Runtime error 9, `Subscript out of range`.|
|The lower bound of any dimension|Runtime error 9, `Subscript out of range`.|
|The number of dimensions|Runtime error 9, `Subscript out of range`.|

An element the resized array has and the original did not is its element type's default value. An element at an
index now outside the array's bounds is discarded.

A `ReDim` of a variable that is currently locked is runtime error 10, `This array is fixed or temporarily locked` (**MS-VBAL §5.4.3.3**: "locked by a ByRef
formal parameter"). An array is locked, as MS-VBA has it, while an activation on the call stack has an element of it as a `ByRef` parameter (`Hold arr(1)`), or
while a `For Each` over it runs; the lock is gone once the call returns, or the loop is over
([ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.LocksArray`). A locked array is not erased either, nor assigned another
array (`arr = other`): error 10 too.

A fixed-size array can't be re-dimensioned either. A `ReDim` of one is a compile error where it is declared, but a
resizable array parameter can be bound to one ([**RD-VBAL §5.3.1.11**](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)):
a `ReDim` of that parameter, with `Preserve` or without, is runtime error 10, and the array keeps its bounds.

> [!IMPORTANT]
> The whole array passed by reference does **not** lock it, which is what MS-VBA does (recorded in Excel) and not how §5.4.3.3 reads: the parameter is another
> name for the variable, a `ReDim` of either is a `ReDim` of both, and each sees the dimensions the other gives it. RD-VBA does what MS-VBA does. The new array is
> written where the variable is, which is what keeps the parameter's name for it.

A target that is a member access - the field of a record (`r.Buffer`, `.Buffer` in a `With r` block), a variable of a module named through it
(`Module1.Buffer`) - is an expression: the array is read from it, and the new array is written back through it, as an assignment to it is. The element type
of an array that has no dimensions yet is the one the member is declared with.

A member of an object is not re-dimensioned, as MS-VBA has it (recorded in Excel), whatever the member is: of an object whose class is declared, it is the
compile error [`VBC09316`](../diagnostics/vbc09316.md) _Method or data member not found_, and of an `Object` [`VBC09306`](../diagnostics/vbc09306.md) _Invalid
ReDim_, in a `With` block too. A member of a `Variant` compiles; the member is read as a value when the statement runs, which is what is re-dimensioned, and the
member is as it was afterwards - a `Variant` that holds no object is error 424.

> [!NOTE]
> 🧩 Nothing in the runtime prevents re-dimensioning an object's array through the object, by writing it back through the member. RD-VBA does what MS-VBA
> does, so that a compile error is where it is expected; a language-level extension could allow it in the future.

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
