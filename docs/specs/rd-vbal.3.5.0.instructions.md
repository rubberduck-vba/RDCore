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
## 3.5.4 Execution

`RDCore.Runtime.Execution.ProcedureExecutor.Run` is the fetch/decode loop **MS-VBAL §2.3.1** describes
("sequentially evaluate each instruction in the frame"): given a session, an activation, and its
`InstructionList`, it reads the instruction at the activation's `Pc` (`ICallStackFrame.Pc`, read-only on
the SDK interface, mutated only by the executor through the concrete `CallStackFrame`), reacts to what
running it produced, and repeats. A `Simple` instruction's statement is dispatched by its own C# type
through `RDCore.Runtime.Semantics.Statements.IStatementRuntimeSemanticsProvider` — the statement analogue
of `RuntimeExpressionEvaluator`'s expression dispatch — every other `InstructionKind`'s control effect is
already pre-resolved on the `Instruction` itself, so the loop decides *whether* to branch without any
statement semantics needing to know about the program counter at all.

Wired today: `Simple` (dispatches to the statement provider — Let-assignment and Set-assignment are the
statement kinds currently handled; Set-coercion (**MS-VBAL §5.5.2.2**) goes through the same direct entry
point `RDCore.Runtime.Semantics.Statements.WithStatementRuntimeSemantics` already uses for its own
`With`-target coercion, not the operator pipeline Let-assignment reuses — Set-coercion has no
per-destination-type strategy fan-out to need one; anything the provider doesn't recognize reports
`InternalError`, the run stops), `Jump` (unconditional `GoTo`), `ConditionalBranch` for an `If`/`ElseIf`
header or an inline `If` (a Boolean condition) and, separately, for a `Select Case`'s own `Case` header
(`Instruction.Matching` set instead — matches its range clauses against the enclosing `Select`'s own
stashed selector, via `RDCore.Runtime.Execution.CaseMatchEvaluator`), `Select`/`With` (each evaluates its
own header expression once — the selector, the target — and stashes it on the activation keyed by its own
offset via `ICallStackFrame.TryGetBlockState`/`CallStackFrame.SetBlockState`, then falls through into the
body; `With` additionally Set/Let-coerces its target first. Neither has a separate closer instruction to
pop the stash on exit), `ExitProcedure`, `Halt` (`End`), `Break` (`Stop`), and falling off the end of the
list (**MS-VBAL §5.4.2.17**'s "completes as if execution had reached the end of the body" — the same
outcome as an explicit `Exit`). Five of the six loop shapes, too: a pre-test loop (`While…Wend`/`Do
While`/`Do Until`, **MS-VBAL §5.4.2.2**) is just another `ConditionalBranch`, dispatched exactly like an
`If` header; a post-test loop (`Do…Loop While`/`Do…Loop Until`, **§5.4.2.6**) is `LoopBack` — evaluate the
condition, branch back to the body's first instruction (`Instruction.Target`) when the loop continues,
fall through when it ends; a bare `Do…Loop` (**§5.4.2.7**) was already just an unconditional `Jump` back
to its own body, needing no new dispatch at all. None of the three need any new per-activation state —
only a Boolean condition, evaluated by the same `ConditionEvaluator` an `If` uses. The `…Until` half of
each pre-test/post-test pair shares its node shape with the `…While` half (MS-VBAL models them as the same
construct with opposite exit polarity: `…While` exits on `False`, `…Until` exits on `True`) — `GetCondition`
returns a `(Condition, Negate)` pair instead of a bare expression, and the evaluated Boolean is inverted
before branching when `Negate` is true, rather than duplicating the branch logic for a second polarity.
`Exit For`/`Exit Do` (`ExitLoop`) branches to `Instruction.Target` exactly like `Jump` does — literally the
same `case` arm — since lowering has already resolved it to the offset right past the innermost enclosing
loop of the matching kind (or left it `null`, an Exit outside any loop, a static gap with no `VBC` id
wired yet).

