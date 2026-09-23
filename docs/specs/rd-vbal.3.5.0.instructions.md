# 3.5.0 Instructions

An *instruction* is the unit a future interpreter's program counter fetches — one per executable
[StatementNode](../api/RDCore.SDK.Model.AST.Abstract.StatementNode.html) of a procedure body (plus a
handful of synthesized instructions a block statement needs but has no source node for), in the order
execution would normally reach them. Where §3.4 catalogs the tree the parser produces, this section
catalogs the flat, offset-addressable
[InstructionList](../api/RDCore.SDK.Semantics.Instructions.InstructionList.html) that
[InstructionListLowering](../api/RDCore.SDK.Semantics.Instructions.InstructionListLowering.html) produces
from it — **MS-VBAL §2.3.1**: "sequentially evaluate each instruction in the frame."

> [!NOTE]
> `GoTo`/`GoSub`/`On…GoTo`/`On…GoSub`/`Resume` can jump to any statement in the procedure, and a recursive
> tree walk cannot express an arbitrary jump. A statement tree is still the right shape for *expressions* —
> they contain no jumps — which is why lowering only ever flattens the *statement* tree, never the
> expression trees each statement's `Inputs` holds.

---
## 3.5.1 InstructionList

An [InstructionList](../api/RDCore.SDK.Semantics.Instructions.InstructionList.html) is built once per
procedure body and is immutable. `Items` is dense: index `i` is offset `i`, and every offset a program
counter can hold names an entry — including `Items.Length` itself, when a label has nothing after it
(execution there completes as if it had reached the end of the body).

Two keys address an instruction without walking `Items`:

|Key|Looks up|Used for|
|---|---|---|
|`Labels` (`TryGetLabelOffset`)|a *line label* or *line number* name → its offset (**MS-VBAL §5.4.1.1**)|resolving a jump's target|
|`ByNode` (`TryGetOffset`)|a statement's `SyntaxNodeId` → its offset|the fault-statement identity a breakpoint or a runtime error anchors to|

