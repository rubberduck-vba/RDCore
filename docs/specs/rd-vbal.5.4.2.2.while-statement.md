# 5.4.2.2 While Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.2** While Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4f2f6c46-3c09-4a6d-905b-fe6658405b6f).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[WhileWendStatementNode](../api/RDCore.SDK.Model.AST.Statements.WhileWendStatementNode.html)|`ConditionalBranch`|The pre-test loop header. Its `Else` is right past the whole loop.|
|None (synthesized)|`Jump`|The back-edge at the end of the body; `Node` is `null`. Its `Target` is the loop's header.|

`Wend` has no AST node of its own ([**RD-VBAL §3.4.1** Block Statements](rd-vbal.3.4.1.block-statements.md);
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

## Static Semantics

MS-VBAL gives `While…Wend` no exit statement of its own. A `While…Wend` loop satisfies neither `Exit For` nor
`Exit Do`.

An `Exit Do` written inside a `While…Wend` is not consumed by it: it resolves against the `Do` loop that encloses
the `While…Wend` ([**RD-VBAL §5.4.2.7** Exit Do Statement](rd-vbal.5.4.2.7.exit-do-statement.md)).

## Runtime Semantics

A `While…Wend` loop is a pre-test loop, like `Do While` and `Do Until`
([**RD-VBAL §5.4.2.6** Do Statement](rd-vbal.5.4.2.6.do-statement.md)). Its header is a `ConditionalBranch`,
dispatched in the same way as an `If` header ([**RD-VBAL §5.4.2.8** If Statement](rd-vbal.5.4.2.8.if-statement.md)).

1. Evaluate the condition. It is a Boolean condition, evaluated by the same `ConditionEvaluator` an `If` uses, and
   forced to `Boolean`
   ([**MS-VBAL §5.5.1.2.2** Let-coercion to and from Boolean](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3a9f5227-5fd5-4240-949a-51ffc32e71a9)).
2. When the condition is `True`, fall through into the body. The body's synthesized `Jump` returns to the header.
3. When the condition is `False`, go to the header's `Else` offset, right past the loop.

The loop needs no per-activation state.

## Implementation

- `ProcedureExecutor` dispatches the header's `ConditionalBranch` and the back-edge `Jump`.
- `RDCore.Runtime.Execution.ConditionEvaluator` forces the condition to `Boolean`
  ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
- Lowering pushes no `Exit Do` context for a `While…Wend` loop
  ([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).
