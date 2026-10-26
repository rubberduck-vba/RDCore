# 5.6.10 Simple Name Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.10** Simple Name Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e3af3398-f090-40db-ade6-de3a93589c76).

## Syntax

|Expression|AST node|Binding context|Resolver call|
|---|---|---|---|
|A bare name|[SimpleNameExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.SimpleNameExpressionNode.html)|Default binding context|[ISymbolResolver](../api/RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver.html)`.ResolveValue`|

🎯 In **RDCore**, name lookups are an explicit evaluation step involving specific AST nodes such as
`SimpleNameExpressionNode`. A simple name expression calls `ISymbolResolver.ResolveValue`, which binds under the
_default binding context_. See [**RD-VBAL §3.0.3** Binding Contexts](rd-vbal.3.0.3.binding-contexts.md).

## Static Semantics

The declared type of a simple name expression is the declared type of the entity its identifier resolves to. The
identifier resolves per the ordered lookup of [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).

This section specifies how a simple name expression's declared type is determined from a `ResolveValue` outcome.
[SimpleNameExpressionStaticSemantics](../api/RDCore.SDK.Semantics.Static.Expressions.SimpleNameExpressionStaticSemantics.html)
implements it.

### Candidates

`ResolveValue` and `ResolveType` bind different candidates (**MS-VBAL §5.6.10**). A simple name expression is
bound by `ResolveValue`:

|`ResolveValue` binds|`ResolveValue` never binds|
|---|---|
|A variable, constant, Enum type or Enum member, property, function, subroutine, procedural module or project.|A user-defined type or a class module.|

