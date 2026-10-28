# 5.4.2.11 Stop Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.11** Stop Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3e8463a8-ee71-4e33-8008-0bd4910e68ea).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `Stop`)|`Break`; `Halt` in a release build|No resolved target.|

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md) and
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

## Runtime Semantics

The executor dispatches `Break` for `Stop`.

An encountered `Stop` is a semantic break: it enters `Break` mode
([**RD-VBAL §2.3.2** Mode / State](rd-vbal.2.3.2.mode-state.md)).

In a release build there is no debugger to break into, and `Stop` ends the program as `End` does
([**RD-VBAL §5.4.2.22** End Statement](rd-vbal.5.4.2.22.end-statement.md)): it lowers to `Halt`, the way a compiled VB6 executable runs it. The statements
after it do not run. A release build is one whose `DEBUG` conditional-compilation constant is
`False`: by default when `Environment.Configuration` is `Release` ([**RD-VBAL §2.3.3** Configuration](rd-vbal.2.3.3.configuration.md#build-configuration)), and
otherwise when the project defines `DEBUG = 0`.

## Implementation

`ProcedureExecutor` dispatches [InstructionKind](../api/RDCore.SDK.Semantics.Instructions.InstructionKind.html)`.Break`, and in a release build
`InstructionKind.Halt` ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

---
> ⏮️ [**RD-VBAL §5.4.2.10** Select Case Statement](rd-vbal.5.4.2.10.select-case-statement.md) | ⏭️ [**RD-VBAL §5.4.2.12** GoTo Statement](rd-vbal.5.4.2.12.goto-statement.md)
