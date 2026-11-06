# 5.4.2.15 Return Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.15** Return Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4cc2aabb-5940-4abb-ad6b-953bd5631073).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[ReturnStatementNode](../api/RDCore.SDK.Model.AST.Statements.ReturnStatementNode.html)|`Return`|No resolved target.|

`Return` lowers to the [InstructionKind](../api/RDCore.SDK.Semantics.Instructions.InstructionKind.html)
`Return` ([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

## Runtime Semantics

`Return` reads the activation's *GoSub Resumption List*, the per-activation LIFO stack of return offsets that
`GoSub` and a successful `On…GoSub` branch push onto
([**RD-VBAL §5.4.2.14** GoSub Statement](rd-vbal.5.4.2.14.gosub-statement.md)).

1. `Return` pops the GoSub Resumption List.
2. Execution branches to the popped offset.

|Condition|Run-time error|
|---|---|
|The GoSub Resumption List is empty.|3 — Return without GoSub|

Nested `GoSub`s unwind in LIFO order: the innermost `Return` is taken first.

`Return`'s runtime semantics resolve no target label at execution time: the offset it branches to is the one
the matching `GoSub` pushed.

The GoSub Resumption List is a plain stack because `Return` depends only on the order of its entries, not on which
`GoSub` pushed each one.

## Implementation

- `RDCore.Runtime.Execution.ProcedureExecutor` dispatches `Return` instructions.
- `Return` pops the list through `CallStackFrame.TryPopGoSubReturn`
  ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
