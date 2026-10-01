# 3.5.4 Execution

`RDCore.Runtime.Execution.ProcedureExecutor` executes a procedure's instructions in a fetch/decode loop.
`ProcedureExecutor.Run` is that loop: the evaluation engine that "sequentially evaluate[s] each instruction in the
frame" ([**RD-VBAL §2.3.1** Composition Root](rd-vbal.2.3.1.composition-root.md)). The environment host
constructs an [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html) and pushes it
to the evaluation engine, which evaluates each instruction in that frame in turn.

The runtime semantics of each statement are defined on the statement's own page, linked from the dispatch table
below. This page describes the loop, how it dispatches, and the state it keeps per activation.

## The fetch/decode loop

`ProcedureExecutor.Run` is given a session, an activation, and the activation's
[InstructionList](../api/RDCore.SDK.Semantics.Instructions.InstructionList.html). It then:

1. Reads the instruction at the activation's program counter, `ICallStackFrame.Pc`.
2. Dispatches the instruction by its [InstructionKind](../api/RDCore.SDK.Semantics.Instructions.InstructionKind.html)
   (see [Dispatch by instruction kind](#dispatch-by-instruction-kind)).
3. Reacts to what running the instruction produced: it continues at the next offset, branches, or stops.
4. Repeats from step 1.

When the program counter reaches `Items.Length`, execution falls off the end of the instruction list. The
activation then completes "as if execution had reached the end of the body" (the wording of
[**MS-VBAL §5.4.2.17** Exit Sub Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fe55464d-a2c4-4ca6-ace1-e757dbe95e73);
[**RD-VBAL §5.4.2.17** Exit Sub Statement](rd-vbal.5.4.2.17.exit-sub-statement.md)): the same outcome as an
explicit `Exit`.

`ICallStackFrame.Pc` is read-only on the SDK interface. Only the executor mutates it, through the concrete
`CallStackFrame` ([**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md)).

For every `InstructionKind` other than `Simple`, the control effect is already pre-resolved on the
[Instruction](../api/RDCore.SDK.Semantics.Instructions.Instruction.html) itself. The loop decides whether to
branch without any statement semantics needing to know about the program counter; a statement's own runtime
semantics evaluate operands and return a result, and never mutate control state
([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

Whether to refuse to run a body that lowered with errors is a decision for the executor, not for lowering
([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).

## Dispatch by instruction kind

|InstructionKind|Executor action|Semantics|
|---|---|---|
|`Simple`|Dispatches the statement by its own type (see [Simple](#simple)).|The statement's own page (see [Simple](#simple)).|
|`Jump`|Branches unconditionally to `Target`.|[**RD-VBAL §5.4.2.12** GoTo Statement](rd-vbal.5.4.2.12.goto-statement.md)|
|`ExitLoop`|Branches to `Target`, in the same way as `Jump`: the executor uses the same `case` arm for both.|[**RD-VBAL §5.4.2.5** Exit For Statement](rd-vbal.5.4.2.5.exit-for-statement.md), [**RD-VBAL §5.4.2.7** Exit Do Statement](rd-vbal.5.4.2.7.exit-do-statement.md)|
|`JumpTable`|Evaluates the selector through `RDCore.Runtime.Execution.JumpTableEvaluator`, then branches to one of `Targets` or falls through.|[**RD-VBAL §5.4.2.13** On...GoTo Statement](rd-vbal.5.4.2.13.on-goto-statement.md)|
|`GoSubTable`|As `JumpTable`, plus a push onto the GoSub Resumption List on a successful branch.|[**RD-VBAL §5.4.2.16** On...GoSub Statement](rd-vbal.5.4.2.16.on-gosub-statement.md)|
|`GoSub`|Pushes onto the GoSub Resumption List, then branches to `Target`.|[**RD-VBAL §5.4.2.14** GoSub Statement](rd-vbal.5.4.2.14.gosub-statement.md)|
|`Return`|Pops the GoSub Resumption List and branches there.|[**RD-VBAL §5.4.2.15** Return Statement](rd-vbal.5.4.2.15.return-statement.md)|
|`ConditionalBranch`|An `If`/`ElseIf` header, a single-line `If`, or a pre-test loop header: evaluates a Boolean condition (see [ConditionalBranch](#conditionalbranch)). A `Case` header (`Instruction.Matching` set instead of a Boolean condition): matches against the selector that the enclosing `Select` stored as block state, through `RDCore.Runtime.Execution.CaseMatchEvaluator`. Falls through on true or match; otherwise branches to `Else`.|[**RD-VBAL §5.4.2.8** If Statement](rd-vbal.5.4.2.8.if-statement.md), [**RD-VBAL §5.4.2.9** Single-line If Statement](rd-vbal.5.4.2.9.single-line-if-statement.md), [**RD-VBAL §5.4.2.2** While Statement](rd-vbal.5.4.2.2.while-statement.md), [**RD-VBAL §5.4.2.6** Do Statement](rd-vbal.5.4.2.6.do-statement.md), [**RD-VBAL §5.4.2.10** Select Case Statement](rd-vbal.5.4.2.10.select-case-statement.md)|
|`LoopBack`|Evaluates the condition; branches back to `Target` when the loop continues, and falls through when it ends.|[**RD-VBAL §5.4.2.6** Do Statement](rd-vbal.5.4.2.6.do-statement.md)|
|`ForOpener`, `ForNext`|Opens and advances a `For` loop, through the `For` loop state.|[**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md)|
|`ForEachOpener`, `ForEachNext`|Opens and advances a `For Each` loop, through the `For Each` state.|[**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md)|
|`With`|Stores the `With` target as block state, then falls through into the body.|[**RD-VBAL §5.4.2.21** With Statement](rd-vbal.5.4.2.21.with-statement.md)|
|`Select`|Stores the selector as block state, then falls through.|[**RD-VBAL §5.4.2.10** Select Case Statement](rd-vbal.5.4.2.10.select-case-statement.md)|
|`OnErrorGoTo`, `OnErrorDisable`, `OnErrorResumeNext`|Sets the activation's error-handler state.|[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md)|
|`ResumeCurrentStatement`, `ResumeNext`, `ResumeLabel`|Resumes from the activation's active error.|[**RD-VBAL §5.4.4.2** Resume Statement](rd-vbal.5.4.4.2.resume-statement.md)|
|`RaiseError`|Raises a run-time error.|[**RD-VBAL §5.4.4.3** Error Statement](rd-vbal.5.4.4.3.error-statement.md)|
|`ExitProcedure`|Completes the activation.|[**RD-VBAL §5.4.2.17** Exit Sub Statement](rd-vbal.5.4.2.17.exit-sub-statement.md), [**RD-VBAL §5.4.2.18** Exit Function Statement](rd-vbal.5.4.2.18.exit-function-statement.md), [**RD-VBAL §5.4.2.19** Exit Property Statement](rd-vbal.5.4.2.19.exit-property-statement.md)|
|`Halt`|Stops the run (`End`).|[**RD-VBAL §5.4.2.22** End Statement](rd-vbal.5.4.2.22.end-statement.md)|
|`Break`|Stops the run (`Stop`).|[**RD-VBAL §5.4.2.11** Stop Statement](rd-vbal.5.4.2.11.stop-statement.md)|

### Simple

A `Simple` instruction's statement is dispatched by its own C# type through
`RDCore.Runtime.Semantics.Statements.IStatementRuntimeSemanticsProvider`. This is the statement analogue of
`RuntimeExpressionEvaluator`'s expression dispatch. The executor dispatches Let-assignment and Set-assignment
statements, among others, to their statement runtime semantics:

|Statement|Node|Semantics|
|---|---|---|
|Let-assignment|[AssignmentStatementNode](../api/RDCore.SDK.Model.AST.Statements.AssignmentStatementNode.html) (implicit or explicit `Let`)|[**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)|
|Set-assignment|`AssignmentStatementNode` (`Set`)|[**RD-VBAL §5.4.3.9** Set Statement](rd-vbal.5.4.3.9.set-statement.md)|
|`LSet`, `RSet`|`AssignmentStatementNode` (`LSet`, `RSet`)|[**RD-VBAL §5.4.3.6** LSet Statement](rd-vbal.5.4.3.6.lset-statement.md), [**RD-VBAL §5.4.3.7** RSet Statement](rd-vbal.5.4.3.7.rset-statement.md)|
|`Mid`, `Mid$`, `MidB`, `MidB$`|[MidStatementNode](../api/RDCore.SDK.Model.AST.Statements.MidStatementNode.html)|[**RD-VBAL §5.4.3.5** Mid/MidB/Mid$/MidB$ Statement](rd-vbal.5.4.3.5.mid-statement.md)|
|`Call`, bare call|[CallStatementNode](../api/RDCore.SDK.Model.AST.Statements.CallStatementNode.html)|[**RD-VBAL §5.4.2.1** Call Statement](rd-vbal.5.4.2.1.call-statement.md)|
|`Debug.Assert`|[DebugAssertStatementNode](../api/RDCore.SDK.Model.AST.Statements.DebugAssertStatementNode.html)|[**RD-VBAL §5.4.2.23** Assert Statement](rd-vbal.5.4.2.23.assert-statement.md)|
|`Debug.Print`|[DebugPrintStatementNode](../api/RDCore.SDK.Model.AST.Statements.DebugPrintStatementNode.html)|—|
|`Open`|[OpenStatementNode](../api/RDCore.SDK.Model.AST.Statements.OpenStatementNode.html)|[**RD-VBAL §5.4.5.1** Open Statement](rd-vbal.5.4.5.1.open-statement.md)|
|`Close`, `Reset`|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html)|[**RD-VBAL §5.4.5.2** Close and Reset Statements](rd-vbal.5.4.5.2.close-and-reset-statements.md)|
|`Seek`|`KeywordStatementNode`|[**RD-VBAL §5.4.5.3** Seek Statement](rd-vbal.5.4.5.3.seek-statement.md)|
|`Lock`, `Unlock`|[FileLockStatementNode](../api/RDCore.SDK.Model.AST.Statements.FileLockStatementNode.html)|[**RD-VBAL §5.4.5.4** Lock Statement](rd-vbal.5.4.5.4.lock-statement.md), [**RD-VBAL §5.4.5.5** Unlock Statement](rd-vbal.5.4.5.5.unlock-statement.md)|
|`Line Input #`|`KeywordStatementNode`|[**RD-VBAL §5.4.5.6** Line Input Statement](rd-vbal.5.4.5.6.line-input-statement.md)|
|`Width #`|`KeywordStatementNode`|[**RD-VBAL §5.4.5.7** Width Statement](rd-vbal.5.4.5.7.width-statement.md)|
|`Print #`, `Write #`|[PrintStatementNode](../api/RDCore.SDK.Model.AST.Statements.PrintStatementNode.html)|[**RD-VBAL §5.4.5.8** Print Statement](rd-vbal.5.4.5.8.print-statement.md), [**RD-VBAL §5.4.5.9** Write Statement](rd-vbal.5.4.5.9.write-statement.md)|
|`Input #`|`KeywordStatementNode`|[**RD-VBAL §5.4.5.10** Input Statement](rd-vbal.5.4.5.10.input-statement.md)|
|`Put`|`KeywordStatementNode`|[**RD-VBAL §5.4.5.11** Put Statement](rd-vbal.5.4.5.11.put-statement.md)|
|`Get`|`KeywordStatementNode`|[**RD-VBAL §5.4.5.12** Get Statement](rd-vbal.5.4.5.12.get-statement.md)|

A statement that the statement provider does not recognize reports `InternalError`, and the run stops.

`InternalError` is never reported for a language-level condition that a well-formed program meets; such a
condition raises a run-time error ([**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md)).

### ConditionalBranch

A `ConditionalBranch` instruction's condition is forced to `Boolean` by
`RDCore.Runtime.Execution.ConditionEvaluator`
([**MS-VBAL §5.5.1.2.2** Let-coercion to and from Boolean](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3a9f5227-5fd5-4240-949a-51ffc32e71a9);
[**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md)).

`ConditionEvaluator` calls `VBBooleanLetCoercionRuntimeSemantics` directly, rather than through an operator node.
A condition has no operator of its own in source: `If x Then` let-coerces `x` without any `(...)`.

A plain condition's truth test never writes parentheses around the coerced expression, so it does not use the
`"__c()_op"` explicit let-coercion operator
([**RD-VBAL §3.3.1** Unary Operators](rd-vbal.3.3.1.unary-operators.md)). The coercion bypasses the operator
pipeline entirely: the condition passes its own expression through to the Boolean let-coercion strategy, with no
synthetic node standing in for an operator that is not present in source.

`JumpTableEvaluator` coerces the `On…GoTo`/`On…GoSub` selector in the same way: it calls the let-coercion strategy
directly, rather than through the provider.

## Per-activation state

The executor keeps the following state on each activation. Each item is read through the SDK interface
`ICallStackFrame` and written only by the executor, through `CallStackFrame`.

|State|Holds|Keyed by|Read (`ICallStackFrame`)|Written (`CallStackFrame`)|Used by|
|---|---|---|---|---|---|
|Program counter|The offset of the instruction to fetch.|— (one value per activation)|`Pc`|`Pc`|Every instruction.|
|Block state|A single value: the `Select` selector, or the `With` target.|The opener's offset.|`TryGetBlockState`|`SetBlockState`|`Select`, `With`, `Case` headers, with-expressions.|
|`For` loop state|A [ForLoopState](../api/RDCore.SDK.Runtime.Shared.ForLoopState.html): counter symbol, counter expression, end and step.|The `ForOpener`'s offset.|`TryGetForLoopState`|`SetForLoopState`|`ForOpener`, `ForNext`|
|`For Each` state|A [ForEachState](../api/RDCore.SDK.Runtime.Shared.ForEachState.html), an enumeration cursor: control symbol/expression, the array, and a flat index.|The `ForEachOpener`'s offset.|`TryGetForEachState`|`SetForEachState`|`ForEachOpener`, `ForEachNext`|
|GoSub Resumption List|A LIFO stack of return offsets.|— (a plain stack)|`GoSubDepth` (a count)|`PushGoSubReturn`, `TryPopGoSubReturn`|`GoSub`, `GoSubTable`, `Return`|
|Error handler|An [ErrorHandlerState](../api/RDCore.SDK.Runtime.Shared.ErrorHandlerState.html): the error-handling mode, the handler target, the active error, and the fault-statement offset.|— (one value per activation)|`ErrorHandler`|`ErrorHandler`|`On Error`, `Resume`, error interception.|
|Function result|The function result variable, a single slot.|— (one value per activation)|`ReturnValue`|`ReturnValue`|[**RD-VBAL §5.3.1.6** Subroutine and Function Declarations](rd-vbal.5.3.1.6.subroutine-and-function-declarations.md)|

### Block state

`TryGetBlockState` holds a single hidden value per block-opening instruction, keyed by that instruction's offset.
A single value is enough for `With`'s target and `Select Case`'s selector, which are held through
`ICallStackFrame.TryGetBlockState` / `CallStackFrame.SetBlockState`.

Neither `Select` nor `With` has a separate closer instruction to remove the stored state on exit.

### Loop state

A `For` loop's state is more than the single value that `TryGetBlockState` holds: it is a counter symbol, a
counter expression, an end and a step. `ForLoopState` is therefore a second, richer hidden-state mechanism
alongside `TryGetBlockState`, with its own SDK type, `RDCore.SDK.Runtime.Shared.ForLoopState`.

A `For Each` loop's state is an enumeration cursor (control symbol/expression, the array, a flat index), with its
own type, `ForEachState`.

`For` and `For Each` state each get their own parallel `TryGetXState`/`SetXState` pair, rather than extending
`TryGetBlockState`'s single-value shape to hold all three.

### GoSub Resumption List

The GoSub Resumption List is a per-activation LIFO stack of return offsets, manipulated through
`CallStackFrame.PushGoSubReturn` and `CallStackFrame.TryPopGoSubReturn`
([**RD-VBAL §5.4.2.14** GoSub Statement](rd-vbal.5.4.2.14.gosub-statement.md),
[**RD-VBAL §5.4.2.15** Return Statement](rd-vbal.5.4.2.15.return-statement.md)). It is a further per-activation
mechanism alongside the block state (`TryGetBlockState`), the `For` loop state (`TryGetForLoopState`) and the
`For Each` state (`TryGetForEachState`).

Unlike those three, which hold a single value per offset in a per-offset dictionary, the GoSub Resumption List is a
plain stack. Nothing about which `GoSub` pushed an entry matters to `Return`; only the order of entries matters.

`ICallStackFrame` exposes only `GoSubDepth`, a count of the list's entries, and not the push/pop mutators.

### Error handler

`activation.ErrorHandler`, of type `ErrorHandlerState`, is a single mutable value per activation, like `Pc`. It
holds the error-handling mode, the handler target, the active error, and the fault-statement offset.

The error-handler state is not per-offset hidden state the way `With`, `Select`, `For` and `For Each` state is.
An `On Error` statement changes the activation's error-handling policy going forward; it is not scoped to one
block ([**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md)).

## Evaluation context

Every `Simple` and `ConditionalBranch` instruction's `RuntimeEvaluationContext` is computed anew before
dispatch, from `Instruction.EnclosingWith`. `EnclosingWith` is a purely lexical fact about the instruction, not
state carried over from whichever `With` last ran
([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).

With-expression target resolution therefore works however control reached the instruction, including via `GoTo`
([**RD-VBAL §5.6.15** With Expressions](rd-vbal.5.6.15.with-expressions.md)).

`RuntimeProcedureInvoker` sets `RuntimeEvaluationContext.Scope` to the invoked procedure's own `Uri` for the whole
activation ([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).

## Error interception

`On Error` and `Resume` ([**MS-VBAL §5.4.4** Error Handling Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/47b18690-3175-44d9-9de1-31629f0aacc7);
[**RD-VBAL §5.4.4** Error Handling Statements](rd-vbal.5.4.4.error-handling-statements.md)) work by interception,
not by dedicated per-error branch logic:

1. Every executor dispatch that can yield an `Error` outcome routes it through `ProcedureExecutor.InterceptError`,
   before the loop decides whether to stop.
2. `InterceptError` is the one place every run-time error passes through, and the point at which every run-time
   error reaches the session's error state. The line number `Erl` reports and the error's stack trace are captured
   there ([**RD-VBAL §6.1.2.7** Information](rd-vbal.6.1.2.7.information.md),
   [**RD-VBAL §6.1.3.2** Err Class](rd-vbal.6.1.3.2.err-class.md)).
3. When the activation's error handler catches the error, execution continues where the handler says
   ([**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md)). Otherwise the loop stops.

Because every error passes through this point, every runtime error the executor can raise (`TypeMismatch`,
`SubscriptOutOfRange`, `ForLoopNotInitialized`, or any other, from any subsystem) is catchable by an error handler,
with no change needed at any individual error-raising site.

An error that propagates out of an activation (no handler caught it) is returned as the `ProcedureExecutor.Run`
call's own return value. In a called procedure, that value reaches the caller as the result of the call, and
propagates in the caller in the same way as any other runtime error
([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).

## Procedure invocation

A called procedure runs through the same `ProcedureExecutor`, in a new activation. The mechanics are described
on the pages below.

|Topic|See|
|---|---|
|`Call` and bare-call statements|[**RD-VBAL §5.4.2.1** Call Statement](rd-vbal.5.4.2.1.call-statement.md)|
|Invoking a procedure: the callee's frame, `ByVal`/`ByRef` parameter binding (`CallStackFrame.PushByRef`; `CallStackFrame.ReleaseAll` never deallocates the address of a `ByRef` alias), named and `Optional` arguments, `ParamArray`, the call-depth limit|[**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)|
|The function result variable (`ICallStackFrame.ReturnValue`)|[**RD-VBAL §5.3.1.6** Subroutine and Function Declarations](rd-vbal.5.3.1.6.subroutine-and-function-declarations.md)|
|`Static` procedures|[**RD-VBAL §5.3.1.2** Static Procedures](rd-vbal.5.3.1.2.static-procedures.md)|
|Hoisted `Dim` and `Static` locals|[**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md)|
|Local `Const`|[**RD-VBAL §5.4.3.2** Local Constant Declarations](rd-vbal.5.4.3.2.local-constant-declarations.md)|
|A bare name or an index expression that invokes a procedure|[**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md), [**RD-VBAL §5.6.13** Index Expressions](rd-vbal.5.6.13.index-expressions.md)|
|Wiring `RuntimeExpressionEvaluator.ProcedureInvoker`|[**RD-VBAL §2.3.1** Composition Root](rd-vbal.2.3.1.composition-root.md)|
|Zero-size storage|[**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)|
|Array and `Variant` values round-tripping through storage|[**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md), [**RD-VBAL §2.5.2.1.5** Variant Values](rd-vbal.2.5.2.1.5.variant-values.md)|

---
> ⏮️ [**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md) | ⏭️ [**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md)
