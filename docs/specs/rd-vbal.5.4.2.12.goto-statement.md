# 5.4.2.12 GoTo Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.12** GoTo Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9ce18dd2-6864-426e-aec3-fd30518024a8).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[GoToStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.GoToStatementNode.html)|`Jump`|`Instruction.Target` holds the offset of the target label.|

A `GoTo` statement's target is an expression naming or numbering a label or line. There is no static link
between the `GoToStatementNode` and the [LineLabelNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Abstract.LineLabelNode.html)
or [LineNumberNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Abstract.LineNumberNode.html) it targets
([**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md)).

A bare line-number target in a single-line `If` branch (`If x Then 100`) is not modelled as its own shape. The
parser synthesizes a `GoToStatementNode` as that branch's statement, typically its only one
([**RD-VBAL §5.4.2.9** Single-line If Statement](rd-vbal.5.4.2.9.single-line-if-statement.md)).

`GoTo` lowers to the `InstructionKind` `Jump`
([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

## Static Semantics

Lowering resolves the target label to an offset
([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)). A label is scoped
to the whole procedure, so a `GoTo` from anywhere in the procedure into the middle of a loop body or an `If`
body resolves in the same way as any other jump
([**RD-VBAL §5.4.1.1** Statement Labels](rd-vbal.5.4.1.1.statement-labels.md)).

|Condition|Result|
|---|---|
|The procedure defines the target label.|`Target` is the label's offset.|
|The procedure does not define the target label, e.g. `GoTo Done` with no `Done:` line.|`Target` is `null`, and lowering reports [VBC09309](../diagnostics/vbc09309.md) *Label not defined*.|

## Runtime Semantics

1. The executor dispatches a `Jump` instruction as an unconditional `GoTo`: execution branches to
   `Instruction.Target`, the offset of the target label.
2. A label with no statement after it, including the end-label of the lexically enclosing procedure
   declaration, resolves to the offset right past the last instruction. A branch to it completes the
   activation as if execution had reached the end of the procedure body
   ([**RD-VBAL §3.5.1** InstructionList](rd-vbal.3.5.1.instructionlist.md)).

An instruction's `EnclosingWith` is computed once, at lowering time; it is not tracked as a runtime stack the
interpreter pushes and pops. A `GoTo` into or out of a `With` block therefore leaves no stale state to unwind
([**RD-VBAL §5.4.2.21** With Statement](rd-vbal.5.4.2.21.with-statement.md)).

A `GoTo` that lands directly on the `Next` of a `For` or `For Each` loop whose opener has not run in the
activation raises run-time error 92, "For loop not initialized"; see
[**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md) and
[**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md).

## Implementation

- `RDCore.Runtime.Execution.ProcedureExecutor` dispatches `Jump` instructions
  ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
- `InstructionListLowering` resolves the
  target label into `Instruction.Target`.
