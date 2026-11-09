# 5.4.2.16 On...GoSub Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.16** On...GoSub Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6f44253f-d6ae-4de5-af01-0b1d9a67d24d).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[OnGoSubStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.OnGoSubStatementNode.html)|`GoSubTable`|Lowering resolves the statement's labels into `Instruction.Targets`.|

An `On expression GoSub label, ...` statement is represented by `OnGoSubStatementNode`, and lowers to the
`InstructionKind` `GoSubTable`
([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

`OnGoSubStatementNode` holds its targets in a `Labels` list. Each target is an expression naming or numbering a
label or line, with no static link to the label node: the same convention as `GoSub`
([**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md)).

[OnGoToStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.OnGoToStatementNode.html) and
`OnGoSubStatementNode` are separate node types, rather than one shared node with a `Kind`.

## Static Semantics

Lowering resolves each label in the list into `Instruction.Targets`
([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).

|Condition|Result|
|---|---|
|The procedure defines the label.|Its `Targets` entry is the label's offset.|
|The procedure does not define the label, e.g. `Missing` in `On n GoSub A, Missing`.|Lowering reports [VBC09309](../diagnostics/vbc09309.md) *Label not defined* for it. Each undefined label in the list is reported on its own.|

## Runtime Semantics

`GoSubTable` performs the same selector algorithm as `JumpTable`
([**RD-VBAL §5.4.2.13** On...GoTo Statement](rd-vbal.5.4.2.13.on-goto-statement.md)), and in addition pushes a
resumption point on a successful branch:

1. The selector expression is evaluated once.
2. The evaluated value is Let-coerced to `Integer`. Let *n* be the coerced value.
3. If *n* is zero, or greater than the number of labels, execution falls through to the next instruction
   without branching.
4. Otherwise, if *n* is negative or greater than 255, run-time error 5 is raised.
5. Otherwise, the offset right after the `On…GoSub` is pushed onto the activation's GoSub Resumption List, and
   execution branches to the *n*'th label (1-based) in `Instruction.Targets`.

The push in step 5 is the same push a bare `GoSub` performs
([**RD-VBAL §5.4.2.14** GoSub Statement](rd-vbal.5.4.2.14.gosub-statement.md)). A later `Return` pops it
([**RD-VBAL §5.4.2.15** Return Statement](rd-vbal.5.4.2.15.return-statement.md)).

|Condition|Run-time error|
|---|---|
|*n* is negative, or greater than 255 (step 4).|5 (Invalid procedure call or argument)|

A label with no statement after it, including the end-label of the lexically enclosing procedure declaration,
resolves to the offset right past the last instruction. A branch to it completes the activation as if execution
had reached the end of the procedure body
([**RD-VBAL §3.5.1** InstructionList](rd-vbal.3.5.1.instructionlist.md)).

## Implementation

- `RDCore.Runtime.Execution.ProcedureExecutor` dispatches `GoSubTable` instructions, in the code path it shares
  with `JumpTable`.
- `RDCore.Runtime.Execution.JumpTableEvaluator` evaluates the selector and Let-coerces it to `Integer`
  (steps 1 and 2), by calling `VBNumericLetCoercionTypeRuntimeSemantics` directly, bypassing the let-coercion
  provider.
- The push in step 5 goes through `CallStackFrame.PushGoSubReturn`.