The name of a class that is not predeclared, used in an expression, is an undefined variable. See
[**RD-VBAL §3.1.1.6** `VB_PredeclaredId`](rd-vbal.3.1.1.attributes.md#3116-vb_predeclaredid) and
[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md#candidates).

### Declared Type

|Resolved entity|Declared type of the simple name expression|
|---|---|
|A `Symbol` that determines its own declared type ([ITypedSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.ITypedSymbol.html))|That type, directly.|
|A bare procedure reference|The procedure's return type.|
|A bare reference to a `Sub`|[VBVoidType](../api/RDCore.SDK.Model.Types.Complex.VBVoidType.html).|

- `ITypedSymbol` unifies bound and unbound typed symbols. See
  [**RD-VBAL §2.5.1** Runtime Entities](rd-vbal.2.5.1.runtime-entities.md).
- A `Sub`'s own symbol already carries `VBVoidType` as its type. See
  [**RD-VBAL §5.3.1.6** Subroutine and Function Declarations](rd-vbal.5.3.1.6.subroutine-and-function-declarations.md).

### Type Characters

**MS-VBAL §5.6.10** makes a name invalid when its type character does not match the declared type of what it names: `x$` of a variable declared `As Long`
is [`VBC09335`](../diagnostics/vbc09335.md) _Type-declaration character does not match declared data type_, as it is in MS-VBA. A function of the library
that has a form with the character in its name (`Left$`) is that form.

🎯 A reserved function of the library takes any type character, which **MS-VBA** ignores (recorded in Excel): `Len$("abc")` is `3`, a `Long`, and `CVar$(5)`,
`Abs%(-3)` and `CInt$(2.6)` are the functions without it. RD-VBA does as MS-VBA does, and [`RDC11019`](../diagnostics/rdc11019.md) warns where the character
is not the function's type. Any other function of the library refuses a character that does not match: `Asc$("A")`, `Sqr$(4)` are `VBC09335`.

### Resolution Outcomes

The static semantics of a simple name expression have three outcomes, depending on the resolver's result. Each
outcome is a
[StaticSemanticsEvaluationResult](../api/RDCore.SDK.Semantics.Static.Abstract.StaticSemanticsEvaluationResult.html):

|Resolver result|Outcome|
|---|---|
|Ambiguous: `Duplicate` or `Ambiguous` (see [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md#resolution-results))|An `Error` carrying `AmbiguousName` or `DuplicateDeclaration` ([VBCompileErrorId](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html)).|
|Unresolved, under `Option Explicit`|An `Error` carrying `VariableNotDefined` (`VBCompileErrorId`).|
|Unresolved, without `Option Explicit`|`Success(VBUnknownType)` ([VBUnknownType](../api/RDCore.SDK.Model.Types.VBUnknownType.html)).|

`SimpleNameExpressionStaticSemantics` consumes `ResolveValue`'s error outcomes, and reports an ambiguous or duplicate
name as a coded compile-time error. See
[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md#duplicate-and-ambiguous-names).

The static-semantics layer consumes [ModuleDirectives](../api/RDCore.SDK.Model.Symbols.ModuleDirectives.html) to
decide whether an unresolved simple name is a deferred `VBUnknownType` or a **VBC09302** _Variable not defined_
compile-time error. See [**RD-VBAL §5.2.1** Option Directives](rd-vbal.5.2.1.option-directives.md) and
[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md#module-directives).

### A Name That Is No Value

A project, a procedural module and an Enum type qualify the names of what they declare (`VBA.Len`, `Strings.Left`,
`Color.Red`): as the owner of a member access they are what they are, and by themselves they are no value, variable or
procedure. Written bare or qualified (`VBA.Strings`, `Globals.Color`) where an expression is, each is an error of what it
is, as **MS-VBA** reports it (recorded in Excel), with a verbose message that says what the name is
([NameClassificationStaticSemantics](../api/RDCore.SDK.Semantics.Static.NameClassificationStaticSemantics.html)):

|The name is|Error|
|---|---|
|A project or a library (`VBA`)|[VBC09376](../diagnostics/vbc09376.md) _Expected variable or procedure, not project_|
|A procedural module, of the project or of a library (`Globals`, `Strings`)|[VBC09363](../diagnostics/vbc09363.md) _Expected variable or procedure, not module_|
|An Enum type (`Color`)|[VBC09377](../diagnostics/vbc09377.md) _Expected variable or procedure, not enum type_|

That is the error wherever the name is: a value, an argument, the target of a `Let` or a `Set`, the operand of `Not`,
of a negation or of parentheses, a condition, a statement by itself. A few places report it as what they require of the
name instead:

|Where|Example|Error|
|---|---|---|
|The operand of a binary operator|`Strings + 1`, `VBA & "x"`, `Color = 1`, `Strings Is Nothing`|[VBC09310](../diagnostics/vbc09310.md) _Type mismatch_|
|The value a `Set` statement assigns|`Set o = Strings`, `Set o = Color`|`VBC09310` _Type mismatch_|
|What `Len` and `LenB` measure|`Len(Globals)`, `LenB(Strings)`|[VBC09333](../diagnostics/vbc09333.md) _Variable required_|
|What a `With` block is about|`With Strings`|[VBC09378](../diagnostics/vbc09378.md) ([**RD-VBAL §5.4.2.21**](rd-vbal.5.4.2.21.with-statement.md))|
|What a `For Each` loop iterates|`For Each v In VBA`|[VBC09379](../diagnostics/vbc09379.md) ([**RD-VBAL §5.4.2.4**](rd-vbal.5.4.2.4.for-each-statement.md))|

A user-defined type or a class is no candidate of `ResolveValue` at all: its name is an undefined variable
(`x = Pair`), and _Method or data member not found_ qualified by its module (`Program.Pair`).

### Unresolved Names

MS-VBA permits an implicit `Variant` declaration for an unresolved name (without `Option Explicit`).

RD-VBA defers inferring the type of an unresolved name to a later type-inference pass
([IVBInferableType](../api/RDCore.SDK.Model.Types.Complex.IVBInferableType.html)), rather than deciding it in the
simple-name-expression rule. See [**RD-VBAL §2.4.4** Deferred Types](rd-vbal.2.4.4.deferred-types.md).

## Runtime Semantics

`RuntimeExpressionEvaluator.EvaluateSimpleName` evaluates a simple name expression according to the symbol the name
resolves to:

|Resolved symbol|Evaluation|
|---|---|
|A `Sub`, `Function` or `Property Get`, referenced by a bare name (other than the executing `Function` or `Property Get` itself, below)|An implicit call: the procedure is invoked through [IProcedureInvoker](../api/RDCore.SDK.Runtime.Abstract.Execution.IProcedureInvoker.html), with zero arguments.|
|The executing `Function` or `Property Get`: a bare reference to a procedure's own name, from within its own body|Reads the `ReturnValue` slot instead of the general symbol table.|
|A `Const`, `EnumConst`, module or instance field, or UDT field|A plain value read, not an implicit call attempt.|

See [**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)
for the invocation itself.

### Members of the Current Instance

A member of a class module named bare in the module's own code is a member of the _current instance_: the call is
made on `Me`. This holds for every form of the call: a `Sub` named alone (`Helper`) or with `Call` (`Call Helper`), a
`Function` given arguments (`Twice(3)`), a `Property Get` read by its name (`If IsEmptyBox Then`), and a `Property Let`
assigned by it (`Size = 3`). A member called on one object therefore reads and writes that object's state, and no
other's, whichever object it is, the class's default instance included
([**RD-VBAL §5.2.4.1.2** Default Instance Variables Static Semantics](rd-vbal.5.2.4.class-module-declarations.md#52412-default-instance-variables-static-semantics)).

### Implicit Calls

`RuntimeExpressionEvaluator`'s bare `SimpleName` dispatch decides whether a name is an implicit call by checking the
two callable leaf symbol types,
[VBFunctionMemberSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBFunctionMemberSymbol.html) and
[VBPropertyGetMemberSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBPropertyGetMemberSymbol.html). It does not
check their shared base type,
[VBReturningMemberSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.VBReturningMemberSymbol.html).

`VBReturningMemberSymbol` is the base type of a `Function`'s and a `Property Get`'s member symbol. `Const`,
`EnumConst`, module-level and instance fields, and UDT fields share this base type with `Function` and `Property Get`
symbols. See [**RD-VBAL §2.5.1** Runtime Entities](rd-vbal.2.5.1.runtime-entities.md).

For an index expression, whether its `Callee` is a bare name resolving to a `Sub`, `Function` or `Property Get` is
checked before the usual recursive `Evaluate(Callee)`. Otherwise, the bare-name `Callee` would already have been
auto-invoked with zero arguments by `SimpleName`'s own dispatch, before the index expression could supply its
arguments. See [**RD-VBAL §5.6.13** Index Expressions](rd-vbal.5.6.13.index-expressions.md).

### Function Result Variable

A bare reference to a procedure's own name, from within its own body, reads the `ReturnValue` slot
([ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.ReturnValue`) instead of the
general symbol table. See
[**RD-VBAL §5.3.1.6** Subroutine and Function Declarations](rd-vbal.5.3.1.6.subroutine-and-function-declarations.md).

`RuntimeExpressionEvaluator.EvaluateSimpleName` detects a self-reference to the executing procedure by comparing the
resolved symbol's `Uri` against `RuntimeEvaluationContext.Scope`. `RuntimeProcedureInvoker` sets
`RuntimeEvaluationContext.Scope` to the invoked procedure's own `Uri` for the whole activation
([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).

`RuntimeExpressionEvaluator.EvaluateInstance` resolves `Me` from the same `RuntimeEvaluationContext.Scope`. See
[**RD-VBAL §5.6.11** Instance Expressions](rd-vbal.5.6.11.instance-expressions.md).

### Arrays

A plain array-typed variable read back as an expression (`SimpleNameExpressionNode`, the ordinary shape of `arr` in
`For Each item In arr`) yields the array value stored in it. See
[**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md) and
[**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md).

## Implementation

|Type or member|Role|
|---|---|
|`SimpleNameExpressionStaticSemantics`|Static semantics: the declared type of a simple name expression, and its compile-time errors.|
|`RuntimeExpressionEvaluator.EvaluateSimpleName` (**RDCore.Runtime**)|Runtime semantics: a value read, an implicit call, or a read of the function result variable.|

---
> ⏮️ [**RD-VBAL §5.6.9.8** Logical Operators](rd-vbal.5.6.9.8.logical-operators.md) | ⏭️ [**RD-VBAL §5.6.11** Instance Expressions](rd-vbal.5.6.11.instance-expressions.md)
