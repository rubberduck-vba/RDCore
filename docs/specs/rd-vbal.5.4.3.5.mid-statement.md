# 5.4.3.5 Mid/MidB/Mid$/MidB$ Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.3.5** Mid/MidB/Mid$/MidB$ Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2a8f3567-c8e0-4176-a802-cf2edeba425f).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[MidStatementNode](../api/RDCore.SDK.Model.AST.Statements.MidStatementNode.html)|`Simple`|`Mid`, `Mid$`, `MidB` and `MidB$`. See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md).|

`MidStatementNode` records the statement's spelling in two independent flags: `IsByteMode` distinguishes
`MidB`/`MidB$` from `Mid`/`Mid$`, and `IsStringInput` records the `$` suffix (`Mid$`/`MidB$`).

|Spelling|`IsByteMode`|`IsStringInput`|
|---|---|---|
|`Mid`|`false`|`false`|
|`Mid$`|`false`|`true`|
|`MidB`|`true`|`false`|
|`MidB$`|`true`|`true`|

The replacement-span mechanics of **MS-VBAL §5.4.3.5** split only on byte mode (`MidB`/`MidB$` versus
`Mid`/`Mid$`), never on the `$` suffix. `IsStringInput` is preserved because it mirrors the `VBVariant`/`VBString`
split of the `Mid`/`Mid$` function overloads, which matters for static semantics.

## Static Semantics

The declared type of the target MUST be `String` or `Variant` (**MS-VBAL §5.4.3.5**); another is a `TypeMismatch`. The target is a variable: a literal, an operator's
result, a constant (by its name, or qualified by its project, its module or the library `Enum` that declares it) or a function of the library (`Mid(Left, 1, 1) = "x"`) is
[`VBC09333`](../diagnostics/vbc09333.md) (`VariableRequired`).

The position and the length are used as the runtime semantics uses them - as `Long` - and the value as a `String`, so each has to be Let-coercible to that
([**RD-VBAL §5.5.1** Let-coercion](rd-vbal.5.5.1.let-coercion.md)).

The rule is [MidStatementStaticSemantics](../api/RDCore.SDK.Semantics.Static.MidStatementStaticSemantics.html)'s, which
[StatementStaticSemanticsEvaluator](../api/RDCore.SDK.Semantics.Static.StatementStaticSemanticsEvaluator.html) asks. An operand whose declared type is not known
is deferred, not rejected.

> [!NOTE]
> A `Byte()` target is a run-time possibility the specification's static rule does not allow: it is a `TypeMismatch`.

## Runtime Semantics

`Mid` replaces a span of the characters of a variable with characters of a value. The replacement never changes the length of the
string: the number of characters replaced, `x`, is the least of the `length` asked for, the number of characters left in the target from
`start` on, and the number of characters in the value.

1. The target's value and the value to assign are each Let-coerced to `String` ([**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)).
   A `Variant` is what it holds.
2. `start` and `length` are Let-coerced to `Long`.
3. If `start` is less than or equal to 0, or greater than the length of the target, or if `length` is less than 0, runtime error 5
   (Invalid procedure call or argument) is raised. A target with no characters has no `start` that is in it.
4. The new string is Let-assigned to the target, so the target can be any variable expression a `Let` accepts, and a fixed-length `String`
   target is given back at its own width.

|Spelling|Counts in|
|---|---|
|`Mid`, `Mid$`|Characters.|
|`MidB`, `MidB$`|Bytes of the string's in-memory form, as `LenB` counts them: two to a character, and the trailing byte of a string whose data is an odd number of bytes. A position can fall inside a character, which is split.|

The `$` suffix changes nothing at run time.

Both modes replace the target's own bytes ([**RD-VBAL §2.4.1.2** VBStringType](rd-vbal.2.4.1.intrinsic-types.md#2412-vbstringtype)),
and no text encoding takes part:

- `MidB` copies the first bytes of the value over the target's bytes from byte `start` on. A `start` may be the
  trailing byte, which `LenB` counts. A byte written over half of a character is kept as written, even when the pair
  it makes is a lone surrogate.
- `Mid` copies the first `2x` bytes of the value over the target's bytes from byte `2(start - 1)` on. A trailing byte
  of the target follows its last whole character, so it is not one of the `x` characters replaced, and stays.

With `s` holding the bytes `41 00 42` (the character `A` and a trailing byte):

|Statement|`s` afterwards|
|---|---|
|`MidB(s, 3, 1) = "Z"`|`41 00 5A`|
|`Mid(s, 1, 1) = "Z"`|`5A 00 42`|
|`Mid(s, 1) = "ZQ"`|`5A 00 42`: `s` has one character, so one is replaced.|

> [!NOTE]
> 🎯 **RD-VBAL differs from MS-VBA** in the last row, as the VB6 runtime runs it. That runtime copies bytes up to the
> target's byte length rather than whole characters, and writes `51`, the first byte of `Q`, over the trailing byte.
> **MS-VBAL §5.4.3.5** replaces characters, and a trailing byte is not one, so RD-VBA leaves it. The other rows agree.

## Implementation

|Type or member|Role|
|---|---|
|`RDCore.Runtime.Semantics.Statements.MidStatementRuntimeSemantics`|The runtime semantics of all four spellings (**RDCore.Runtime**).|
|`RDCore.Runtime.Semantics.Statements.StatementRuntimeSemanticsProvider`|Dispatches a `MidStatementNode`; see [**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md).|

---
> ⏮️ [**RD-VBAL §5.4.3.4** Erase Statement](rd-vbal.5.4.3.4.erase-statement.md) | ⏭️ [**RD-VBAL §5.4.3.6** LSet Statement](rd-vbal.5.4.3.6.lset-statement.md)
