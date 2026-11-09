# 3.5.2 Instruction

An [Instruction](../api/RDCore.SDK.Semantics.Instructions.Instruction.html) is one entry of an
[InstructionList](../api/RDCore.SDK.Semantics.Instructions.InstructionList.html)
([**RD-VBAL §3.5.1** InstructionList](rd-vbal.3.5.1.instructionlist.md)).

## Fields

Every instruction carries its `Offset`, the source `Node` it was lowered from, and an
[InstructionKind](../api/RDCore.SDK.Semantics.Instructions.InstructionKind.html). The kind tells the interpreter
which of the resolved-target fields to consult, if any.

|Field|Holds|
|---|---|
|`Offset`|The instruction's own offset: its index in `InstructionList.Items`.|
|`Node`|The [StatementNode](../api/RDCore.SDK.Model.AST.Abstract.StatementNode.html) the instruction was lowered from; `null` for a synthesized instruction ([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).|
|`Kind`|The `InstructionKind`.|
|`Target`|A single resolved offset, for the kinds that branch to one place.|
|`Targets`|One resolved offset per label, in source order, for `JumpTable` and `GoSubTable`.|
|`Else`|For `ConditionalBranch`: the offset to go to on false or no match.|
|`End`|On a block's opening instruction: the offset right past the block.|
|`Matching`|The offset of the block-opening instruction whose state this instruction reads back: a `Case` header names its `Select` opener; a `ForNext` or `ForEachNext` names its own opener.|
|`EnclosingWith`|The offset of the innermost `With` opener lexically enclosing the instruction.|

## Instruction kinds

The table below lists what each statement lowers to. Each linked RD-VBAL §5.4 page implements the MS-VBAL
section of the same number, and defines the statement's runtime semantics.

|Statement|InstructionKind|Resolved target(s)|Semantics|
|---|---|---|---|
|`GoTo`|`Jump`|`Target`: the label's offset.|[**RD-VBAL §5.4.2.12** GoTo Statement](rd-vbal.5.4.2.12.goto-statement.md)|
|`On expression GoTo label, ...`|`JumpTable`|`Targets`: one offset per label, in source order. A selector out of range falls through at runtime instead of branching.|[**RD-VBAL §5.4.2.13** On...GoTo Statement](rd-vbal.5.4.2.13.on-goto-statement.md)|
|`GoSub`|`GoSub`|`Target`: the label's offset.|[**RD-VBAL §5.4.2.14** GoSub Statement](rd-vbal.5.4.2.14.gosub-statement.md)|
|`Return`|`Return`|None|[**RD-VBAL §5.4.2.15** Return Statement](rd-vbal.5.4.2.15.return-statement.md)|
|`On expression GoSub label, ...`|`GoSubTable`|`Targets`: one offset per label, in source order.|[**RD-VBAL §5.4.2.16** On...GoSub Statement](rd-vbal.5.4.2.16.on-gosub-statement.md)|
|`Exit Sub`, `Exit Function`, `Exit Property`|`ExitProcedure`|`Target`: the procedure's closer, on its `End` line. None in a body lowered without its declaration.|[**RD-VBAL §5.4.2.17** Exit Sub Statement](rd-vbal.5.4.2.17.exit-sub-statement.md), [**RD-VBAL §5.4.2.18** Exit Function Statement](rd-vbal.5.4.2.18.exit-function-statement.md), [**RD-VBAL §5.4.2.19** Exit Property Statement](rd-vbal.5.4.2.19.exit-property-statement.md)|
|`Exit For`, `Exit Do`|`ExitLoop`|`Target`: the offset right past the closer of the innermost enclosing loop of the matching kind.|[**RD-VBAL §5.4.2.5** Exit For Statement](rd-vbal.5.4.2.5.exit-for-statement.md), [**RD-VBAL §5.4.2.7** Exit Do Statement](rd-vbal.5.4.2.7.exit-do-statement.md)|
|`Stop`|`Break`|None|[**RD-VBAL §5.4.2.11** Stop Statement](rd-vbal.5.4.2.11.stop-statement.md)|
|`End`|`Halt`|None|[**RD-VBAL §5.4.2.22** End Statement](rd-vbal.5.4.2.22.end-statement.md)|
|`If` or `ElseIf` header; single-line `If`|`ConditionalBranch`|`Else`: the offset to go to on false: the next header in the chain, the first instruction of the `Else` body, or the `End If` closer (for a single-line `If`, right past the statement).|[**RD-VBAL §5.4.2.8** If Statement](rd-vbal.5.4.2.8.if-statement.md), [**RD-VBAL §5.4.2.9** Single-line If Statement](rd-vbal.5.4.2.9.single-line-if-statement.md)|
|`Case` header|`ConditionalBranch`|`Else`: the offset to go to on no match: the next header in the chain, the first instruction of the `Case Else` body, or the `End Select` closer. `Matching`: the offset of its `Select` opener.|[**RD-VBAL §5.4.2.10** Select Case Statement](rd-vbal.5.4.2.10.select-case-statement.md)|
|Pre-test loop header: `While…Wend`, `Do While`, `Do Until`|`ConditionalBranch`|`Else`: right past the whole loop.|[**RD-VBAL §5.4.2.2** While Statement](rd-vbal.5.4.2.2.while-statement.md), [**RD-VBAL §5.4.2.6** Do Statement](rd-vbal.5.4.2.6.do-statement.md)|
|Pre-test loop back-edge (synthesized)|`Jump`|`Target`: the loop's header.|[**RD-VBAL §5.4.2.2** While Statement](rd-vbal.5.4.2.2.while-statement.md), [**RD-VBAL §5.4.2.6** Do Statement](rd-vbal.5.4.2.6.do-statement.md)|
|`Do…Loop While`, `Do…Loop Until` closer|`LoopBack`|`Target`: the body's first instruction, taken when the loop continues. Falling through ends the loop.|[**RD-VBAL §5.4.2.6** Do Statement](rd-vbal.5.4.2.6.do-statement.md)|
|Bare `Do…Loop` back-edge|`Jump`|`Target`: the body's first instruction.|[**RD-VBAL §5.4.2.6** Do Statement](rd-vbal.5.4.2.6.do-statement.md)|
|`For` opener / `Next` closer|`ForOpener` / `ForNext`|Opener's `End`: right past the `Next`. `Next`'s `Target`: the body's first instruction. `Next`'s `Matching`: the opener's offset.|[**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md)|
|`For Each` opener / `Next` closer|`ForEachOpener` / `ForEachNext`|The same shape as `For`.|[**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md)|
|`With` opener|`With`|`End`: right past the block. Every instruction inside the block carries `EnclosingWith` = this opener's offset.|[**RD-VBAL §5.4.2.21** With Statement](rd-vbal.5.4.2.21.with-statement.md)|
|`Select Case` opener|`Select`|`End`: the `End Select` closer. Each `Case` header's `Matching` names this opener's offset.|[**RD-VBAL §5.4.2.10** Select Case Statement](rd-vbal.5.4.2.10.select-case-statement.md)|
|`On Error GoTo label`|`OnErrorGoTo`|`Target`: the label's offset.|[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md)|
|`On Error GoTo 0`, `On Error GoTo -1`|`OnErrorDisable`|None|[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md)|
|`On Error Resume Next`|`OnErrorResumeNext`|None|[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md)|
|`Resume`, `Resume 0`|`ResumeCurrentStatement`|None|[**RD-VBAL §5.4.4.2** Resume Statement](rd-vbal.5.4.4.2.resume-statement.md)|
|`Resume Next`|`ResumeNext`|None|[**RD-VBAL §5.4.4.2** Resume Statement](rd-vbal.5.4.4.2.resume-statement.md)|
|`Resume label`|`ResumeLabel`|`Target`: the label's offset.|[**RD-VBAL §5.4.4.2** Resume Statement](rd-vbal.5.4.4.2.resume-statement.md)|
|`Error number`|`RaiseError`|None|[**RD-VBAL §5.4.4.3** Error Statement](rd-vbal.5.4.4.3.error-statement.md)|
|The trailing jump of an `If`/`ElseIf`/`Else` or `Case`/`Case Else` branch (synthesized)|`Jump`|`Target`: the construct's closer.|[**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)|
|`End If`, `End Select` closer (synthesized)|`Jump`|`Target`: the next instruction.|[**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)|
|`End Sub`, `End Function`, `End Property` closer (synthesized)|`Jump`|`Target`: the end of the body.|[**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)|
|Every other statement|`Simple`|None|[**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md) (dispatch by statement type)|

A label operand that does not resolve, and an `Exit For`/`Exit Do` with no enclosing loop of the matching kind,
leave the target `null`; see [**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md).

## The `Matching` field

A `ForNext` instruction uses the same `Instruction.Matching` field that a `Case` header uses for its
back-reference to its `Select`. The two uses are never ambiguous, because `InstructionKind` alone picks which
interpretation applies.

## Design

> [!TIP]
> A statement's own runtime semantics stay pure: they evaluate operands and return a result, and never mutate
> control state. `InstructionKind` and the pre-resolved offsets on `Instruction` let the interpreter's
> fetch/decode loop decide whether to branch, without the statement semantics needing to know about the
> program counter ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
