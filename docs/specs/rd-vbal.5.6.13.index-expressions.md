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


## 5.6.13.1 Argument Lists

This section corresponds to [**MS-VBAL §5.6.13.1** Argument Lists](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5b35d806-1305-4427-a120-d25a71c45c02).

The `ByVal` keyword (`argument-expression = ["byval"] expression`) flags one argument as passed by value, whatever mechanism
its parameter declares. It is a token of the source and stays one in the tree: a
[ByValArgumentExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.ByValArgumentExpressionNode.html) wraps the argument
it is written before, in an argument list of a call and of a named argument alike. Such an argument is a value bound to nothing, so
it is never aliased to a `ByRef` parameter ([**RD-VBAL §5.3.1.11**](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).

It is invalid for an argument list to have a `ByVal` argument unless it is that of an invocation of an external procedure. The
parser cannot tell what is invoked, so this is a compile error where the callee is known,
[VBC09327](../diagnostics/vbc09327.md), not a syntax error: `Foo ByVal x` parses, and is valid when `Foo` is a `Declare`. The
argument list of a `RaiseEvent` is the exception, being known not to be that of an external procedure: there it is a syntax
error ([**RD-VBAL §5.4.2.20**](rd-vbal.5.4.2.20.raiseevent-statement.md)).

See [**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md#argument-mapping).


## 5.6.13.2 Argument List Queues

This section corresponds to [**MS-VBAL §5.6.13.2** Argument List Queues](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9a66cbdb-d2af-40e4-b6f2-b63bcde2a1f3).

> [!NOTE]
> Reserved. This section has no content yet.

---
> ⏮️ [**RD-VBAL §5.6.12** Member Access Expressions](rd-vbal.5.6.12.member-access-expressions.md) | ⏭️ [**RD-VBAL §5.6.14** Dictionary Access Expressions](rd-vbal.5.6.14.dictionary-access-expressions.md)
