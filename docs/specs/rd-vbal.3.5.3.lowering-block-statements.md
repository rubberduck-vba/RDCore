# 3.5.3 Lowering Block Statements

*Lowering* turns a procedure body's statement tree into the InstructionList/Instruction model
([**RD-VBAL §3.5.1** InstructionList](rd-vbal.3.5.1.instructionlist.md),
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).
`InstructionListLowering.Lower` does
it, and returns an `InstructionListLoweringResult`.

Lowering is pure: it uses no symbol resolver and no runtime session
([**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md)). It emits one
instruction per executable statement, plus the synthesized instructions that a block statement needs
but has no source node for.

## Structured blocks

A block statement (`If`, `Select Case`, a loop, `With`) is kept structured; it is not flattened into a
low-level jump IR. Its header(s) remain instructions of their own, addressed by `ByNode` like any other statement.

Only the control effects between block headers are pre-resolved offsets: an `If`/`Case` branch's
fall-through-versus-skip choice, and a loop's back-edge. Lowering computes these offsets once, instead of the
interpreter computing them on every fetch.

Lowering only flattens the statement tree. It never flattens the expression trees held in each statement's
`Inputs`.

## Synthesized closers

A block's closing line (`Next`, `Loop`, `Wend`, `End If`, `End Select`, `End With`, and the procedure's `End Sub`,
`End Function` or `End Property`) has no AST node of its own: the whole construct is one
[ForStatementNode](../api/RDCore.SDK.Model.AST.Statements.ForStatementNode.html),
[DoLoopStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoLoopStatementNode.html) (and so on) with a
`Body` ([**RD-VBAL §3.4.1** Block Statements](rd-vbal.3.4.1.block-statements.md)). Lowering emits a closer
instruction for it, whose `Closes` is that line:

|Block|Closer instruction|Work|`Node`|
|---|---|---|---|
|`For`|`ForNext`|Increments and tests the counter.|`null` (synthesized)|
|`For Each`|`ForEachNext`|Advances to the next element.|`null` (synthesized)|
|`While…Wend`, `Do While`, `Do Until`|`Jump` back to the header|None|`null` (synthesized)|
|`Do…Loop While`, `Do…Loop Until`|`LoopBack`|Evaluates the condition and decides whether to branch back.|The loop's own node: the closer is the loop's only instruction that has one.|
|`Do…Loop`|`Jump` back to the body|None|The loop's own node: the closer is the loop's only instruction that has one.|
|`If`, `Select Case`|`Jump` to the next instruction|None|`null` (synthesized)|
|`With`|`EndWith`|Lets go of the `With` object ([**RD-VBAL §5.4.2.21** With Statement](rd-vbal.5.4.2.21.with-statement.md)).|`null` (synthesized)|
|The procedure|`Jump` to the end of the body|None|`null` (synthesized)|

A synthesized instruction has `Node = null`, so it is never a value in `ByNode`.

A synthesized `Next` closer does not reuse the loop's own node. Doing so would take the loop's `ByNode` entry away
from the opener, which is the more useful attribution for a breakpoint on the `For`/`For Each` line.

Every path through an `If` or a `Select Case` reaches its closer: the trailing jump of each branch, the fall-through
out of the last branch, and a condition or a selector that no branch matches. The header's `End` is the closer's
offset.

`Lower` emits the procedure's closer when it is given the procedure's declaration (`declaration`). A statement run
on its own has no declaration, and its body has no procedure closer. A line label written just before the `End`
statement resolves to the procedure's closer.

