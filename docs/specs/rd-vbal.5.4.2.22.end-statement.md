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

### Reset between runs

RD-VBA addition: a session can be reset between two runs of a program, for a host that runs the program once for each request, so that nothing one
run left reaches the next.
The reset sets each module variable and `Static` local that holds an object to its initial value, and an object that loses its last reference runs its
`Class_Terminate`. The session is then reset as `End` resets it, and its current directory goes back to the one it started in
([**RD-VBAL §6.1.2.5** FileSystem](rd-vbal.6.1.2.5.filesystem.md)). A `Class_Terminate` that leaves an error unhandled stops the release there: the
reset reports the error as a run does, and the objects still held go with the session, running no `Class_Terminate`. A program that waits is ended
first. While a program runs, the reset is refused. Objects that hold one another, and that nothing else holds, never lose their last reference,
and run no `Class_Terminate`, as in **MS-VBA**.

## Implementation

- `RDCore.Runtime.Execution.ProcedureExecutor` dispatches `Halt` instructions.
- `RDCore.Runtime.Execution.RuntimeProcedureInvoker` leaves the activations of a program that ends as they are.
- `RDCore.Runtime.Execution.SessionWipe` resets the session, and lets go of what the module variables hold before a reset between runs.
- `RDCore.Runtime.Support.Host.ProgramExecution` runs the reset between runs on the program's thread, in the session's turn.
