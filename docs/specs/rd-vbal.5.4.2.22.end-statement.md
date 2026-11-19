# 5.4.2.22 End Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.22** End Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/76570466-0d4d-4159-9e84-1baf9d6e6d2f).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[KeywordStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `End`)|`Halt`|No resolved target.|

`End` lowers to the `InstructionKind` `Halt`
([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

## Runtime Semantics

The executor dispatches the `Halt` instruction for `End`
([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

The static execution context retains the state of immediate commands until an `End` command resets the
execution context to its initial state
([**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md)).

`End` ends the program where it is, without unwinding the call stack: no `Class_Terminate` runs, for any object. The session is then reset:

- module variables and `Static` locals return to their initial values;
- every object is released;
- every open file is closed.

A program also ends this way when a run-time error reaches no handler, or when an internal error stops it, once the error is reported. An error
that a handler catches unwinds the activations it leaves, and their objects terminate as they go. A program that waits at an error under a debugger
has not ended, and keeps its state until it ends. A line the shell runs is a program of its own: one that ends on an error resets the session, the
variables that earlier lines assigned included.

## Implementation

- `RDCore.Runtime.Execution.ProcedureExecutor` dispatches `Halt` instructions.
- `RDCore.Runtime.Execution.RuntimeProcedureInvoker` leaves the activations of a program that ends as they are.
- `RDCore.Runtime.Execution.SessionWipe` resets the session.
