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

> [!NOTE]
> Reserved. This section has no content yet.

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
|`MidB`, `MidB$`|Bytes of the string's in-memory form, two to a character, as `LenB` counts. A position can fall inside a character, which is split.|

The `$` suffix changes nothing at run time.

## Implementation

|Type or member|Role|
|---|---|
|`RDCore.Runtime.Semantics.Statements.MidStatementRuntimeSemantics`|The runtime semantics of all four spellings (**RDCore.Runtime**).|
|`RDCore.Runtime.Semantics.Statements.StatementRuntimeSemanticsProvider`|Dispatches a `MidStatementNode`; see [**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md).|

---
> ⏮️ [**RD-VBAL §5.4.3.4** Erase Statement](rd-vbal.5.4.3.4.erase-statement.md) | ⏭️ [**RD-VBAL §5.4.3.6** LSet Statement](rd-vbal.5.4.3.6.lset-statement.md)
