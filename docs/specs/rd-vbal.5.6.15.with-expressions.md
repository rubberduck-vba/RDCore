# 5.6.15 With Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.15** With Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/97f83233-034d-4a41-ba62-1b5518da85a2).

A _with-expression_ is a `.Member` or `!member` expression inside a `With` block. It refers to the target of the
innermost enclosing `With` statement. See [**RD-VBAL §5.4.2.21** With Statement](rd-vbal.5.4.2.21.with-statement.md).

## Static Semantics

At compile time, the declared type of the innermost enclosing `With` block's target is carried by
`StaticEvaluationContext.EnclosingWithTargetType`.
See [**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md).

## Runtime Semantics

For a `.Member` or `!member` with-expression, the `EnclosingWithTarget` resolution of `RuntimeExpressionEvaluator`
yields the target stored by the innermost enclosing `With` statement.

With-expression target resolution does not depend on how control reached the instruction; it holds when control
arrives through `GoTo`:

1. The `With` statement stores its evaluated target on the activation
   ([**RD-VBAL §5.4.2.21** With Statement](rd-vbal.5.4.2.21.with-statement.md)).
2. Every `Simple` and `ConditionalBranch` instruction's `RuntimeEvaluationContext` is recomputed before each
   dispatch, from `Instruction.EnclosingWith`, which is a purely lexical property of the instruction
   ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
3. The with-expression resolves against `RuntimeEvaluationContext.EnclosingWithTarget`.
