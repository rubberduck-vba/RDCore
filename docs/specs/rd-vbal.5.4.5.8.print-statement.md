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

`Debug` is written only as this statement and `Debug.Assert`: any other use of the name is a syntax error, as it is in
MS-VBA (see [**RD-VBAL §5.4.2.23** Assert Statement](rd-vbal.5.4.2.23.assert-statement.md)).

An output list is a list of expressions, so in `Debug.Print x = 0` and `Owner.Print x = 0` the output list is the comparison `x = 0`: neither is a
Let statement, whose target is never the `Print` of an object with an output list
([**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)).

The output list these nodes carry is described in [5.4.5.8.1 Output Lists](#54581-output-lists) below. See
[**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

### The bare `Print`

Whether there is a bare `Print` at all is the language's to say
([`SupportedLanguage.HasBarePrint`](../api/RDCore.SDK.Workspace.SupportedLanguage.html)):

|Language|A `Print` with no file number|
|---|---|
|RD-VBA|Not a statement. `Print` is a reserved identifier of VBA, illegal as a name and with no semantics, and it is no recognized statement inside a procedure. (Only the Immediate window accepts it, as `Debug.Print`; that is not procedure scope.) It is [`VBC09331`](../diagnostics/vbc09331.md), the same as any other name the language does not declare.|
|VB6|Undefined, for now: it is the `Print` member of the form or report it is written in, and the platform has no forms yet.|
|BASIC|A statement. It writes to the session's own output - the one `Debug.Print` writes to - by the same output rules ([5.4.5.8.1](#54581-output-lists)).|

In BASIC it differs from `Debug.Print` in one way: it is not a debug statement, and is lowered in a release build, where `Debug.Print` leaves no
instruction at all ([**RD-VBAL §3.5.1** InstructionList](rd-vbal.3.5.1.instructionlist.md)). It is what an interactive shell - which is written in BASIC -
writes its output with: the shell's `?` shorthand expands to it.

Instruction-list lowering is told the language the body is written in (`InstructionLoweringOptions.Language`, from the environment's
`IRuntimeEnvironmentProfile.Language`) and reports the undefined form; a body lowered for no language is lowered as written.

> [!NOTE]
> **Not implemented.** `StatementStaticSemanticsEvaluator` does not report it, having no language to ask: the language server has no live caller of it yet. A document
> module (a form, a report) with a `Print` member of its own is not yet the target of a bare `Print` written in it.
>
> The `?` shorthand has no lexer or grammar token: the shell expands it before the line is parsed.

## Static Semantics

> [!NOTE]
> Reserved. This section has no content yet.

## Runtime Semantics

A number or a date in an output list is let-coerced to `String`, as **MS-VBAL §5.4.5.8.1** says, in the regional
settings of the session's environment ([**RD-VBAL §5.5.1.2.4** Let-coercion to and from String](rd-vbal.5.5.1.2.runtime-semantics.md#55124-let-coercion-to-and-from-string)).
Every form of `Print` is a text channel: a `String` is written as its whole characters, and a trailing byte is not
written ([**RD-VBAL §2.4.1.2** VBStringType](rd-vbal.2.4.1.intrinsic-types.md#2412-vbstringtype)).

An object is printed as what it stands for, its default member's value, as the value it is (` 7 ` for an Excel range that
holds 7). As in MS-VBA (recorded in Excel), for `Print #` and `Debug.Print` alike, `Nothing` is error 91 and an object
whose default member takes an argument (a `Collection`) error 450.

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
