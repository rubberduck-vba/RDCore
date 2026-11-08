# 5.4.2.6 Do Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.6** Do Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/61d886e0-1768-4032-8bbb-dd3eca7977df).

## Syntax

`Do...Loop` has five header shapes, each represented by its own node type.

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[DoLoopStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoLoopStatementNode.html)|`Jump`|Bare `Do` … `Loop`. The back-edge `Jump` carries the loop's own node; its `Target` is the body's first instruction.|
|[DoWhileLoopStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoWhileLoopStatementNode.html)|`ConditionalBranch`, `Jump`|Pre-test `Do While` … `Loop`. The header's `Else` is right past the loop; a synthesized back-edge `Jump` (`Node = null`) targets the header.|
|[DoUntilLoopStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoUntilLoopStatementNode.html)|`ConditionalBranch`, `Jump`|Pre-test `Do Until` … `Loop`; the same shape as `Do While`.|
|[DoLoopWhileStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoLoopWhileStatementNode.html)|`LoopBack`|Post-test `Do` … `Loop While`. The `LoopBack` closer carries the loop's own node; its `Target` is the body's first instruction.|
|[DoLoopUntilStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoLoopUntilStatementNode.html)|`LoopBack`|Post-test `Do` … `Loop Until`; the same shape as `Loop While`.|

The `…Until` half of each pre-test and post-test pair shares its node shape with the `…While` half. `Loop` has no AST
node of its own ([**RD-VBAL §3.4.1** Block Statements](rd-vbal.3.4.1.block-statements.md);
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

## Static Semantics

MS-VBAL models `…While` and `…Until` loops as the same construct with opposite exit polarity.

`Exit Do` resolves against the innermost enclosing loop of any of the five forms
([**RD-VBAL §5.4.2.7** Exit Do Statement](rd-vbal.5.4.2.7.exit-do-statement.md)).

## Runtime Semantics

|Form|Dispatch|
|---|---|
|Pre-test: `Do While`, `Do Until`|A `ConditionalBranch`, dispatched in the same way as an `If` header ([**RD-VBAL §5.4.2.8** If Statement](rd-vbal.5.4.2.8.if-statement.md)). When the loop continues, execution falls through into the body; when it ends, it goes to the header's `Else` offset, right past the loop.|
|Post-test: `Do…Loop While`, `Do…Loop Until`|`LoopBack`: evaluate the condition; branch back to the body's first instruction ([Instruction](../api/RDCore.SDK.Semantics.Instructions.Instruction.html)`.Target`) when the loop continues, and fall through when it ends.|
|Bare: `Do…Loop`|An unconditional `Jump` back to its own body. It needs no dispatch of its own.|

The condition of a pre-test or post-test loop is a Boolean condition, evaluated by the same `ConditionEvaluator` an
`If` uses, and forced to `Boolean`
([**MS-VBAL §5.5.1.2.2** Let-coercion to and from Boolean](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3a9f5227-5fd5-4240-949a-51ffc32e71a9)).

|Loop|Exits when the condition is|
|---|---|
|`…While`|`False`|
|`…Until`|`True`|

For an `…Until` loop, the evaluated Boolean is inverted before branching.

Pre-test, post-test and bare `Do` loops need no per-activation state.

## Implementation

- `ProcedureExecutor` dispatches the pre-test header's `ConditionalBranch`, the post-test closer's `LoopBack`, and the
  bare loop's `Jump`.
- A post-test loop's closer evaluates the condition and decides whether to branch back. The `LoopBack` instruction
  carries the loop's own node: it is the loop's only instruction
  ([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).
- `GetCondition` returns a `(Condition, Negate)` pair instead of a bare expression; `Negate` is true for an `…Until`
  loop. The evaluated Boolean loop condition is inverted before branching when `Negate` is true. The pair avoids
  duplicating the branch logic for a second polarity.
- `RDCore.Runtime.Execution.ConditionEvaluator` forces the condition to `Boolean`
  ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
