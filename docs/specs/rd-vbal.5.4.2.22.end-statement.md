# 5.4.2.22 End Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.22** End Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/76570466-0d4d-4159-9e84-1baf9d6e6d2f).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `End`)|`Halt`|No resolved target.|

`End` lowers to the [InstructionKind](../api/RDCore.SDK.Semantics.Instructions.InstructionKind.html) `Halt`
([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

## Runtime Semantics

The executor dispatches the `Halt` instruction for `End`
([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

The static execution context retains the state of immediate commands until an `End` command resets the
execution context to its initial state
([**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md)).

## Implementation

- `RDCore.Runtime.Execution.ProcedureExecutor` dispatches `Halt` instructions.
