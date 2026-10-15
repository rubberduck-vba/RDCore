# 5.4.5.9 Write Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.9** Write Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7d516617-3cbc-4cb1-88f7-d64e8e640a07).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[PrintStatementNode](../api/RDCore.SDK.Model.AST.Statements.PrintStatementNode.html)|`Simple`|`Token`: `Write`.|

See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

## Runtime Semantics

`Write #` and `Input #` use one text format: `Write #` writes it, and `Input #` reads it
([**RD-VBAL §5.4.5.10** Input Statement](rd-vbal.5.4.5.10.input-statement.md)).

### Text Format

|Value|Written as|
|---|---|
|`String`|A quoted string.|
|`Boolean` `True`|`#TRUE#`|
|`Null`|`#NULL#`|
|An `Error` value|`#ERROR n#`, where `n` is the error value's number.|
|`Date`|`#yyyy-mm-dd hh:mm:ss#`|

A `Variant` is written as the value it holds, through a `Variant` within a `Variant`: a `Boolean`, `Null`, `Error`,
`Date` or number held in a `Variant` takes the form of its own type above, as **MS-VBAL §5.4.5.9**'s rules are about
an output expression's data value.

The spellings are fixed, whatever the regional settings of the session's environment: a number's decimal separator is
`.`, and a date's separators and year are those above, in the Gregorian calendar. They are the spellings `Input #`
reads back.

## Implementation

`Write #` writes through the channel's character output surface,
[IFileChannelOutput](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannelOutput.html), which it shares with
`Print #`. `Width #` sets that surface's maximum line length
([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).

---
> ⏮️ [**RD-VBAL §5.4.5.8** Print Statement](rd-vbal.5.4.5.8.print-statement.md) | ⏭️ [**RD-VBAL §5.4.5.10** Input Statement](rd-vbal.5.4.5.10.input-statement.md)