Label names are looked up case-insensitively, like every VBA identifier. A label is scoped to the whole
procedure — not the block it is written in, however deeply nested — so a `GoTo` from anywhere in the
procedure into the middle of a loop or `If` body resolves exactly like any other jump (§3.5.3 covers the
consequence this has for a loop's hidden per-activation state). A synthesized instruction (§3.5.3) has no
source node, so it is never a value in `ByNode`.

---
## 3.5.2 Instruction

Every [Instruction](../api/RDCore.SDK.Semantics.Instructions.Instruction.html) carries its `Offset`, the
source `Node` it lowered from (`null` for a synthesized instruction — §3.5.3), and an
[InstructionKind](../api/RDCore.SDK.Semantics.Instructions.InstructionKind.html) that tells the interpreter
which of the resolved-target fields to consult, if any:

|Statement|InstructionKind|Resolved target(s)|MS-VBAL|
|---|---|---|---|
|`GoTo`|`Jump`|`Target`: the label's offset|§5.4.2.12|
|`On expression GoTo label, ...`|`JumpTable`|`Targets`: one offset per label, in source order; a selector out of range falls through at runtime instead of branching|§5.4.2.13|
|`Exit Sub`/`Exit Function`/`Exit Property`|`ExitProcedure`|—|§5.4.2.17/.18/.19|
|`Exit For`/`Exit Do`|`ExitLoop`|`Target`: right past the innermost enclosing loop of the matching kind's closer; unresolved (no diagnostic yet) when lowering finds none|§5.4.2.5/.7|
|`Stop`|`Break`|—|§5.4.2.11|
|`End`|`Halt`|— (not a MS-VBAL-numbered statement)|
|`If`/`ElseIf` header, a `Case` header, a pre-test loop header (`Do While`/`Do Until`/`While…Wend`)|`ConditionalBranch`|`Else`: the offset to go to on false/no-match — the next header in the chain, or right past the whole construct|§5.4.2.2/.3(pre-test)/.8/.10|
|`Do…Loop While`/`Do…Loop Until` closer|`LoopBack`|`Target`: the body's first instruction, taken when the loop continues; falling through ends it|§5.4.2.6|
|`For` opener / `Next` closer|`ForOpener` / `ForNext`|opener's `End`: the `Next`'s offset · `Next`'s `Target`: the body's first instruction|§5.4.2.3|
|`For Each` opener / `Next` closer|`ForEachOpener` / `ForEachNext`|same shape as `For`|§5.4.2.4|
|`With` opener|`With`|`End`: right past the block; every instruction inside carries `EnclosingWith` = this opener's offset|§5.4.2.21|
|`Select Case` opener|`Select`|`End`: right past the block; each `Case` header's `Matching` names this opener's offset|§5.4.2.10|
|everything else, including a synthesized branch's trailing jump and a bare/post-test loop's back-edge|`Simple` / `Jump`|`Jump`'s `Target` when it has one|—|

A `Jump`/`JumpTable` operand that does not resolve to a label the procedure defines carries a `null`
target instead — lowering reports [VBC09309](../diagnostics/vbc09309.html) for it, the same way a repeated
label definition reports [VBC09319](../diagnostics/vbc09319.html), and keeps the first offset the label was
defined at. Lowering never fails outright: it always produces a complete `InstructionList`, whether or not
every label and loop-exit resolved. Whether to refuse to run a body that lowered with errors is a decision
for whatever executes it, not for lowering.

> [!TIP]
> A statement's own runtime semantics stay pure — they evaluate operands and return a result, never mutate
> control state. `InstructionKind` and the pre-resolved offsets on `Instruction` are what let the
> interpreter's fetch/decode loop decide *whether* to branch without the semantics themselves needing to
> know about the program counter at all.

---
## 3.5.3 Block statements

A block statement (`If`/`Select Case`/a loop/`With`) is structured, not flattened into a low-level jump IR
(**D1** in the interpreter plan): its header(s) stay real instructions, addressed by `ByNode` like any
other statement, and only the *control effects between them* — an `If`/`Case` branch's fall-through-vs-skip
choice, a loop's back-edge — are pre-resolved offsets, computed once by lowering instead of on every
fetch.

**A closer with no source node.** MS-VBAL gives `Next`/`Loop`/`Wend` no AST node of their own — the whole
construct is one `ForStatementNode`/`DoLoopStatementNode`/etc. with a `Body`. Where a loop's closer does
real work (a `For`'s `Next` increments and tests the counter; a post-test loop's closer evaluates the
condition and decides whether to branch back), lowering still needs a real instruction to hold that work,
so it synthesizes one with `Node = null` — reusing the loop's own node would silently steal its `ByNode`
entry away from the opener, which is the more useful attribution for a breakpoint on the `For`/`For Each`
line. `If`/`Select Case` need no such synthesized closer at all: falling out of the last branch, or the
`Else`/`Case Else` that needs no condition, already lands exactly where the construct's own `End`/`Else`
chaining says it should, with nothing left to do.

**Every branch ends with a jump.** After an `If`/`ElseIf`/`Case` branch's body runs, lowering always emits
a synthesized, unconditional `Jump` to right past the whole construct — even for the last branch, where it
is redundant with the fall-through that would happen anyway. This keeps the emission logic uniform instead
of special-casing "is this the last branch," at the cost of one harmless extra instruction.

**Loop exits.** `Exit For`/`Exit Do` resolve against the *innermost* enclosing loop of the matching kind —
`Exit For` needs a `For`/`For Each`; `Exit Do` needs one of the five `Do…Loop` forms. `While…Wend` grants
neither: MS-VBAL gives it no exit statement of its own, so an `Exit Do` written inside one is not consumed
by it and resolves against whatever real `Do` loop already encloses it (or is left unresolved, with no
diagnostic, if none does).

**`EnclosingWith` is static.** Every instruction lexically inside a `With` block — however deeply nested,
through an `If` or a loop — carries `EnclosingWith` set to that `With`'s opener offset, restored to
whatever it was before once lowering leaves the block. This is computed once at lowering time, not tracked
as a runtime stack the interpreter pushes and pops: a `GoTo` into or out of a `With` block therefore leaves
no stale state to unwind, because there never was any to begin with.

**A dead `#If`/`#ElseIf`/`#Else` branch is never lowered.** `Lower` takes an optional `deadRanges`
argument — the source ranges
[RDCore.Runtime.Semantics.Precompiler.PrecompilerLiveBranchEvaluator](../api/RDCore.Runtime.Semantics.Precompiler.PrecompilerLiveBranchEvaluator.html)
found not live. A statement or label lexically inside one of those ranges, at any depth, is skipped
entirely: no instruction, no `ByNode` entry, no label definition — exactly as if the excluded source had
never been there, the same way the real MS-VBA preprocessor logically removes it before the rest of the
language ever sees it (**MS-VBAL §3.4.2**).

**Unrecognized statement kinds.** `GoSub`/`Return`/`On…GoSub` and error-handling statements (`On Error`,
`Resume`, `Error`) fall through as `Simple`, the same as any statement kind this pass does not give a
dedicated shape.

---
## 3.5.4 Placement and licensing

`InstructionList`/`Instruction`/`InstructionKind`/`InstructionListLowering` live in **RDCore.SDK** (MIT):
lowering is pure — no symbol resolver, no runtime session — and the SDK's static-analysis consumers
(unreachable code, unused label, a flow-based inspection) want the same flattened list a future
interpreter drives. The interpreter's executor, activation state, and hidden per-loop/per-`Select`/per-`With`
storage are **RDCore.Runtime** (GPLv3).

---
> ⏮️ [**RD-VBAL §3.4** Statements](rd-vbal.3.4.0.statements.html) | ⏭️ [**RD-VBAL §4.0** Program Structure](rd-vbal.4.0.program-structure.html)
