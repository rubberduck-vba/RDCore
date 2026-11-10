# 5.4.4.1 On Error Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.4.1** On Error Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e2561165-c99a-444b-8bc0-be60a196867a).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[OnErrorGoToStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.OnErrorGoToStatementNode.html)|`OnErrorGoTo`|`On Error GoTo <label>`. `Target`: the label's offset.|
|`OnErrorGoToStatementNode`|`OnErrorDisable`|`On Error GoTo 0`.|
|`OnErrorGoToStatementNode`|`OnErrorReset`|`On Error GoTo -1`, which MS-VBAL does not document and MS-VBA compiles.|
|[OnErrorResumeStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.OnErrorResumeStatementNode.html)|`OnErrorResumeNext`|`On Error Resume Next`.|

- `On Error Resume Next` and `On Error GoTo` are parsed by the same grammar rule, disambiguated by the keyword that
  follows `On Error`.
- The `0` of `On Error GoTo 0` is the same
  [**MS-VBAL §5.4.4.2** Resume Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/00439540-cf97-451d-9f20-7856d4d98c9b)
  sentinel that `Resume 0` uses; see [**RD-VBAL §5.4.4.2** Resume Statement](rd-vbal.5.4.4.2.resume-statement.md).

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md) and
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

## Static Semantics

|Condition|Compile error|
|---|---|
|`On Error GoTo` names a handler label that is not defined, e.g. `On Error GoTo Handler` with no `Handler:`.|[VBC09309](../diagnostics/vbc09309.md): Label not defined|

`On Error GoTo 0` and `On Error GoTo -1` do not refer to a label. The `0` of `On Error GoTo 0` and the `-1` of
`On Error GoTo -1` are never looked up as labels: `VBC09309` is not raised for them, whether or not the procedure
defines a line `0`.

## Runtime Semantics

An `On Error` statement sets the error-handling policy of the current activation. The policy is an
[ErrorHandlingMode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Shared.ErrorHandlingMode.html):

