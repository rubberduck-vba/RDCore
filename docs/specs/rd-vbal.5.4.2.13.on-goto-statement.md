# 5.4.2.13 On...GoTo Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.13** On...GoTo Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/371fa3be-b105-4334-8794-a7488107a6f8).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[OnGoToStatementNode](../api/RDCore.SDK.Model.AST.Statements.OnGoToStatementNode.html)|`JumpTable`|[Instruction](../api/RDCore.SDK.Semantics.Instructions.Instruction.html)`.Targets` holds one offset per label, in source order.|

An `On expression GoTo label, ...` statement is represented by `OnGoToStatementNode`, and lowers to the
[InstructionKind](../api/RDCore.SDK.Semantics.Instructions.InstructionKind.html) `JumpTable`
([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

`OnGoToStatementNode` holds its targets in a `Labels` list. Each target is an expression naming or numbering a
label or line, with no static link to the label node: the same convention as `GoTo`
([**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md)).

`OnGoToStatementNode` and [OnGoSubStatementNode](../api/RDCore.SDK.Model.AST.Statements.OnGoSubStatementNode.html)
are separate node types, rather than one shared node with a `Kind`.

## Static Semantics

Lowering resolves each label in the list into `Instruction.Targets`
([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).

|Condition|Result|
|---|---|
|The procedure defines the label.|Its `Targets` entry is the label's offset.|
|The procedure does not define the label, e.g. `Missing` in `On n GoTo A, Missing`.|Its `Targets` entry is `null`, and lowering reports [VBC09309](../diagnostics/vbc09309.md) *Label not defined*. Each undefined label in the list is reported on its own.|

## Runtime Semantics

`On…GoTo` and `On…GoSub` share one selector algorithm
([**RD-VBAL §5.4.2.16** On...GoSub Statement](rd-vbal.5.4.2.16.on-gosub-statement.md)). The executor applies it
to a `JumpTable` instruction as follows:

1. The selector expression is evaluated once.
2. The evaluated value is Let-coerced to `Integer`. Let *n* be the coerced value.
3. If *n* is zero, or greater than the number of labels, execution falls through to the next instruction
   without branching.
4. Otherwise, if *n* is negative or greater than 255, run-time error 5 is raised.
5. Otherwise, execution branches to the *n*'th label (1-based) in `Instruction.Targets`.

Step 3 takes precedence over step 4, in the order **MS-VBAL §5.4.2.13** lists them.

|Condition|Run-time error|
|---|---|
|*n* is negative, or greater than 255 (step 4).|5 — Invalid procedure call or argument|

A label with no statement after it, including the end-label of the lexically enclosing procedure declaration,
resolves to the offset right past the last instruction. A branch to it completes the activation as if execution
had reached the end of the procedure body
([**RD-VBAL §3.5.1** InstructionList](rd-vbal.3.5.1.instructionlist.md)).

## Implementation

- `RDCore.Runtime.Execution.ProcedureExecutor` dispatches `JumpTable` instructions
  ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
- `RDCore.Runtime.Execution.JumpTableEvaluator` evaluates the selector and Let-coerces it to `Integer`
  (steps 1 and 2). `ProcedureExecutor` applies steps 3 to 5, in one code path shared by `JumpTable` and
  `GoSubTable`.
- `JumpTableEvaluator` Let-coerces the selector by calling `VBNumericLetCoercionTypeRuntimeSemantics` directly,
  bypassing the let-coercion provider. `ConditionEvaluator` likewise bypasses the provider to force a condition to
  `Boolean` ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
