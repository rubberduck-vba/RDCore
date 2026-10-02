# 5.4.2.17 Exit Sub Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.17** Exit Sub Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fe55464d-a2c4-4ca6-ace1-e757dbe95e73).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `"Exit Sub"`)|`ExitProcedure`|No resolved target.|

`Exit Sub`, `Exit Function` and `Exit Property` all lower to the
[InstructionKind](../api/RDCore.SDK.Semantics.Instructions.InstructionKind.html) `ExitProcedure`
([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

## Static Semantics

An `Exit Sub` must be inside the body of a subroutine; in a `Function` or in a property it is
[`VBC09332`](../diagnostics/vbc09332.md) (`ExitSubNotAllowedInFunctionOrProperty`). The rule is
[ExitStatementStaticSemantics](../api/RDCore.SDK.Semantics.Static.ExitStatementStaticSemantics.html)'s, applied when the kind of the procedure is known
([**RD-VBAL §5.4.2.18** Exit Function Statement](rd-vbal.5.4.2.18.exit-function-statement.md)).

## Runtime Semantics

The executor dispatches the `ExitProcedure` instruction, which completes the current activation.

Reaching the end of the instruction list completes the activation "as if execution had reached the end of the
body" (the wording of **MS-VBAL §5.4.2.17**). This is the same outcome as an explicit `Exit`
([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

## Implementation

- `RDCore.Runtime.Execution.ProcedureExecutor` dispatches `ExitProcedure` instructions.

---
> ⏮️ [**RD-VBAL §5.4.2.16** On...GoSub Statement](rd-vbal.5.4.2.16.on-gosub-statement.md) | ⏭️ [**RD-VBAL §5.4.2.18** Exit Function Statement](rd-vbal.5.4.2.18.exit-function-statement.md)
