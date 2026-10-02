# 5.4.2.7 Exit Do Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.7** Exit Do Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f672b312-fe2a-4f4d-9ad4-42729b110fe7).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `"Exit Do"`)|`ExitLoop`|`Target`: the offset right past the closer of the innermost enclosing `Do` loop.|

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md) and
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

## Static Semantics

`Exit Do` needs an enclosing loop of one of the five `Do…Loop` forms
([**RD-VBAL §5.4.2.6** Do Statement](rd-vbal.5.4.2.6.do-statement.md)). It resolves against the innermost enclosing
loop of that kind.

A `While…Wend` loop does not satisfy `Exit Do`: MS-VBAL gives `While…Wend` no exit statement of its own
([**RD-VBAL §5.4.2.2** While Statement](rd-vbal.5.4.2.2.while-statement.md)). An `Exit Do` written inside a
`While…Wend` is not consumed by it; it resolves against the `Do` loop that encloses the `While…Wend`.

An `Exit Do` that has no enclosing `Do` loop, including one inside a `While…Wend` that no `Do` loop encloses, is
[`VBC09312`](../diagnostics/vbc09312.md) (`ExitDoNotWithinDoLoop`). The rule is
[ExitStatementStaticSemantics](../api/RDCore.SDK.Semantics.Static.ExitStatementStaticSemantics.html)'s, which
[StatementStaticSemanticsEvaluator](../api/RDCore.SDK.Semantics.Static.StatementStaticSemanticsEvaluator.html) and instruction-list
lowering both ask. Lowering emits no instruction for the statement that is an error.

## Runtime Semantics

1. `ExitLoop` branches to [Instruction](../api/RDCore.SDK.Semantics.Instructions.Instruction.html)`.Target`,
   in the same way as `Jump` ([**RD-VBAL §5.4.2.12** GoTo Statement](rd-vbal.5.4.2.12.goto-statement.md)).
2. No runtime search is needed: lowering has already resolved `Target` to the offset right past the innermost
   enclosing loop of the matching kind.

## Implementation

- [InstructionListLowering](../api/RDCore.SDK.Semantics.Instructions.InstructionListLowering.html) resolves the
  `ExitLoop` target ([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).
- `ProcedureExecutor` uses the same `case` arm for `ExitLoop` and `Jump`
  ([**RD-VBAL §5.4.2.5** Exit For Statement](rd-vbal.5.4.2.5.exit-for-statement.md)).

---
> ⏮️ [**RD-VBAL §5.4.2.6** Do Statement](rd-vbal.5.4.2.6.do-statement.md) | ⏭️ [**RD-VBAL §5.4.2.8** If Statement](rd-vbal.5.4.2.8.if-statement.md)