A `For` loop (**MS-VBAL §5.4.2.3**) is the first construct needing genuinely new per-activation state:
`ForOpener` evaluates `start-value`, `end-value`, and `step-increment` once, in that order (a missing
`step-clause` defaults to the integer value `1`, never itself evaluated as a source expression), Let-assigns
the counter to `start-value` through the same real Let-assignment machinery a `Let` statement uses, then
stashes `end`/`step` — plus the counter symbol and its own expression node, for every later step to reuse —
as a `RDCore.SDK.Runtime.Shared.ForLoopState`, read back via `ICallStackFrame.TryGetForLoopState`/
`CallStackFrame.SetForLoopState`. This is a *second*, richer hidden-state mechanism alongside
`TryGetBlockState`, exactly as anticipated when `With`/`Select` were wired: a `For` loop's state is more
than the single value that one holds. Steps 1/2 of the algorithm ("if step is zero or positive and the
counter already exceeds end" / "if step is negative and the counter already falls short") are checked
immediately, using the real relational operators
(`RDCore.Runtime.Semantics.Operators.Relational.BinaryGtRelationalOperatorRuntimeSemantics`/
`BinaryLtRelationalOperatorRuntimeSemantics`) rather than a raw numeric comparison — the counter's own
declared type (Currency, Decimal, Date-as-Double, …) has real, spec-mandated comparison semantics a plain
CLR `>`/`<` would get wrong; only `step`'s own sign, which the algorithm frames as a plain classification
rather than a VBA-visible comparison expression, is read directly off its numeric magnitude. Already out of
range → skip straight to `Instruction.End`; otherwise fall through into the body. `ForNext` reads the
stashed state back via `Instruction.Matching` (reusing the same field a `Case` header's own back-reference
to its `Select` uses — the two are never ambiguous, since `InstructionKind` alone picks which
interpretation applies), reads the counter's *current* value (the body may have reassigned it directly —
legal, if unusual, VBA), adds `step` through the real addition operator
(`RDCore.Runtime.Semantics.Operators.Arithmetic.BinaryAdditionOperatorRuntimeSemantics`, MS-VBAL §5.6.9.3 —
a real, overflow-checked operation, not a bare CLR add), Let-assigns the sum back, then re-tests the same
way the opener did: back to the body when still in range, fall through past the loop otherwise. `ForNext`
finding no stashed state for its own `Matching` offset means its `ForOpener` never ran this activation — a
`GoTo` landed directly on the closer — which is **MS-VBAL §5.4.2.3** error 92, "For loop not initialized"
(`VBRuntimeErrorId.ForLoopNotInitialized`, new resx entry `VBForLoopNotInitialized_Verbose`, both
languages). Every location-bearing node passed to any of these operator calls is a real node the loop
already has (the counter's own expression, or, for its very first assignment, the loop's own start
expression) — never a synthetic stand-in, the same discipline the `ExpressionNode` widening this section
already described for `Case` clause matching applies here too.