|Policy (`ErrorHandlingMode`)|Set by|When an error is raised|
|---|---|---|
|`Disabled` (the policy [**MS-VBAL §5.4.4** Error Handling Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/47b18690-3175-44d9-9de1-31629f0aacc7) calls *Default*)|`On Error GoTo 0`; an error caught under `GoTo`|The error is not caught: it propagates out of the activation (see [Propagation](#propagation)).|
|`ResumeNext`|`On Error Resume Next`|The error is caught silently. Execution continues at the statement that follows the one that raised the error. The policy is not reset: every later error in the same activation is caught the same way.|
|`GoTo`|`On Error GoTo <label>`; a `Resume` that ends the handler|The error branches to the label. The policy is reset to disabled while the handler runs: a second, unhandled error inside the handler body propagates rather than re-entering the handler. A `Resume`, or an `On Error GoTo -1`, ends the handler and sets the policy it is to have after it: `GoTo` the same label, unless an `On Error` statement in the handler said otherwise ([In an active handler](#in-an-active-handler)).|

The asymmetry between `On Error Resume Next` (policy kept) and `On Error GoTo <label>` (policy reset to disabled
while the handler runs) follows MS-VBAL: the policy is still to go to the label, and an error inside the handler is
not one it handles.

A `Select Case` is one statement, up to its `End Select`: an error in its selector goes on at the statement past
`End Select`, and no `Case` runs, `Case Else` included (MS-VBA). The same holds for `Resume Next`
([**RD-VBAL §5.4.4.2**](rd-vbal.5.4.4.2.resume-statement.md)).

A `For` is not: an error in its start, its end or its step goes on at the first statement of its body, and the loop
keeps nothing the header evaluated ([**RD-VBAL §5.4.2.3**](rd-vbal.5.4.2.3.for-statement.md#an-error-in-the-header-under-on-error-resume-next)).

### In an active handler

A handler is active from the moment an `On Error GoTo` catches an error until a `Resume`, an `On Error GoTo -1`, an `Exit` or the end of the procedure.
An `On Error` statement the handler runs sets the policy for **after** the handler, and nothing more. The error is still the one being handled, which a
`Resume` still resumes, and a second error raised in the handler still propagates to the caller. Each table row is what MS-VBA did, recorded in Excel
(oracle\s53):

|In the handler|A second error in the handler|After `Resume Next`, the next error|
|---|---|---|
|nothing|goes to the caller (A01)|goes to the handler's label again (C05)|
|`On Error GoTo Other`|goes to the caller (A02)|goes to `Other` (C01)|
|`On Error Resume Next`|goes to the caller (A03)|is gone past (C02)|
|`On Error GoTo 0`|goes to the caller (A04)|goes to the caller (C03)|
|`Err.Clear`, then `On Error GoTo Other`|goes to the caller (A07)|not recorded|
|`On Error GoTo -1`|goes to the handler's label again (A05)|`Resume` has nothing to resume: error 20 (C04)|
|`On Error GoTo -1`, then `On Error GoTo Other`|goes to `Other` (A06)|not recorded|
|`On Error GoTo -1`, then `On Error Resume Next`|is gone past (A10)|not recorded|

What follows a second error in the handler doesn't run (D02).

`On Error GoTo -1` ends the error being handled and keeps the policy. In a handler, that policy is the one the handler is to have after it: its own label,
unless an `On Error` in the handler said otherwise. Outside a handler, RD-VBA's `-1` clears the error `On Error Resume Next` went past and keeps the
policy, as it does in one; that case is not recorded in Excel yet. MS-VBAL documents neither `-1` nor what an `On Error` statement does in a handler. Every `On Error` statement clears `Err`, in a handler too (B01-B05).

### Error interception

When a statement raises a run-time error:

1. The executor routes the error through `ProcedureExecutor.InterceptError`, before the loop decides whether to
   stop.
2. `InterceptError` applies the activation's error-handling policy, as the table above shows.
3. When the policy does not catch the error, the error propagates out of the activation.

Every runtime error the executor can raise, from any subsystem, is catchable by an error handler: for example
`TypeMismatch`, `SubscriptOutOfRange` and `ForLoopNotInitialized`. See
[**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md) and
[**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md).

### Propagation

An error that propagates out of an activation (no handler caught it) is returned as the `ProcedureExecutor.Run`
call's own return value.

In a called procedure, that value reaches the caller as the result of the call. A nested call's runtime error
propagates like any other runtime error, so the caller's own error-handling policy applies to it. See
[**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md).

### Per-activation state

`activation.ErrorHandler`, of type [ErrorHandlerState](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Shared.ErrorHandlerState.html), is a
single mutable value per activation, like `Pc`. It holds the error-handling mode, the handler target, the active
error, and the fault-statement offset.

The error-handler state is per activation, not per-offset hidden state the way `With`, `Select`, `For` and
`For Each` state is. An `On Error` statement changes the activation's error-handling policy going forward; it is not
scoped to one block.

## Implementation

|Type or member|Role|
|---|---|
|`RDCore.Runtime.Execution.ProcedureExecutor.InterceptError`|The one point every run-time error passes through; applies the activation's error-handling policy.|
|`ErrorHandlerState`|The activation's error-handling mode, handler target, active error and fault-statement offset; while a handler runs (`IsHandling`), the policy after it (`AfterHandling`, `HandlingOver`) (**RDCore.SDK**).|
|`ErrorHandlingMode`|`Disabled`, `ResumeNext`, `GoTo` (**RDCore.SDK**).|
|[ICallStackFrame](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.ErrorHandler`|Reads the activation's `ErrorHandlerState`; read-only on the SDK interface, and written only by the executor through `CallStackFrame.ErrorHandler`. See [**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md).|
|`InstructionListLowering`|Lowers `On Error GoTo 0` as `OnErrorDisable`, `On Error GoTo -1` as `OnErrorReset`, and `On Error GoTo <label>` as `OnErrorGoTo` (**RDCore.SDK**).|
