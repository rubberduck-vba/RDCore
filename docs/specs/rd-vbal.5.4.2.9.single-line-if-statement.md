# 5.4.2.9 Single-line If Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.9** Single-line If Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6b4fae50-5e47-4469-970b-a2b5a2b62e7a).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[InlineIfStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.InlineIfStatementNode.html)|`ConditionalBranch`|`ThenBody` and `ElseBody` may each hold several colon-separated statements instead of a full `block`.|
|[GoToStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.GoToStatementNode.html) (synthesized by the parser)|`Jump`|A bare line-number target in either branch, e.g. `If x Then 100`.|

A bare line-number target in either branch is not modelled as its own AST shape. MS-VBAL specifies such a target as
equivalent to a `GoTo` statement targeting that line, so the parser synthesizes a `GoToStatementNode` as that
branch's statement, typically its only one
([**RD-VBAL §5.4.2.12** GoTo Statement](rd-vbal.5.4.2.12.goto-statement.md)).

See [**RD-VBAL §3.4.1** Block Statements](rd-vbal.3.4.1.block-statements.md) and
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

## Runtime Semantics

The executor dispatches `ConditionalBranch` for a single-line `If`, with a Boolean condition, the same way as for a
block `If` header ([**RD-VBAL §5.4.2.8** If Statement](rd-vbal.5.4.2.8.if-statement.md)).

1. Evaluate the condition, forced to `Boolean` by `ConditionEvaluator`
   ([**MS-VBAL §5.5.1.2.2** Let-coercion to and from Boolean](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3a9f5227-5fd5-4240-949a-51ffc32e71a9)).
2. When the condition is `True`, fall through into `ThenBody`.
3. When the condition is `False`, go to the header's `Else` offset.

## Implementation

- `ProcedureExecutor` dispatches `ConditionalBranch`.
- `RDCore.Runtime.Execution.ConditionEvaluator` forces the condition to `Boolean`
  ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