`For Each` (**MS-VBAL §5.4.2.4**) is wired for arrays, structurally recognizes an object exposing an
enumeration member, and reports a real run-time error for anything else — never `InternalError` for a
well-formed program hitting a genuine language-level condition. `ForEachOpener` evaluates the collection
expression once; when it's a `VBArrayValue`, it Let- or Set-assigns the control variable to the first
element (Set, when the array's own item type is `Object` — **§5.4.2.4**'s own rule) and stashes a
`ForEachState` (control symbol/expression, the array, a flat index) via
`ICallStackFrame.TryGetForEachState`/`CallStackFrame.SetForEachState` — the array's own storage is
already column-major, exactly the traversal order **§5.4.2.4.1** ("Array Enumeration Order") mandates, so
`VBArrayValue.ElementAt(flatIndex)` walks it directly with no per-dimension subscript math. A *declared*
array whose bounds just happen to hold zero elements skips the body entirely (`.End`), per **§5.4.2.4**'s
own "if the array has no elements" wording — but an array that was never initialized at all
(`VBArrayValue.IsInitialized` false: a `Dim` with no bounds, never `ReDim`'d) is a different condition the
spec text doesn't separately call out: real VBA raises error 92, "For loop not initialized" — the
collection itself was never properly set up — rather than silently skipping. `ForEachNext` reads the
state back via `Instruction.Matching`
(the same field `ForNext`/`Case` reuse), advances the index, assigns the next element or falls through
when exhausted — no stashed state at `ForEachNext` is also error 92 (a `GoTo` landing directly on the
closer). A live object whose class
exposes a member with `VB_UserMemId = -4` (commonly `_NewEnum`) is recognized structurally
(`VBReturningMemberSymbol`/`SymbolProperties.UserMemId`/`WellKnownDispIds.NewEnum`, the same lookup
`VBCollectionType`'s own constructor already used) but reports `InternalError`: actually enumerating one
means invoking it and then the COM `IEnumVARIANT`-shaped methods on whatever it returns, which needs real
procedure invocation (S9) that doesn't exist yet. `Nothing` is error 91 (invoking `_NewEnum` on an unset
reference); a live object with no such member is error 438; anything else (a scalar) is error 13,
`TypeMismatch` — MS-VBAL requires the collection to be an array or an enumeration-capable object
reference, so neither is a deferred gap.

Reading a plain array-typed variable back out as an expression (`SimpleNameExpressionNode`, the ordinary
shape of `arr` in `For Each item In arr`) round-trips correctly: `VBArrayType.CreateValue` unboxes the
`VBRuntimeArrayValue` `SymbolAddressTable` boxed for it, handing back the exact same instance — cells
intact — rather than attempting to rebuild one from a bare handle (**RD-VBAL §2.5.2.1.2** has the storage
mechanism). `ProcedureExecutorTests` now proves `For Each item In arr` end to end over a real `Dim`'d
array local for that reason; `LowerForEachOverLiteralCollection` (feeding the collection through a
`LiteralExpressionNode`, reading its `StaticValue` directly) remains only for the empty-array case, which
hits a separate, still-open zero-size-value storage gap unrelated to array storage.

`On…GoTo`/`On…GoSub` (**MS-VBAL §5.4.2.13**/**§5.4.2.16**) share one selector algorithm
(`RDCore.Runtime.Execution.JumpTableEvaluator`): evaluate the selector expression once, Let-coerce it to
`Integer` (calling `VBNumericLetCoercionTypeRuntimeSemantics` directly, the same bypass-the-provider
pattern `ConditionEvaluator` already established for `Boolean`), then branch to the *n*'th label
(1-based) in `Instruction.Targets` — *n* zero or greater than the label count falls through without
branching; *n* negative or greater than 255 is error 5, "Invalid procedure call or argument". `JumpTable`
(`On…GoTo`) was lowered since S1 but never dispatched until this slice, which closes that gap alongside
its closest sibling rather than leaving it dangling a third time. `GoSubTable` (`On…GoSub`) does exactly
the same, plus pushing a resumption point on a successful branch — the identical push a bare `GoSub`
(**MS-VBAL §5.4.2.14**) does.

`GoSub`/`Return` (**MS-VBAL §5.4.2.14**/**§5.4.2.15**) give each activation its own GoSub Resumption
List — a per-activation LIFO stack of return offsets, `CallStackFrame.PushGoSubReturn`/
`TryPopGoSubReturn` (a third such per-activation mechanism, but unlike `TryGetForLoopState`/
`TryGetForEachState`/`TryGetBlockState`'s single-value-per-offset shape, this is a plain stack: nothing
about *which* `GoSub` pushed an entry matters to `Return`, only order does — `ICallStackFrame` exposes
only `GoSubDepth`, a count, not the mutators). A `GoSub` (or a successful
`On…GoSub` branch) pushes the offset right after itself, then branches; `Return` pops the list and
branches there — an empty list is error 3, "Return without GoSub". Nested `GoSub`s unwind correctly in
LIFO order (innermost `Return` first), proven directly by a test. Neither statement's own runtime
semantics resolve a target label at execution time — like `GoTo`, that's entirely lowering's job
(`InstructionKind.GoSub`/`GoSubTable`'s `Target`/`Targets`), so a `GoSub` to an undefined label is left
unresolved with its own `VBC09309` diagnostic, exactly like `GoTo`. Not modeled (a pre-existing gap `GoTo`
already has too, not new to this slice): MS-VBAL's "if the *n*'th label is the same as the end-label of
the lexically enclosing procedure declaration, execution completes as if reaching the end of the body" —
no test exercises a procedure's own implicit end-label today.

A `Case` header's range clauses are matched exactly the way **MS-VBAL §5.4.2.10** phrases its own runtime
semantics — as a real comparison/logical expression, evaluated through the real operator strategies every
other expression uses (`BinaryRelationalOperatorRuntimeSemantics`/`BinaryAndLogicalOperatorRuntimeSemantics`),
never a hand-rolled equality/range check: a value clause (`Case 5`) becomes `selector = 5`; a comparison
clause (`Case Is > 5`) becomes `selector > 5`, reusing the clause's own already-normalized
comparison-operator token to pick the right strategy directly; a `To` clause (`Case 1 To 10`) becomes
`(selector >= 1) And (selector <= 10)`. `CaseMatchEvaluator` calls the specific strategy directly rather
than going through `OperatorRuntimeSemanticsProvider`'s token dispatch — it already knows which one it
wants — passing the clause's own real operand expression (never a fabricated stand-in for the
already-evaluated selector) as the location-bearing node; the already-evaluated selector itself is passed
straight through as an operand *value*, not re-evaluated per clause, matching the spec's own "the
select-expression is immediately evaluated" (once, ahead of every case-clause). This is what motivated
widening `OperatorRuntimeSemantics<TContext,TFlags>`'s own node parameter from `VBOperatorExpression` down
to `ExpressionNode` (mirroring S5c's identical widening of `ILetCoercionRuntimeSemantics`): nothing in the
whole operator hierarchy ever read `.Left`/`.Right`/`.Token` off that parameter — only `.Identity` (for
error attribution) and, at the token-dispatch layer alone (`OperatorRuntimeSemanticsProvider`, deliberately
untouched), `.Token` — so requiring a real binary/unary operator node there was never load-bearing, and
forced exactly the same kind of synthetic-node fabrication S5c had already ruled out for let-coercion. A
`Null` selector short-circuits every `Case` header straight to `Case Else` without evaluating any range
clause at all (**MS-VBAL §5.4.2.10**: "If select-expression is the data value Null, only the
case-else-clause is executed") — not yet exercisable by an automated test, since neither the parser's
`Null` literal keyword nor pushing a raw `VBNullValue` onto a frame work today (separate, pre-existing
gaps; ticketed, not this slice's scope).

A `ConditionalBranch`'s condition is forced to `Boolean` by `RDCore.Runtime.Execution.ConditionEvaluator`
(**MS-VBAL §5.5.1.2.2**), which calls `VBBooleanLetCoercionRuntimeSemantics` directly rather than through
an operator node: a condition has no operator of its own in source (`If x Then` let-coerces `x` without
any `(...)`), and the `"__c()_op"` explicit let-coercion operator (**RD-VBAL §3.3**) is reserved for the
case where source *does* write parentheses around the coerced expression, forcing that frame explicitly —
which a plain condition's truth test never does. Because that bypasses the operator pipeline entirely, the
let-coercion strategy contract (`ILetCoercionRuntimeSemantics.EvaluateLetCoercion`/`.Analyze`) now takes
the coerced value's own `ExpressionNode` rather than a `VBOperatorExpression`: every strategy already used
that node opaquely (identity/location only, for error reporting), so the narrower type was never load-bearing
— a condition simply passes its own expression through with no synthetic node standing in for an operator
that was never there. The same widening let `WithStatementRuntimeSemantics` close its own documented
UDT-target gap: a UDT-valued `With` target now Let-assigns through
`ILetCoercionRuntimeSemanticsProvider.EvaluateLetCoercionSemantics` directly, the same way a class-valued
one already Set-assigned through `ISetCoercionRuntimeSemantics`.

Every `Simple`/`ConditionalBranch` instruction's own `RuntimeEvaluationContext` is recomputed fresh before
dispatch from `Instruction.EnclosingWith` — a purely lexical fact about that instruction, not state carried
over from whichever `With` last ran — so `RuntimeExpressionEvaluator`'s existing `EnclosingWithTarget`
resolution (**MS-VBAL §5.6.15**, wired since S4) picks up the innermost enclosing `With`'s stashed target
for a `.Member`/`!member` with-expression however control reached that instruction, `GoTo` included.

`On Error`/`Resume` (**MS-VBAL §5.4.4**) work by interception, not by dedicated per-error branch logic:
every dispatch above that could yield an `Error` outcome routes it through `ProcedureExecutor`'s own
`InterceptError` before the loop decides whether to actually stop. This means every runtime error the
executor can already raise — `TypeMismatch`, `SubscriptOutOfRange`, `ForLoopNotInitialized`, any of
them, from any subsystem, present or future — became catchable in this one slice, with no changes at any
individual error-raising site. `activation.ErrorHandler` (`ErrorHandlerState`) is a single mutable value
per activation, like `Pc` — not per-offset hidden state the way `With`/`Select`/`For`/`For Each` state is
— since an `On Error` statement changes the activation's policy going forward, it isn't scoped to one
block. `On Error Resume Next` catches an error silently and continues at the statement right after the
one that raised it, *without* resetting the policy — every later error in the same activation is caught
the same way. `On Error GoTo` `<label>` branches there and *does* reset the policy to disabled — a second,
unhandled error inside the handler body itself propagates rather than re-entering it. This asymmetry is
MS-VBAL's own, not a simplification. `On Error GoTo 0` (and the undocumented VBA6/7 `On Error GoTo -1`,
which `OnErrorGoToStatementNode`'s own doc comment already anticipated) both lower as `OnErrorDisable`,
treated identically for now — MS-VBAL doesn't document `-1` at all, so nothing here diverges from spec by
not distinguishing them further. `Resume`/`Resume Next`/`Resume` `<label>` all require an active error
(error 20, "Resume without error", otherwise) and clear it on success: a bare `Resume` (or `Resume 0`,
the same **MS-VBAL §5.4.4.2** sentinel `On Error GoTo 0` uses) re-executes the fault statement, `Resume
Next` continues past it, `Resume` `<label>` branches there. `Error` `<number>` raises a run-time error
directly, "as if `Err.Raise` were invoked" — `RDCore.Runtime.Execution.ErrorHandlingEvaluator` Let-coerces
the number expression to `Integer` the same direct-strategy-call way `ConditionEvaluator` forces a
condition to `Boolean`.

**Deliberately not modeled by this slice:** the `Err` object as something VBA source can read or call
(`Err.Number`, `Err.Raise(...)`) — that needs real member-dispatch machinery, S9's job, the same
prerequisite blocking `_NewEnum` invocation. Also not modeled: a `Default`-policy error actually
propagating to a real *caller* activation and applying *its* policy — no multi-frame call stack exists
yet (S9 again). Today, "propagate" means this `Run` call's own return value; a future caller's `Run`
would see exactly that value from invoking the callee, and could route it through its own
`InterceptError` the same way — the mechanism generalizes for free once S9 exists, it doesn't need
rework.

A `Call`/bare-call statement (**MS-VBAL §5.4.2.1**) and a bare `Sub`/`Function`/`Property Get` reference
invoke through `IProcedureInvoker`: `RDCore.Runtime.Execution.RuntimeProcedureInvoker` looks the callee's
own lowered body up by symbol, pushes a fresh `ICallStackFrame`, runs it through the same
`ProcedureExecutor`, and reports the outcome back — `ExitProcedure` becomes a successful result (the
`Function`/`Property Get`'s own function result variable, or `VBVoidValue` for a `Sub`), an `Error` outcome
becomes a `RuntimeSemanticsEvaluationResult` error the caller's own `ExecuteCall` turns back into an
`Error` outcome, so a nested call's own runtime error propagates exactly like any other. `RuntimeExpressionEvaluator.ProcedureInvoker`/`.LetCoercionProvider` are settable
properties, not constructor parameters: `RuntimeProcedureInvoker` itself needs a `ProcedureExecutor` built
from a `StatementRuntimeSemanticsProvider` built from the SAME evaluator, so the evaluator has to exist
before its own invoker can be built, and gets wired after every other collaborator is composed. An
`IndexExpressionNode` whose `Callee` is a bare name resolving to a `Sub`/`Function`/`Property Get` is
checked for that BEFORE the usual recursive `Evaluate(Callee)` — otherwise a bare-name `Callee` would
already have been auto-invoked with zero arguments by `SimpleName`'s own dispatch, before `Index` ever got
to supply its own; this is also the ONLY shape that recurses (`Foo(n - 1)`, even from within `Foo`'s own
body). `RuntimeCallStack` now enforces a real depth limit (`OnBeforeTryPush`, MS-VBAL error 28, "Out of
stack space") — previously unenforced, since nothing ever pushed a SECOND frame before S9a existed. Found
and fixed a real, previously-unreachable bug along the way: `RuntimeExpressionEvaluator`'s own bare
`SimpleName` dispatch tested `VBReturningMemberSymbol` (a `Function`/`Property Get`'s own base type) to
decide "is this an implicit call" — but `Const`/`EnumConst`/module-and-instance fields/UDT fields all
share that SAME base type, so a plain field read was always wrongly treated as a call attempt. Invisible
until S9a's own tests were the first in the whole suite to read a module-level symbol by bare name; fixed
by checking the two actually-callable leaf types (`VBFunctionMemberSymbol`/`VBPropertyGetMemberSymbol`)
instead of their shared base.

**`ByRef` parameter binding (MS-VBAL §5.3.1.11) and `Function`/`Property Get` return values (§5.3.1).**
A parameter is bound `ByVal` (a fresh, Let-coerced `ValueBindingHandle` — never aliasing the caller's own
storage) UNLESS it is `ByRef` AND `RuntimeExpressionEvaluator`'s own argument-evaluation loop could resolve
the argument to a real, addressable, writable variable whose declared type exactly matches the parameter's
own (or is `Variant`) — the two shapes §5.3.1.11 allows a plain reference binding for without a
class/Object copy-back dance, which isn't modeled yet (an `Object`-typed `ByRef` parameter falls through to
a `ByVal`-style copy today, a documented, narrower-than-spec gap, not a wrong result). When it can, the
argument is passed as a `VBRuntimeReference` (the address itself); `RuntimeProcedureInvoker` binds the
parameter through the NEW `CallStackFrame.PushByRef` — a real name-aliasing binding onto that SAME address
(`ISymbolResolver.TryGetAddress`/`ICallStackFrame.TryGetAddress` resolve it), not a copy, so a write inside
the callee is visible to the caller the instant it happens; `CallStackFrame.ReleaseAll` never deallocates a
`ByRef` alias's address — it belongs to whoever originally allocated it, the callee only ever borrowed it.
Whenever a `ByRef` parameter's own argument isn't recognized as aliasable (an expression, a literal, a
mismatched-declared-type variable, a read-only target), §5.3.1.11's own "otherwise" case applies: the SAME
`ByVal`-style Let-coerced copy, never an error.

Each invocation of a `Function`/`Property Get` gets a fresh function result variable (§5.3.1), modeled as
the NEW `ICallStackFrame.ReturnValue` — a single per-activation slot, read-only on the SDK interface like
`Pc`, mutable on `CallStackFrame`, seeded to the declared return type's own default before the body runs.
A bare reference to the procedure's OWN name, from within its own body, reads or Let-assigns this slot
instead of the general symbol table: `RuntimeExpressionEvaluator.EvaluateSimpleName` and
`StatementRuntimeSemanticsProvider.ExecuteLetAssignment` both detect self-reference the same way —
comparing the resolved symbol's own `Uri` against `RuntimeEvaluationContext.Scope`, which
`RuntimeProcedureInvoker` always sets to that SAME procedure's own `Uri` for the whole activation, the same
identity check `EvaluateInstance` already uses for `Me`. A bare reference to the function result variable
can never go through `"__let_op"`, since it isn't a real addressable `Symbol` with an `IBindingHandle` —
`ExecuteLetAssignment`'s self-reference branch Let-coerces directly instead, the same lower-level call
`ByVal`/`ByRef`-fallback parameter passing already makes for the identical reason. `RuntimeProcedureInvoker`
reads `frame.ReturnValue` back once `ExitProcedure` is reached — however it was reached, an explicit
`Exit Function`/`Exit Property` or falling off the end of the body alike — instead of always reporting
`VBVoidValue`.

**Hoisted locals (MS-VBAL §5.4.3).** Procedure invocation's own step 4 — "create the function result
variable and any procedure extent local variables declared within the procedure" — is now real for
`Dim`/`Static` too, not just the function result variable above. A procedure's own NEW
`VBProcedureMemberSymbol.Locals`/`VBReturningMemberSymbol.Locals` (mirroring `Parameters` exactly, down to
`ScopeTreeBuilder` extracting both the same way — "a procedure's parameters and its own Dim/Static/Const
locals ride on the member symbol, not as separate entries" — so a local never needs a second, flat
registration of its own to resolve by name) lists every `Dim`/`Static`/`Const` declared in the body;
`RuntimeProcedureInvoker.HoistLocals` walks it right after parameter binding, before the body ever runs. A
`Dim` (§5.4.3.1, procedure extent) gets a fresh `CallStackFrame.Push`, seeded to its declared type's own
default, every single call — freed with the rest of the frame when that call returns, so it never sees a
previous call's value. A `Static` (module extent) instead needs storage that OUTLIVES the call: the NEW
`ISymbolResolver.TryAllocate` reserves it in the session's own module-level heap, the same tier a module
field already uses — deliberately NOT through `ISessionSymbols.TryDefine`, whose own bucket-add would
register the SAME symbol a second time (a real ambiguous-name risk, not just redundant work), since
`Locals`-riding already covers the name. Nothing about *reading* a `Static` local needs to change at all:
`CallStackAwareSymbolResolver` already falls through to session-level storage for any Local-scoped symbol
the current frame doesn't itself declare, so the one-time `TryAllocate` (guarded by `TryGetAddress` so only
the FIRST call actually allocates — every later call must see whatever the previous call's own body last
wrote) is the entire mechanism. **Deliberately not modeled by this slice:** a local `Const` — MS-VBAL's own
value is a compile-time substitution, never a runtime address, and nothing yet threads a local `Const`'s
own initializer expression to where the runtime could evaluate it; reading one still resolves to
`InternalError`, the same documented gap as before, not a silent misread. Also not modeled: a whole
procedure declared `Static` (MS-VBAL §5.3.1.2 — every one of ITS OWN locals gets module extent, not just
the ones with an explicit `Static` keyword) — narrower than full spec, not wrong for what it does cover.

**Named arguments and `Optional` parameters (MS-VBAL §5.3.1.11).** `RuntimeExpressionEvaluator.MapArguments`
replaces the previous strict positional 1:1 check with the spec's own two-pass algorithm: positional
arguments map left-to-right, `NamedArgumentNode` maps by name, a `MissingArgumentNode` mapped to a
non-`Optional` parameter is error 448 (checked during mapping, per spec, not folded into the general
error-449 sweep afterwards); an extra positional argument beyond the parameter count is error 450. An
unmapped `Optional` parameter uses the new `VBParameterSymbol.DefaultValue` (pre-computed, `null` when no
`= ...` clause was specified, falling back to the declared type's own default) directly — no Let-coercion,
no reference binding, since there's no caller expression to coerce from or alias.

Comma-omitting an argument (`Foo(1, , 3)`) is legal MS-VBA for a parameter of any declared type — MS-VBA
defers argument-type validation to run-time, it never rejects this at compile time. What actually depends
on the parameter being `Variant` is `IsMissing`: the omitted value is a `VT_ERROR` Variant carrying
`DISP_E_PARAMNOTFOUND`, so `IsMissing` always returns `False` for a non-`Variant` parameter — silently, not
an error — since nothing else can hold that sentinel. RDCore doesn't flag this yet; a design-time diagnostic
for it is planned.

**Deliberately not modeled:** `ParamArray`. Collecting the trailing extra arguments needs a real array
value, but `RuntimeProcedureInvoker`'s `ByVal` push can't pass an array-typed argument at all yet
(`VBArrayType.CreateValue` needs its handle `VBRuntimeArrayValue`-boxed, which it never is) — a pre-existing
gap, not specific to `ParamArray`, that needs closing first. Until then a trailing `ParamArrayParameterSymbol`
isn't special-cased: an extra positional argument still reports 450, and omitting it reports 449 instead of
defaulting to an empty array.

---
## 3.5.5 Placement and licensing

`InstructionList`/`Instruction`/`InstructionKind`/`InstructionListLowering` live in **RDCore.SDK** (MIT):
lowering is pure — no symbol resolver, no runtime session — and the SDK's static-analysis consumers
(unreachable code, unused label, a flow-based inspection) want the same flattened list the interpreter
drives. `ICallStackFrame.Pc`/`ReturnValue`/`TryGetAddress`/`TryGetBlockState`/`TryGetForLoopState`/
`TryGetForEachState`/`GoSubDepth`/`ErrorHandler` are likewise on the SDK interface (read-only there, for a
future debugger surface) but only ever mutated by the executor, through `CallStackFrame.Pc`/`ReturnValue`/
`PushByRef`/`SetBlockState`/`SetForLoopState`/`SetForEachState`/`PushGoSubReturn`/`TryPopGoSubReturn`/
`ErrorHandler`'s own setter — the GoSub Resumption List is a plain stack, not a per-offset dictionary like
the other three, since nothing about *which* `GoSub` pushed an entry matters to `Return`, only order does;
`GoSubDepth` (a count, not a peek) is exposed instead of anything that could look inside it. `TryGetBlockState`
is a single hidden value per block-opening instruction, keyed by that instruction's own offset — enough for
`With`'s target and `Select Case`'s selector. A `For` loop's own state is richer — counter symbol, counter
expression, end, step — so it gets its own SDK type, `RDCore.SDK.Runtime.Shared.ForLoopState`; a `For Each`
loop's is different again — an enumeration cursor (control symbol/expression, the array, a flat index) —
its own `ForEachState`. Each gets its own parallel `TryGetXState`/`SetXState` pair rather than stretching
`TryGetBlockState`'s single-value shape to fit all three. `ErrorHandlerState` (mode, handler target, active
error, fault-statement offset) is different again — like `Pc`, it's a single mutable value per activation
rather than per-offset hidden state, since an `On Error` statement changes the policy going forward rather
than scoping it to one block — so it gets a plain `ErrorHandler { get; }`/`{ get; set; }` property pair,
the same shape `Pc` itself uses, rather than a `TryGetXState`/`SetXState` pair. `ISymbolResolver.TryGetAddress`/
`TryAllocate` are the same read-only(ish)/SDK-interface split applied to name resolution itself:
`CallStackAwareSymbolResolver`/`RuntimeSymbolResolver` (**RDCore.Runtime**) are the only two resolvers with a
real answer for either — `TryAllocate` genuinely mutates (a `Static` local's own storage), unlike every
other SDK-interface member here, because allocating session-level storage was never something only the
executor needed a hook for; every compile-time-only resolver (`CompositeSymbolResolver`,
`ScopeTreeSymbolResolver`, `IntrinsicSymbolResolver`) returns `false` for both, mirroring `TryRead`'s own
existing pattern. `VBProcedureMemberSymbol.Locals`/`VBReturningMemberSymbol.Locals` (**RDCore.SDK**, MIT) —
a `Dim`/`Static`/`Const` local "riding on" its own declaring procedure symbol, exactly like `Parameters`
already does — is what `ScopeTreeBuilder` (also **RDCore.SDK**) extracts for name resolution and what
`RuntimeProcedureInvoker.HoistLocals` (**RDCore.Runtime**) walks for storage; nothing about the STATIC
side's own scope-tree machinery needed to change for the split between the two, only to learn about the
new property. `ProcedureExecutor`, its statement dispatch,
and activation state are **RDCore.Runtime** (GPLv3). `IProcedureInvoker`/`CallableBindingHandle` (the call
*contract*: given a procedure symbol, a resolver, and arguments, run it) predate S9a and live in
**RDCore.SDK** (MIT); `RuntimeProcedureInvoker` (the call's own real implementation — frame setup,
`ByVal`/`ByRef` parameter binding, function result values, the depth guard) is **RDCore.Runtime** (GPLv3),
matching the SDK-contract/Runtime-implementation split every other execution-engine piece already follows.

---
> ⏮️ [**RD-VBAL §3.4** Statements](rd-vbal.3.4.0.statements.html) | ⏭️ [**RD-VBAL §4.0** Program Structure](rd-vbal.4.0.program-structure.html)
