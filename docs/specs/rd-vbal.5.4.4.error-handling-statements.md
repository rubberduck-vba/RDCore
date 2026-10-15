# 5.4.4 Error Handling Statements

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.4** Error Handling Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/47b18690-3175-44d9-9de1-31629f0aacc7).

Each error-handling statement is listed below with the node the parser produces for it, the instruction kind it
lowers to, and the RD-VBAL page that describes its implementation.

|Statement|AST node|Instruction kind|RD-VBAL|
|---|---|---|---|
|`On Error GoTo <label>`|[OnErrorGoToStatementNode](../api/RDCore.SDK.Model.AST.Statements.OnErrorGoToStatementNode.html)|`OnErrorGoTo`|[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md)|
|`On Error GoTo 0`, `On Error GoTo -1`|`OnErrorGoToStatementNode`|`OnErrorDisable`|[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md)|
|`On Error Resume Next`|[OnErrorResumeStatementNode](../api/RDCore.SDK.Model.AST.Statements.OnErrorResumeStatementNode.html)|`OnErrorResumeNext`|[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md)|
|Bare `Resume`, `Resume 0`|[ResumeStatementNode](../api/RDCore.SDK.Model.AST.Statements.ResumeStatementNode.html)|`ResumeCurrentStatement`|[**RD-VBAL §5.4.4.2** Resume Statement](rd-vbal.5.4.4.2.resume-statement.md)|
|`Resume <label>`|`ResumeStatementNode`|`ResumeLabel`|[**RD-VBAL §5.4.4.2** Resume Statement](rd-vbal.5.4.4.2.resume-statement.md)|
|`Resume Next`|[ResumeNextStatementNode](../api/RDCore.SDK.Model.AST.Statements.ResumeNextStatementNode.html)|`ResumeNext`|[**RD-VBAL §5.4.4.2** Resume Statement](rd-vbal.5.4.4.2.resume-statement.md)|
|`Error <number>`|[ErrorStatementNode](../api/RDCore.SDK.Model.AST.Statements.ErrorStatementNode.html)|`RaiseError`|[**RD-VBAL §5.4.4.3** Error Statement](rd-vbal.5.4.4.3.error-statement.md)|

## Error interception

`On Error` and `Resume` work by interception, not by dedicated per-error branch logic. Every executor dispatch that
can yield an error routes it through `ProcedureExecutor.InterceptError`, which applies the activation's
error-handling policy. See [**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md).

Each activation carries a single
[ErrorHandlerState](../api/RDCore.SDK.Runtime.Shared.ErrorHandlerState.html): the error-handling mode, the handler
target, the active error, and the fault-statement offset. The error-handling modes are described in
[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md).

### Where an error is located

An error carries the location of what raised it, and that is where it is reported: the line of a run that ends with
it, where the debugger waits, and `Erl`
([**RD-VBAL §6.1.2.7.1.14** Erl](rd-vbal.6.1.2.7.information.md#6127114-erl)). A standard-library member cannot know where it was called from, so an error it raises, such as `CInt("x")`'s type
mismatch or `Err.Raise`'s error, has no location of its own.

`InterceptError` gives an error that has no location the location of the statement that faulted, before the error
reaches the session's error state and the debugger. That located error is the one every handler state holds and the
one that propagates to the caller, so the error is waited at once, at its own statement, and not again at the
statement that called the procedure. An error that already has a location keeps it: an operator's error is located at
its own expression. The located error takes the statement's start column.

The `Next` of a `For` or `For Each` loop has no statement of its own, so an error there, such as error 92, is located
at the `For` it closes.

## Unhandled errors

An error that no handler catches propagates out of the activation; see
[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md). `Break` mode is entered when an
unhandled run-time error has occurred; see [**RD-VBAL §2.3.2** Mode / State](rd-vbal.2.3.2.mode-state.md).

## The Err object

VBA source reads and calls the error object at run time (`Err.Number`, `Err.Raise 5`): every error that is raised, handled or not, reaches the session's
error state, which is what `Err` reports ([**RD-VBAL §6.1.3.2** Err Class](rd-vbal.6.1.3.2.err-class.md)).

The `Err` class is described in [**RD-VBAL §6.1.3.2** Err Class](rd-vbal.6.1.3.2.err-class.md).

---
> ⏮️ [**RD-VBAL §5.4.3.9** Set Statement](rd-vbal.5.4.3.9.set-statement.md) | ⏭️ [**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md)
