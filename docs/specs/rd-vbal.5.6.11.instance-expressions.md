# 5.6.11 Instance Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.11** Instance Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6665e1e5-6c40-43a3-a989-da7ee48e7bd1).

An _instance expression_ is the keyword `Me`.

## Static Semantics

**MS-VBAL §5.6.11** describes instance expressions (`Me`) as _values_:

|Enclosing procedure|`Me`|
|---|---|
|In a class module|A value whose _declared type_ is defined by the class module containing the _enclosing procedure_.|
|In a procedural ("standard") module|Statically invalid.|

## Runtime Semantics

At run time, an instance expression (`Me`) represents the _current instance_ of the type defined by the enclosing
class module, and has this type as its _value type_ (**MS-VBAL §5.6.11**).

In line with **MS-VBAL §5.6.11**, RD-VBA implements `Me` as a simple runtime artifact, the _current object_, rather
than as an expression. The current object is a common concept in many programming languages, where it is often
written with the token `this`.

- The runtime injects an implicit `Me` (`ByVal`) parameter into all _instance member calls_, pointed at the current
  object.
- The runtime context supplies `Me` as a [VBObjectValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBObjectValue.html)
  presenting the _default interface_ of the enclosing class type.
- The `Me` value is pushed to the _stack frame_ of instance member calls as any parameter is. See
  [**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md).

`RuntimeExpressionEvaluator.EvaluateInstance` resolves the name `Me` through `ResolveValue`, from
`RuntimeEvaluationContext.Scope`. `RuntimeProcedureInvoker` sets `RuntimeEvaluationContext.Scope` to the invoked
procedure's own `Uri`. `RuntimeExpressionEvaluator.EvaluateSimpleName` uses the same `RuntimeEvaluationContext.Scope`
to detect a self-reference to the executing procedure; see
[**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md).
