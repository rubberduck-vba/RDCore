# 5.4.2.14 GoSub Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.14** GoSub Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/492e1f84-c47f-40ef-819f-f1d23e475c91).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[GoSubStatementNode](../api/RDCore.SDK.Model.AST.Statements.GoSubStatementNode.html)|`GoSub`|`Instruction.Target` holds the resolved offset of the target label.|

A `GoSub` statement's target is an expression naming or numbering a label or line. There is no static link
between the `GoSubStatementNode` and the [LineLabelNode](../api/RDCore.SDK.Model.AST.Abstract.LineLabelNode.html)
or [LineNumberNode](../api/RDCore.SDK.Model.AST.Abstract.LineNumberNode.html) it targets
([**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md)).

As for `GoTo`, lowering alone resolves a `GoSub` target label. A `GoSub` statement lowers to
`InstructionKind.GoSub`, whose `Target` holds
the resolved offset ([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md),
[**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).

## Static Semantics

|Condition|Result|
|---|---|
|The procedure defines the target label.|`Target` is the label's offset.|
|The procedure does not define the target label, e.g. `GoSub Done` with no `Done:` line.|The `GoSub` is left unresolved, and lowering reports [VBC09309](../diagnostics/vbc09309.md) *Label not defined* for it, as for `GoTo` ([**RD-VBAL §5.4.2.12** GoTo Statement](rd-vbal.5.4.2.12.goto-statement.md)).|

## Runtime Semantics

`GoSub` and `Return` give each activation its own *GoSub Resumption List*: a per-activation LIFO stack of
return offsets ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

1. The `GoSub` pushes the offset right after itself onto the activation's GoSub Resumption List.
2. Execution branches to the `GoSub`'s target.

A successful `On…GoSub` branch performs the same push
([**RD-VBAL §5.4.2.16** On...GoSub Statement](rd-vbal.5.4.2.16.on-gosub-statement.md)).

Nested `GoSub`s unwind in LIFO order: the innermost `Return` is taken first
([**RD-VBAL §5.4.2.15** Return Statement](rd-vbal.5.4.2.15.return-statement.md)).

Neither `GoSub`'s nor `Return`'s runtime semantics resolve a target label at execution time; lowering resolves the
target.

A label with no statement after it, including the end-label of the lexically enclosing procedure declaration,
resolves to the offset right past the last instruction. A branch to it completes the activation as if execution
had reached the end of the procedure body
([**RD-VBAL §3.5.1** InstructionList](rd-vbal.3.5.1.instructionlist.md)).

## Implementation

- `RDCore.Runtime.Execution.ProcedureExecutor` dispatches `GoSub` instructions.
- The GoSub Resumption List is manipulated through `CallStackFrame.PushGoSubReturn` and
  `CallStackFrame.TryPopGoSubReturn`.
- The GoSub Resumption List is a plain stack, not a per-offset dictionary like the other three activation
  states (block state, `For` loop state, `For Each` state).
- [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.GoSubDepth` exposes the
  number of entries in the GoSub Resumption List, not its top entry. No member exposes the entries themselves
  ([**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md)).
