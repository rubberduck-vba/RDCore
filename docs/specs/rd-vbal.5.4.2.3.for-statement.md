# 5.4.2.3 For Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.3** For Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/389b1dc4-e608-4ed0-ae64-d88f62f12ea3).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[ForStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.ForStatementNode.html)|`ForOpener`|The `For` opener. Its `End` is the offset right past the `Next` closer.|
|None (synthesized)|`ForNext`|The `Next` closer; `Node` is `null`. Its `Target` is the body's first instruction; its `Matching` is the offset of its `ForOpener`.|

`Next` has no AST node of its own: the whole `For...Next` construct is one `ForStatementNode` with a `Body`
([**RD-VBAL §3.4.1** Block Statements](rd-vbal.3.4.1.block-statements.md)). The instruction fields are described in
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

## Static Semantics

Reassigning a `For` loop counter inside the loop body is legal VBA.

Performance-related diagnostics should be issued when a
[VBCollectionType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.Complex.VBCollectionType.html) is accessed by index within the body
of a `For...Next` loop ([**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md)).

## Runtime Semantics

A `For` loop needs per-activation state. Its opener stores a
[ForLoopState](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Shared.ForLoopState.html) on the activation, and its closer reads it back.

### ForOpener

1. Evaluate `start-value`, `end-value` and `step-increment` once each, in that order. A missing `step-clause`
   defaults to the integer value `1`; that default is never itself evaluated as a source expression.
2. Let-coerce all three to the type the loop counts in (see below): an object to its default member's value, a
   `String` to the number it reads as. A value that is no number raises what Let-coercing it raises: 13, or 94 for
   `Null`.
3. Let-assign the counter to `start-value`, through the same Let-assignment machinery a `Let` statement uses
   ([**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)).
4. Store `end` and `step`, together with the counter symbol and the counter's own expression node, as a
   `ForLoopState`, for every later step to reuse.
5. Check steps 1 and 2 of the **MS-VBAL §5.4.2.3** algorithm immediately: "if step is zero or positive and the
   counter already exceeds end", and "if step is negative and the counter already falls short".
6. When the counter is already out of range, branch to
   `Instruction.End`, right past the loop. Otherwise, fall
   through into the body.

### ForNext

1. Read the stored `ForLoopState` back via `Instruction.Matching`.
2. Read the counter's current value. The body may have reassigned the counter directly.
3. Add `step` to it through the addition operator
   ([**MS-VBAL §5.6.9.3** Arithmetic Operators](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e070115f-8d40-40cf-ac6d-ab18b9c6c906);
   [**RD-VBAL §5.6.9.3** Arithmetic Operators](rd-vbal.5.6.9.3.arithmetic-operators.md)). The addition is an
   overflow-checked operation, not an unchecked CLR addition.
4. Let-assign the sum back to the counter.
5. Re-test the counter in the same way as `ForOpener`: branch back to the body (`Instruction.Target`) while the counter
   is still in range, and fall through past the loop otherwise.

A `Variant` counter that the body sets to `Null` stays `Null`: `Null` plus the step is `Null`
([**MS-VBAL §5.6.9.3**](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e070115f-8d40-40cf-ac6d-ab18b9c6c906)),
and the `Next` assigns it to the counter. The range test then compares `Null` with the end, and the comparison is `Null`
([**MS-VBAL §5.6.9.5**](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f8acd631-55c1-4199-bc1e-022aaab6d9c8)).
That result is Let-coerced to `Boolean`, as the condition of an `If` is, and raises error 94. RD-VBA does what MS-VBA
does, as observed in Excel (64-bit, en-US), counting up or down:

- The `Next` raises the error, after it has assigned the counter `Null`.
- Under `On Error Resume Next`, or a handler's `Resume Next`, execution goes on after the loop.
- With `Erl` counting line-number labels (`ErlLineNumbering` set to `LineLabel`), `Erl` is the `Next`'s line number.

Every error a `Next` raises is located at the `Next`, as MS-VBA locates it. That covers this 94, an overflow when it
steps the counter (6), and a counter that holds no number (13). An overflow leaves the counter as it was, and a `Next`
that closes two loops (`Next j, i`) raises the outer loop's overflow there too. The position the error reports, the line
a program waits at when it waits at the error, and `Erl` are all the `Next`'s, whether `Erl` counts document lines or
line-number labels. Error 92, of a `Next` whose loop was never opened, is located at the `Next` too. An error of the
`start-value`, the `end-value` or the `step-increment` is the `For` statement's, and is located there.

### The type a loop counts in

**MS-VBAL §5.4.2.3** checks that the start, the end and the step are "Let-coercible to Double", then proceeds "using
the original, uncoerced values". Compared as they are, the counter of `For i = 1 To "40"` would never exceed `"40"`: a
number is less than any `String` (**MS-VBAL §5.6.9.5**), and the loop would not end. The values are checked so that
they can be counted with: RD-VBA counts with them as numbers, as MS-VBA does, which counts that loop 40 times (recorded
in Excel, as is the rest of this section).

A counter of a declared type counts in that type, and its start, end and step are Let-coerced to it:
`For k = 1 To 2.6` counts to 3 with an `Integer` `k`, and `Step 0.5` is a step of 0.

A `Variant` counter counts in the widest type of its start, end and step:

|Rank|Type|Of|
|---|---|---|
|1|`Byte`|`Byte`; and of a loop whose start, end and step are all `Empty`|
|2|`Integer`|`Integer`, `Boolean`|
|3|`Long`|`Long`|
|4|`LongLong`|`LongLong`|
|5|`Double`|`Single`, `Double`, `String`|
|6|`Currency`|`Currency`|
|7|`Date` or `Decimal`|`Date`, `Decimal`: of a tie between the two, the end's type wins over the start's, and the step's over neither|

