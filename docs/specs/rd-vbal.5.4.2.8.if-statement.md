# 5.4.2.8 If Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.8** If Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/17ff9b37-fbc8-491f-85b2-13c3a379acac).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[IfBlockStatementNode](../api/RDCore.SDK.Model.AST.Statements.IfBlockStatementNode.html)|`ConditionalBranch`|The `If` header.|
|[ElseIfBlockStatementNode](../api/RDCore.SDK.Model.AST.Statements.ElseIfBlockStatementNode.html)|`ConditionalBranch`|An `ElseIf` header.|
|[ElseBlockStatementNode](../api/RDCore.SDK.Model.AST.Statements.ElseBlockStatementNode.html)|None|The `Else` branch needs no condition. The last header's `Else` offset names the first instruction of its body.|
|None (synthesized)|`Jump`|The trailing jump at the end of each branch's body. `Node` is `null`; `Target` is right past the whole construct.|

See [**RD-VBAL §3.4.1** Block Statements](rd-vbal.3.4.1.block-statements.md) and
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

## Runtime Semantics

The executor dispatches `ConditionalBranch` for an `If` or `ElseIf` header, or a single-line `If`
([**RD-VBAL §5.4.2.9** Single-line If Statement](rd-vbal.5.4.2.9.single-line-if-statement.md)), with a Boolean
condition.

1. Evaluate the header's condition, forced to `Boolean` by `ConditionEvaluator`
   ([**MS-VBAL §5.5.1.2.2** Let-coercion to and from Boolean](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3a9f5227-5fd5-4240-949a-51ffc32e71a9)).
2. When the condition is `True`, fall through into the branch's body.
3. When the condition is `False`, go to the header's `Else` offset: the next header in the chain, the first
   instruction of the `Else` body, or right past the whole construct.
4. After a branch's body runs, the synthesized trailing `Jump` goes right past the whole construct.

A condition is not coerced through an operator node, because a condition has no operator of its own in source:
`If x Then` let-coerces `x` without any `(...)`.

## Implementation

- `ProcedureExecutor` dispatches `ConditionalBranch`.
- `RDCore.Runtime.Execution.ConditionEvaluator` forces the condition to `Boolean`. It calls
  `VBBooleanLetCoercionRuntimeSemantics` directly, rather than through an operator node
  ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
- An `If` block needs no synthesized closer: leaving the last branch, or the `Else` branch, already reaches the
  offset that the construct's own `End`/`Else` chaining designates.
- After every branch's body, lowering emits a synthesized, unconditional `Jump` to right past the whole construct,
  even for the last branch ([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).
