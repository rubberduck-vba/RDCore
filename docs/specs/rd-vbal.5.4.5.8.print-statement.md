# 5.4.5.8 Print Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.8** Print Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6f427c3d-2471-4cd8-8cae-2e2951918b51).

## Syntax

|Form|AST node|Instruction kind(s)|Notes|
|---|---|---|---|
|`Print #`|[PrintStatementNode](../api/RDCore.SDK.Model.AST.Statements.PrintStatementNode.html)|`Simple`|The file-number form. `Token`: `Print`.|
|`Print`|[PrintStatementNode](../api/RDCore.SDK.Model.AST.Statements.PrintStatementNode.html)|`Simple`|The bare form, with no file number (`FileNumber` is `null`): `Print "x"`. See below.|
|`Owner.Print`|[ObjectPrintExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.ObjectPrintExpressionNode.html)|—|The object-qualified form.|
|`Debug.Print`|[DebugPrintStatementNode](../api/RDCore.SDK.Model.AST.Statements.DebugPrintStatementNode.html)|`Simple`|The object-qualified form whose owner is `Debug`.|

The output list these nodes carry is described in [5.4.5.8.1 Output Lists](#54581-output-lists) below. See
[**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

### The bare `Print`

In VB6 a `Print` with no file number is the `Print` member of the form or report it is written in. The platform has no forms, so the bare form writes to the
session's own output - the one `Debug.Print` writes to - by the same output rules ([5.4.5.8.1](#54581-output-lists)). It differs from `Debug.Print` in one
way: it is not a debug statement, and is lowered in a release build, where `Debug.Print` leaves no instruction at all
([**RD-VBAL §3.5.1** InstructionList](rd-vbal.3.5.1.instructionlist.md)). It is what an interactive shell writes its output with: the shell's `?` shorthand expands
to it.

> [!NOTE]
> **Not implemented.** A document module (a form, a report) with a `Print` member of its own is not yet the target of a bare `Print` written in it. Whether a
> module that is neither may use the bare form at all is a matter for the dialects - VB6 does not, BASIC does - which the language server does not tell apart yet.
>
> The `?` shorthand has no lexer or grammar token: the shell expands it before the line is parsed.

## Static Semantics

> [!NOTE]
> Reserved. This section has no content yet.

## Runtime Semantics

> [!NOTE]
> Reserved. This section has no content yet.

## Implementation

`Print #` writes through the channel's character output surface,
[IFileChannelOutput](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannelOutput.html), which it shares with
`Write #`. `Width #` sets that surface's maximum line length
([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).

## 5.4.5.8.1 Output Lists

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.8.1** Output Lists](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/630ce2fe-abf1-4aae-a126-1c6567ac2a41).

`PrintStatementNode` and `ObjectPrintExpressionNode` share one output-list shape:

|Node|Represents|
|---|---|
|[PrintOutputItemNode](../api/RDCore.SDK.Model.AST.Expressions.PrintOutputItemNode.html)|An output item: a value and/or a `;` or `,` separator.|
|[PrintSpcClauseNode](../api/RDCore.SDK.Model.AST.Expressions.PrintSpcClauseNode.html)|A `Spc` clause.|
|[PrintTabClauseNode](../api/RDCore.SDK.Model.AST.Expressions.PrintTabClauseNode.html)|A `Tab` clause.|

> [!TIP]
> In a parsed output list, a value and its trailing separator are always two separate, alternating
> `PrintOutputItemNode`s: one value-only, one separator-only. MS-VBAL's `outputItem` grammar rule admits combining
> a value and its separator into one output item, but the generated parser never takes that alternative. A
> consumer of `Items` should walk the alternating list rather than assume `(value, separator)` pairs.

See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

---
> ⏮️ [**RD-VBAL §5.4.5.7** Width Statement](rd-vbal.5.4.5.7.width-statement.md) | ⏭️ [**RD-VBAL §5.4.5.9** Write Statement](rd-vbal.5.4.5.9.write-statement.md)
