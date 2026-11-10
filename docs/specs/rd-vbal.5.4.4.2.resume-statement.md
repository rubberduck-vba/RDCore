# 5.4.4.2 Resume Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.4.2** Resume Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/00439540-cf97-451d-9f20-7856d4d98c9b).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[ResumeStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.ResumeStatementNode.html)|`ResumeCurrentStatement`|A bare `Resume` (`LabelExpression` is `null`), or `Resume 0`.|
|`ResumeStatementNode`|`ResumeLabel`|`Resume <label>`. `Target`: the label's offset.|
|[ResumeNextStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.ResumeNextStatementNode.html)|`ResumeNext`|`Resume Next`.|

- `ResumeStatementNode.LabelExpression` is nullable.
- `Resume Next` has its own node type, `ResumeNextStatementNode`. It is not a `ResumeStatementNode` with a "Next"
  label.
- The `0` in `Resume 0` is the same **MS-VBAL §5.4.4.2** sentinel that `On Error GoTo 0` uses; see
  [**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md).

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md) and
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

## Static Semantics

|Condition|Compile error|
|---|---|
|`Resume` names a label that is not defined, e.g. `Resume Retry` with no `Retry:`.|[VBC09309](../diagnostics/vbc09309.md): Label not defined|

To resolve `VBC09309` for an undefined `Resume` label, define the label, or use `Resume Next` to continue after the
statement that failed. `Resume 0` is not a jump, so `VBC09309` is not raised for it.

## Runtime Semantics

`Resume`, `Resume Next` and `Resume <label>` all require an active error. The active error and the offset of the
fault statement are held on the activation's
[ErrorHandlerState](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Shared.ErrorHandlerState.html); see
[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md).

1. If the activation has no active error, run-time error 20, "Resume without error", is raised.
2. Otherwise, the active error is cleared. A `Resume` in the handler an `On Error GoTo` branched to ends it, and the
   policy is the one the handler is to have after it: to go to that label again, unless an `On Error` statement the
   handler ran said otherwise ([**RD-VBAL §5.4.4.1**](rd-vbal.5.4.4.1.on-error-statement.md#in-an-active-handler)).
   An `On Error GoTo -1` ended the handler already: there is no active error, and the `Resume` is error 20.
3. Execution continues where the `Resume` form says:

|Form|Instruction kind|Execution continues at|
|---|---|---|
|Bare `Resume`|`ResumeCurrentStatement`|The fault statement, which is re-executed.|
|`Resume 0`|`ResumeCurrentStatement`|The fault statement, which is re-executed: `Resume 0` behaves as a bare `Resume`.|
|`Resume Next`|`ResumeNext`|The statement past the fault statement: past `End Select`, when the error is in a `Select Case`'s selector.|
|`Resume <label>`|`ResumeLabel`|The label.|

|Condition|Run-time error|
|---|---|
|`Resume`, `Resume Next` or `Resume <label>` executes without an active error.|20 (Resume without error)|

## Implementation

|Type or member|Role|
|---|---|
|`RDCore.Runtime.Execution.ProcedureExecutor`|Dispatches `ResumeCurrentStatement`, `ResumeNext` and `ResumeLabel`.|
|`ErrorHandlerState.ActiveError`, `ErrorHandlerState.FaultStatementOffset`|The active error, and the offset of the fault statement (**RDCore.SDK**).|
|`VBRuntimeErrorId.ResumeWithoutError`|Run-time error 20.|