A step, a pause and a breakpoint stop before a closer, on its closing line
([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md#where-a-program-stops)).

A `Do…Loop While`, `Do…Loop Until` or `Do…Loop` closer is the loop's own node, which begins on the `Do` line. Its
`Closes` is the `Loop` line, where it is stopped on. Before the body, lowering emits a synthesized `Jump` to the next
instruction, whose `Closes` is the `Do` line. The closer's back edge goes past it, to the body's first instruction, so
the `Do` line is stopped on only on entry.

## Branch trailing jumps

After an `If`/`ElseIf`/`Case` branch's body, lowering always emits a synthesized, unconditional `Jump` to the
construct's closer. It does so even for the last branch, where the jump is redundant with the fall-through.

Always emitting the trailing `Jump` keeps the emission logic uniform, instead of special-casing "is this the last
branch", at the cost of one extra instruction that does not change behaviour.

A trailing jump is on no line: a program goes through it to the closer, on the `End If` or `End Select` line. The
trailing jump of a single-line `If` with an `Else` part is on no line either: a step after the `Then` part goes on to
the next line, as MS-VBA goes.

## Loop exits

`Exit For` and `Exit Do` resolve against the innermost enclosing loop of the matching kind. Lowering sets the
`ExitLoop` instruction's `Target` to the offset right past that loop's closer, so the interpreter needs no
runtime search.

|Statement|Needs an enclosing|
|---|---|
|`Exit For`|`For` or `For Each` loop ([**RD-VBAL §5.4.2.5** Exit For Statement](rd-vbal.5.4.2.5.exit-for-statement.md)).|
|`Exit Do`|Loop of one of the five `Do…Loop` forms ([**RD-VBAL §5.4.2.7** Exit Do Statement](rd-vbal.5.4.2.7.exit-do-statement.md)).|

A `While…Wend` loop satisfies neither `Exit For` nor `Exit Do`: MS-VBAL gives `While…Wend` no exit statement of
its own ([**RD-VBAL §5.4.2.2** While Statement](rd-vbal.5.4.2.2.while-statement.md)). An `Exit Do` written
inside a `While…Wend` is not consumed by it; it resolves against the `Do` loop, if any, that encloses the
`While…Wend`.

An `Exit For` or `Exit Do` that has no enclosing loop of the matching kind, including an `Exit Do` inside a `While…Wend`
that no `Do` loop encloses, is [VBC09313](../diagnostics/vbc09313.md) or [VBC09312](../diagnostics/vbc09312.md), and lowers to no
instruction. An `Exit Sub`, `Exit Function` or `Exit Property` in the wrong kind of procedure is
[VBC09332](../diagnostics/vbc09332.md), [VBC09314](../diagnostics/vbc09314.md) or [VBC09315](../diagnostics/vbc09315.md), and lowers to no instruction
either. The rule is the one `StatementStaticSemanticsEvaluator` asks
(`ExitStatementStaticSemantics`); the kind of procedure is a parameter
of `Lower`, and is not checked when it is not given.

## Procedure exits

`Exit Sub`, `Exit Function` and `Exit Property` lower to `ExitProcedure`, whose `Target` is the procedure's closer.
An `Exit` goes through the `End Sub`, `End Function` or `End Property` line, where a breakpoint or a step stops before
the procedure returns. An `ExitProcedure` in a body lowered without its declaration has no `Target`.

## `EnclosingWith` is static

Every instruction lexically inside a `With` block, however deeply nested (through an `If` or a loop), carries
`EnclosingWith` set to that `With`'s opener offset. When lowering leaves the block, `EnclosingWith` is restored to
its value before the block.

`EnclosingWith` is computed once, at lowering time. It is a purely lexical fact about the instruction, not a
runtime stack the interpreter pushes and pops. Because it is static, a `GoTo` into or out of a `With` block
leaves no stale state to unwind ([**RD-VBAL §5.4.2.21** With Statement](rd-vbal.5.4.2.21.with-statement.md)).

## Dead conditional-compilation branches

A dead `#If`/`#ElseIf`/`#Else` branch is never lowered. `Lower` takes the source ranges that
`RDCore.Runtime.Semantics.Precompiler.PrecompilerLiveBranchEvaluator` found not live, as
`InstructionLoweringOptions.DeadRanges`.

A statement or label lexically inside a dead range, at any depth, is skipped entirely: it gets no instruction,
no `ByNode` entry, and no label definition. The result is the same as if the excluded source were absent, in the
same way that the MS-VBA preprocessor logically removes it before the rest of the language sees it
([**MS-VBAL §3.4.2** Conditional Compilation If Directives](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7fca6481-24cc-4736-9757-f4af90863e26)).

## Labels and diagnostics

Lowering builds the label table that a jump statement's target resolves against, and needs every label to
resolve to a single, unambiguous offset. Lowering alone resolves a jump's target label, for
`GoSub` as for `GoTo`: a `GoSub` statement's `Target`, and an `On…GoSub` statement's `Targets`, hold the
resolved offsets.

|Condition|Lowering result|Diagnostic|
|---|---|---|
|A label operand (`GoTo`, `GoSub`, `On…GoTo`, `On…GoSub`, `On Error GoTo`, `Resume`) names a line label or line number the procedure does not define.|The operand carries a `null` target.|[VBC09309](../diagnostics/vbc09309.md): Label not defined|
|A label is defined more than once.|The first offset the label was defined at is kept; every jump to the label resolves against that first definition.|[VBC09319](../diagnostics/vbc09319.md): Duplicate label definition|
|An `Exit For`/`Exit Do` has no enclosing loop of the matching kind.|No instruction is lowered for it.|[VBC09313](../diagnostics/vbc09313.md): Exit For not within For...Next, [VBC09312](../diagnostics/vbc09312.md): Exit Do not within Do...Loop|
|An `Exit Sub`/`Exit Function`/`Exit Property` is in the wrong kind of procedure.|No instruction is lowered for it.|[VBC09332](../diagnostics/vbc09332.md), [VBC09314](../diagnostics/vbc09314.md), [VBC09315](../diagnostics/vbc09315.md)|

Lowering never fails outright: it always produces a complete `InstructionList`, whether or not every label
resolved and every statement was where it may be.

Whether to refuse to run a body that lowered with errors is a decision for the component that executes the body, not for
lowering ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

## Other statements

Any statement kind that lowering does not give a dedicated shape lowers as `Simple`
([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).
