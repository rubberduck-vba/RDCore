# 5.4.2.18 Exit Function Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.18** Exit Function Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d70e6f6f-b830-4be2-acce-aa491c8acb5a).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `"Exit Function"`)|`ExitProcedure`|No resolved target.|

`Exit Function` lowers to the same
[InstructionKind](../api/RDCore.SDK.Semantics.Instructions.InstructionKind.html) as `Exit Sub` and
`Exit Property`: `ExitProcedure` ([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

## Static Semantics

An `Exit Function` must be inside the body of a function; in a `Sub`, or in a `Property Let` or a `Property Set`, it is
[`VBC09314`](../diagnostics/vbc09314.md) (`ExitFunctionNotAllowedInSubOrProperty`).

> [!NOTE]
> **MS-VBAL §5.4.2.18** says the statement must be in a function, and the platform also accepts it in a `Property Get`, as MS-VBA does.
> The rule is [ExitStatementStaticSemantics](../api/RDCore.SDK.Semantics.Static.ExitStatementStaticSemantics.html)'s. It is applied when the
> kind of the procedure is known: [StatementStaticSemanticsEvaluator](../api/RDCore.SDK.Semantics.Static.StatementStaticSemanticsEvaluator.html)
> and instruction-list lowering are each told it, and check nothing of the kind without it.

## Runtime Semantics

The executor dispatches the `ExitProcedure` instruction, as for `Exit Sub`
([**RD-VBAL §5.4.2.17** Exit Sub Statement](rd-vbal.5.4.2.17.exit-sub-statement.md)).

The function result variable is read back however `ExitProcedure` was reached: an explicit `Exit Function`, or
reaching the end of the body. The value read back is the call's result
([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).

## Implementation

- `RDCore.Runtime.Execution.ProcedureExecutor` dispatches `ExitProcedure` instructions.
- `RDCore.Runtime.Execution.RuntimeProcedureInvoker` reads the function result variable back from
  [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.ReturnValue`.

---
> ⏮️ [**RD-VBAL §5.4.2.17** Exit Sub Statement](rd-vbal.5.4.2.17.exit-sub-statement.md) | ⏭️ [**RD-VBAL §5.4.2.19** Exit Property Statement](rd-vbal.5.4.2.19.exit-property-statement.md)
