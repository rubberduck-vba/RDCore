# 5.6.13 Index Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.13** Index Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/551030b2-72a4-4c95-9cb0-fb8f8c8774b4).

## Syntax

|Expression|AST node|
|---|---|
|Index|[IndexExpressionNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Expressions.IndexExpressionNode.html)|

See [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md).

**MS-VBAL §3.3** makes the blanks before a token part of it, so blanks between an expression and its argument list leave it its argument list:
`x = F (2)` calls `F` with `2`, and `F (1) = 2` assigns to `F(1)`. So it is in a list to print, where the blanks separate no output items: MS-VBA
reads `Debug.Print F (2)` as a call of `F` (recorded in Excel). See [**RD-VBAL §5.4.5.8.1** Output Lists](rd-vbal.5.4.5.8.print-statement.md#54581-output-lists).

Right after what a call statement without `Call` calls, they are read otherwise: `F (2)` is `F` called with the parenthesized argument `(2)`
([**RD-VBAL §5.4.2.1** Call Statement](rd-vbal.5.4.2.1.call-statement.md)). They are read the same way after the object of a `Line` statement, which is no expression
of its own: `Line (0, 0) - (1, 1)` begins with its first point.

> [!NOTE]
> **Not implemented.** A call statement whose callee goes on past an argument list written after blanks (`Foo (1).Bar`, `Foo (1) (2)`) is a syntax
> error in RD-VBA.

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

### A Function That Returns an Array

The argument list written after a function that returns an array is the call's, and an argument list after that one indexes what it returns
(recorded in Excel, oracle\s55):

|Code|Of|Is|
|---|---|---|
|`x = F()(1)`|`Function F() As Long()`|The element `1` of what `F` returns.|
|`x = G(1)(0)`|`Function G(n As Long) As Long()`|The element `0` of what `G(1)` returns.|
|`x = UBound(F)`|`Function F() As Long()`|The upper bound of what `F` returns.|
|`x = F(1)`|`Function F() As Long()`|[VBC09342](../diagnostics/vbc09342.md) _Wrong number of arguments or invalid property assignment_.|
|`x = G(1, 0)`|`Function G(n As Long) As Long()`|`VBC09342`, not a type mismatch of the array assigned to `x`.|

> [!IMPORTANT]
> **MS-VBAL §5.6.13** lets the arguments of a function "with a parameter list that cannot accept any parameters" index what it returns, when its
> declared type is an array and they are as many as its rank: `F(1)` would be an element. **MS-VBA** gives them to the function, and refuses the
> call at compile time (oracle\s55 A01). **RD-VBA** follows MS-VBA. A function that takes no parameters and returns a `Variant` or an object is
> not given them: they index what it returns, as the spec says.

### Variant Holding an Array

`RuntimeExpressionEvaluator.EvaluateIndex` unwraps a
[VBVariantValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html) before matching a wrapped
[VBArrayValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBArrayValue.html), so `v(0)` indexes a `Variant` holding an
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
|A subscript past the range of a `Long`.|6 (Overflow)|
|A subscript of text that is no number.|13 (Type mismatch)|
|A `Null` subscript.|94 (Invalid use of Null)|
|A subscript outside its dimension, or a number of subscripts other than the array's rank.|9 (Subscript out of range)|

### Empty Argument List

An array written with an empty argument list is the array itself (**MS-VBAL §5.6.13**), with the classification and the declared type of the array:
`f()` is `f`, wherever it is written. MS-VBA agrees (recorded in Excel, oracle\s53), for a fixed-size array and a dynamic one alike:

|Code|Is|
|---|---|
|`UBound(f())`, `LBound(f(), 1)`|The bounds of `f`.|
|`v = f()`|A copy of `f`, as `v = f` is.|
|`Fill f()`, to `ByRef a() As Long`|`f` itself, passed by reference: what `Fill` assigns, `f` holds.|
|`Erase f()`|`Erase f`.|
|`Debug.Print f()`, as `Debug.Print f`|[VBC09310](../diagnostics/vbc09310.md) _Type mismatch_, when it compiles: an array is no value `Debug.Print` writes.|

The parser reads `f()` as one missing argument;
[IndexExpressionNode.IsEmptyArgumentList](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Expressions.IndexExpressionNode.html) says whether an argument list is empty,
written either way.

> [!IMPORTANT]
> **MS-VBAL §5.6.13** makes an array given a second empty argument list, `f()()`, invalid: the rule takes an empty argument list only of an array that
> "an empty argument list has not already been specified for". **MS-VBA** compiles it as the array (`UBound(f()())` compiles), and raises run-time error 9
> (Subscript out of range) whenever it runs (oracle\s53 E07, E08, F07, F08). **RD-VBA** follows MS-VBA, and warns:
> [RDC11025](../diagnostics/rdc11025.md).

### Object Callee

An index expression whose `Callee` is an object - or a `Variant` holding one - calls the object's **default member**, the one its class marks with `VB_UserMemId = 0`: `c(1)` is
`c.Item(1)`, and `c(1)(2)` indexes what that returns. A class of the workspace and a class of the library are found alike
([**RD-VBAL §6.1.3.1** Collection Object](rd-vbal.6.1.3.1.collection-object.md)). An object with no such member is runtime error 438, and `Nothing` is runtime error 91.
The target of an assignment is the same call: `c(1) = 5` assigns `c.Item(1)` ([**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md#elements-of-an-array)).

#### A Default Member That Takes No Parameters

A default member that takes no parameters is called with none, and the argument list applies to what it returns, as if that were the callee: the
default member of the object it returns, again, or an element of the array. MS-VBA does the same (recorded in Excel):

|Code|Of|Is|
|---|---|---|
|`v = o("k")`, `v = o(1)`|`Property Get Inner() As Collection`, the default member of `o`'s class|`o.Inner("k")`, early bound and late bound.|
|`v = ch("k")`|A default member that returns an object whose own default member takes no parameters|`ch.Inner.Inner("k")`, early bound and late bound.|
|`v = a(1)`|`Property Get Items() As Variant`, which returns an array|The element `1` of the array.|
|`o2("k") = 5`, `Set o2("k") = c`|A default member that returns an object whose default `Item` has a `Property Let` and a `Property Set`|`o2.Inner("k") = 5`, `Set o2.Inner("k") = c`.|
|`o("k") = 9`|`Property Get Inner() As Collection`|Run-time error 424: the `Collection`'s `Item` has no `Property Let`, and what it returns is not an object.|

**RD-VBA differs from MS-VBAL:** **MS-VBAL §5.6.13** gives an object declared `Object` or `Variant` the same rule at run time: its default member is
resolved with the argument list queue, and what it returns is given the arguments. MS-VBA, and RD-VBA with it, does so only where the default member
returns an object and the assignment is not a `Set`. Called late, a default member that takes no parameters and returns an array is run-time error 451
when given an argument list, and a `Set` through it is run-time error 450. Nothing in the code says what an `Object` holds before it runs, so no
diagnostic reports it.


## 5.6.13.1 Argument Lists

This section corresponds to [**MS-VBAL §5.6.13.1** Argument Lists](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5b35d806-1305-4427-a120-d25a71c45c02).

The `ByVal` keyword (`argument-expression = ["byval"] expression`) is written before one argument. It is a token of the source and stays one in the
tree: a [ByValArgumentExpressionNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Expressions.ByValArgumentExpressionNode.html) wraps the argument it is written
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
A parameter declared `ByVal` and an array (`ByVal a() As Long`) is `VBC09371` too, wherever it is declared
([**RD-VBAL §5.3.1.5**](rd-vbal.5.3.1.5.parameter-lists.md)).

`ByValArgumentStaticSemantics` checks each `ByVal` argument against its parameter; how the parameter is declared is a fact of the argument
([ArgumentFact](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Semantics.ArgumentFact.html)), from which RDCore.Diagnostics reports the
redundant ones ([`RDC00310`](../diagnostics/rdc00310.md)). Which library parameters the
type library declares by reference is the parameter's `SymbolProperties.TypeLibraryByRef`, read off the declarations (a `Variant` parameter, or
`StdLibTypeLibraryByRefAttribute`); it says nothing of how the argument is passed. [VBC09327](../diagnostics/vbc09327.md), MS-VBAL's own error, is no
longer raised.

See [**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md#argument-mapping).


## 5.6.13.2 Argument List Queues

This section corresponds to [**MS-VBAL §5.6.13.2** Argument List Queues](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9a66cbdb-d2af-40e4-b6f2-b63bcde2a1f3).

> [!NOTE]
> Reserved. This section has no content yet.