`Empty` has no rank, and nor has the step `1` of a loop that writes none: `For i = CByte(1) To CByte(3)` counts in
`Byte`. A loop that counts in `Date` reads a `String` as a date only, not as the number a Let-coercion to `Date` would
also read it as: `"1/1/1900"` is a start, `"1"` is error 13.

### Range Test

The range checks of `ForOpener` and `ForNext` use the relational operators `>` and `<`
([**MS-VBAL §5.6.9.5** Relational Operators](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f8acd631-55c1-4199-bc1e-022aaab6d9c8);
[**RD-VBAL §5.6.9.5** Relational Operators](rd-vbal.5.6.9.5.relational-operators.md)), not a raw numeric
comparison. The counter's declared type (`Currency`, `Decimal`, `Date` as `Double`, …) has comparison semantics
specified by MS-VBAL, which a CLR `>` or `<` comparison does not implement.

The counter, the end and the step are of the one type the loop counts in, so the comparison is between numbers of a
type. Only the sign of `step` is read directly from its numeric value, once, when the loop starts
(`ForLoopState.CountsDown`). The **MS-VBAL §5.4.2.3** algorithm treats the step's sign as a classification, not as a
VBA-visible comparison expression.

|Step|Out of range when|
|---|---|
|Zero or positive|The counter exceeds `end` (`counter > end`).|
|Negative|The counter falls short of `end` (`counter < end`).|

### Errors

A label is scoped to the whole procedure, so a `GoTo` can land directly on the `Next` closer
([**RD-VBAL §5.4.1.1** Statement Labels](rd-vbal.5.4.1.1.statement-labels.md)). When `ForNext` finds no stored
state for its `Matching` offset, its `ForOpener` never ran in this activation.

|Condition|Run-time error|
|---|---|
|`ForNext` finds no stored `ForLoopState` for its `Matching` offset (a `GoTo` landed directly on the closer).|92 (For loop not initialized)|
|The addition of `step` to the counter overflows.|6 (Overflow)|
|The start, the end or the step is no number: `"abc"`, an `Error`, an array, a `String` that is no date in a loop that counts in `Date`.|13 (Type mismatch)|
|The start, the end or the step is `Null`.|94 (Invalid use of Null)|
|The counter is a `Variant` that holds `Null` when `Next` tests it.|94 (Invalid use of Null)|
|The start, the end or the step is out of the range of the type the loop counts in (`For b = 1 To 300` with a `Byte` `b`).|6 (Overflow)|

Run-time error 92 is `VBRuntimeErrorId.ForLoopNotInitialized`
([**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md)).

### An error in the header, under `On Error Resume Next`

MS-VBAL does not say what a loop is when its start, end or step raises an error that `On Error Resume Next` goes past
([**RD-VBAL §5.4.4.1**](rd-vbal.5.4.4.1.on-error-statement.md)). RD-VBA does what MS-VBA does, recorded in Excel
(oracle\s54 F01-F07, X01-X04):

- The statement that follows the `For` is the first of its body, and it runs: the counter is not tested against the end.
- Nothing the header evaluated is kept. The counter is not given the start, and neither the end nor the step is stored,
  even one evaluated before the error.
- The loop keeps the end and the step it had: what an earlier pass through the same `For` stored, in the same
  activation. With none, the end is 0, and so is the step of a loop that writes a `Step` clause; a loop that writes
  none steps by 1.
- Its `Next` steps the counter and tests it with that end and that step, as it does any other loop's.

`For i = 1 To 10 \ n`, with `n` 0 on a first pass, runs its body with `i` as it was, and steps it by 1 until it exceeds
0: once, for an `i` of 0 or more. A faulted
`Step` leaves a step of 0: the loop runs until something in its body leaves it.

## Implementation

- `ProcedureExecutor` dispatches `ForOpener` and `ForNext`.
- The `Next` closer increments and tests the counter, so lowering synthesizes an instruction (`Node = null`) to hold
  that work ([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).
- `ForOpener` stores `end`, `step`, the counter symbol and the counter's own expression node as a
  `RDCore.SDK.Runtime.Shared.ForLoopState`. The state is read back via
  [ICallStackFrame](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.TryGetForLoopState`, and
  written via `CallStackFrame.SetForLoopState`.
- A `For` loop's state holds more than a single value: counter symbol, counter expression, end and step. It therefore
  has its own SDK type, `ForLoopState`, rather than the single block-state value that `With` and `Select Case` use
  ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
- `ForNext` uses the same `Instruction.Matching` field that a `Case` header uses for its back-reference to its
  `Select` ([**RD-VBAL §5.4.2.10** Select Case Statement](rd-vbal.5.4.2.10.select-case-statement.md)).
- The range test uses `RDCore.Runtime.Semantics.Operators.Relational.BinaryGtRelationalOperatorRuntimeSemantics` and
  `BinaryLtRelationalOperatorRuntimeSemantics`. The increment uses
  `RDCore.Runtime.Semantics.Operators.Arithmetic.BinaryAdditionOperatorRuntimeSemantics`.
- Every location-bearing node passed to the loop's operator calls is a node the loop already has, never a synthesized
  one: the counter's own expression or, for the first assignment, the loop's start expression.
  The same rule applies to `Case` clause matching, which relies on the operator node parameter being typed
  [ExpressionNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Abstract.ExpressionNode.html)
  ([**RD-VBAL §5.4.2.10** Select Case Statement](rd-vbal.5.4.2.10.select-case-statement.md)).
- The verbose message of `ForLoopNotInitialized`, when a `Next` closer runs without its opener, is the resx entry
  `VBForLoopNotInitialized_Verbose`, provided in both languages.
