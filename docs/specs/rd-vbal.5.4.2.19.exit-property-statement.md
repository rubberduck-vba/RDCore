# 5.4.2.19 Exit Property Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.19** Exit Property Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2c254f33-48b5-40b7-9dc7-4943313a1742).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[KeywordStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `"Exit Property"`)|`ExitProcedure`|`Target`: the `End Property` closer ([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md#procedure-exits)).|

`Exit Property` lowers to the same
`InstructionKind` as `Exit Sub` and
`Exit Function`: `ExitProcedure` ([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

## Static Semantics

An `Exit Property` must be inside the body of a property declaration (`Property Get`, `Property Let` or `Property Set`); in a `Sub` or a `Function` it is
[`VBC09315`](../diagnostics/vbc09315.md) (`ExitPropertyNotAllowedInSubOrFunction`). The rule is
`ExitStatementStaticSemantics`'s, applied when the kind of the procedure is known
([**RD-VBAL §5.4.2.18** Exit Function Statement](rd-vbal.5.4.2.18.exit-function-statement.md)).

## Runtime Semantics

The executor dispatches the `ExitProcedure` instruction, as for `Exit Sub`
([**RD-VBAL §5.4.2.17** Exit Sub Statement](rd-vbal.5.4.2.17.exit-sub-statement.md)).

For a `Property Get`, the function result variable is read back however `ExitProcedure` was reached: an
explicit `Exit Property`, or reaching the end of the body. The value read back is the call's result
([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).

## Implementation

- `RDCore.Runtime.Execution.ProcedureExecutor` dispatches `ExitProcedure` instructions.
- `RDCore.Runtime.Execution.RuntimeProcedureInvoker` reads the function result variable back from
  [ICallStackFrame](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.ReturnValue`.
