# 5.6.13 Index Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.13** Index Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/551030b2-72a4-4c95-9cb0-fb8f8c8774b4).

## Syntax

|Expression|AST node|
|---|---|
|Index|[IndexExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.IndexExpressionNode.html)|

See [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md).

## Runtime Semantics

`RuntimeExpressionEvaluator.EvaluateIndex` evaluates an index expression.

### Procedure Callee

For an `IndexExpressionNode`, whether its `Callee` is a bare name resolving to a `Sub`, `Function` or `Property Get`
is checked before the usual recursive `Evaluate(Callee)`. Such an index expression invokes the procedure with the
index expression's own arguments. See
[**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md).
A member of the class module whose code it is, named bare, is invoked on the current instance
([**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md#members-of-the-current-instance)).

The check precedes `Evaluate(Callee)` because, otherwise, a bare-name `Callee` would already have been auto-invoked
with zero arguments by `SimpleName`'s own dispatch, before the index expression could supply its arguments. See
[**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md).

An index expression with a procedure `Callee` is the only expression form that recurses (e.g. `Foo(n - 1)`, even
from within `Foo`'s own body). A bare `Foo`, from within `Foo`'s own body, reads the function result variable instead
([**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md)).

### Variant Holding an Array

`RuntimeExpressionEvaluator.EvaluateIndex` unwraps a
[VBVariantValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html) before matching a wrapped
[VBArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBArrayValue.html), so `v(0)` indexes a `Variant` holding an
array the same as a declared array. See
[**RD-VBAL §5.5.1.2.12** Let-coercion to Variant](rd-vbal.5.5.1.2.runtime-semantics.md#551212-let-coercion-to-variant).

### Subscripts

An index expression whose `Callee` is an array selects an element: one subscript per dimension, each Let-coerced to
`Long`, as a bound is. **MS-VBAL §5.6.13** only says that each argument must be "within its respective array
dimension"; MS-VBA coerces it as any value is coerced to a `Long`
([**RD-VBAL §5.5.1.2**](rd-vbal.5.5.1.2.runtime-semantics.md)):

|Subscript|Selects|
|---|---|
|`2.5`, `1.5`|`2`, rounded to even.|
|`"2"`|`2`.|
|`True`, `Empty`|`-1`, `0`.|
|A `Byte`, a `Currency`, a `Date`, a `Variant` holding a number|Its value as a `Long`.|

|Condition|Run-time error|
|---|---|
|A subscript past the range of a `Long`.|6 — Overflow|
|A subscript of text that is no number.|13 — Type mismatch|
|A `Null` subscript.|94 — Invalid use of Null|
|A subscript outside its dimension, or a number of subscripts other than the array's rank.|9 — Subscript out of range|

### Object Callee

An index expression whose `Callee` is an object - or a `Variant` holding one - calls the object's **default member**, the one its class marks with `VB_UserMemId = 0`: `c(1)` is
`c.Item(1)`, and `c(1)(2)` indexes what that returns. A class of the workspace and a class of the library are found alike
([**RD-VBAL §6.1.3.1** Collection Object](rd-vbal.6.1.3.1.collection-object.md)). An object with no such member is runtime error 438, and `Nothing` is runtime error 91.


## 5.6.13.1 Argument Lists

This section corresponds to [**MS-VBAL §5.6.13.1** Argument Lists](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5b35d806-1305-4427-a120-d25a71c45c02).

The `ByVal` keyword (`argument-expression = ["byval"] expression`) is written before one argument. It is a token of the source and stays one in the
tree: a [ByValArgumentExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.ByValArgumentExpressionNode.html) wraps the argument it is written
before, in an argument list of a call, of a call statement and of a named argument alike. Such an argument is a value bound to nothing, so it is never
aliased to a `ByRef` parameter ([**RD-VBAL §5.3.1.11**](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).

### Where `ByVal` is written

> [!IMPORTANT]
> **MS-VBAL §5.6.13.1** says it is invalid for an argument list to have a `ByVal` argument unless it is that of an invocation of an external procedure.
> **MS-VBA** compiles it elsewhere too, and **RD-VBA** follows MS-VBA: a `ByVal` argument is valid where MS-VBA compiles it, and is the compile error
> MS-VBA reports where it does not (recorded in Excel, 100 cases).

What decides is the parameter the argument is for, not the procedure. MS-VBA takes `ByVal` only where the argument is passed by value already, and where it
means something:

|The argument is for|`ByVal` is|Why|
|---|---|---|
|A `ByVal` parameter of any procedure - of the workspace, of a class, of a library, of a `Declare`; an index of a `Property Let`|Valid, and changes nothing. [RDC00310](../diagnostics/rdc00310.md) says so, a _Hint_.|The parameter takes its argument by value with the keyword or without: the value is let-coerced to the parameter's type as it is without it (`Twice(ByVal 2.7)` is `6`, as `Twice(2.7)` is), and an object is passed as itself, not as the value of its default member - which `(c)` would be, so `ByVal c` is not the parentheses of [**RD-VBAL §5.6.6**](rd-vbal.5.6.6.parenthesized-expressions.md).|
|A `ByRef` or `As Any` parameter of a `Declare`, or an `As Any` parameter of the library (`VarPtr`)|Valid.|The argument is passed by value where a reference goes: the value itself where an address is expected ([**RD-VBAL §6.1.2.13**](rd-vbal.6.1.2.13.hidden-module.md#varptr)).|
|A `ByRef` parameter of a procedure of the workspace or of a class, an `Optional ByRef` one too|[VBC09310](../diagnostics/vbc09310.md) _Type mismatch_.|A procedure of VBA code is given a reference to a variable, which a value is not.|
|A `ByRef` array parameter (`a() As Long`)|[VBC09371](../diagnostics/vbc09371.md) _Array argument must be ByRef_.|An array is always passed by reference.|
|A `ParamArray`, the `Array` keyword's elements|`VBC09310` _Type mismatch_.|A `ParamArray` collects references to its arguments.|
|A parameter of the library the type library declares by reference: every `Variant` one (`VARIANT*`) but the `String` of a `$` function, and every one of `Replace`'s|`VBC09310` _Type mismatch_.|As for the workspace's `ByRef`: `Left(ByVal "abc", 1)` is refused, `Left("abc", ByVal 1)` and `Left$(ByVal "abc", 1)` are not.|
|A member called late-bound (on an `Object` or a `Variant`)|`VBC09310` _Type mismatch_.|Its parameters are not known, and are taken to be `ByRef`.|
|A function MS-VBA compiles inline: the reserved names (`Abs`, `Fix`, `Int`, `Sgn`, `Len`, `LenB`, the conversion functions but `CDec`, `String`), `InStr`, `InStrB`, `StrComp`, `LBound`, `UBound`|[VBC00001](../diagnostics/vbc00001.md) _Syntax error_.|They are parsed with their arguments, and the parser takes no `ByVal` there.|
|An index of an array (`a(ByVal 0)`)|`VBC00001` _Syntax error_.|An index is no argument.|

The argument list of a `RaiseEvent` takes no `ByVal` at all: there it is a syntax error ([**RD-VBAL §5.4.2.20**](rd-vbal.5.4.2.20.raiseevent-statement.md)).
A `ByVal a() As Long` parameter declaration is a _Syntax error_ in MS-VBA too.

[ByValArgumentStaticSemantics](../api/RDCore.SDK.Semantics.Static.ByValArgumentStaticSemantics.html) checks each `ByVal` argument against its parameter,
and records the redundant ones as a fact of the argument
([ValueExpressionSemanticFlags](../api/RDCore.SDK.Semantics.Flags.ValueExpressionSemanticFlags.html)`.RedundantByVal`). Which library parameters the
type library declares by reference is the parameter's `SymbolProperties.TypeLibraryByRef`, read off the declarations (a `Variant` parameter, or
`StdLibTypeLibraryByRefAttribute`); it says nothing of how the argument is passed. [VBC09327](../diagnostics/vbc09327.md), MS-VBAL's own error, is no
longer raised.

See [**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md#argument-mapping).


## 5.6.13.2 Argument List Queues

This section corresponds to [**MS-VBAL §5.6.13.2** Argument List Queues](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9a66cbdb-d2af-40e4-b6f2-b63bcde2a1f3).

> [!NOTE]
> Reserved. This section has no content yet.

---
> ⏮️ [**RD-VBAL §5.6.12** Member Access Expressions](rd-vbal.5.6.12.member-access-expressions.md) | ⏭️ [**RD-VBAL §5.6.14** Dictionary Access Expressions](rd-vbal.5.6.14.dictionary-access-expressions.md)
