# 5.4.5.8 Print Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.8** Print Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6f427c3d-2471-4cd8-8cae-2e2951918b51).

## Syntax

|Form|AST node|Instruction kind(s)|Notes|
|---|---|---|---|
|`Print #`|[PrintStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.PrintStatementNode.html)|`Simple`|The file-number form. `Token`: `Print`.|
|`Print`|[PrintStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.PrintStatementNode.html)|`Simple`|The bare form, with no file number (`FileNumber` is `null`): `Print "x"`. See below.|
|`Owner.Print`|[ObjectPrintExpressionNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Expressions.ObjectPrintExpressionNode.html)|None|The object-qualified form.|
|`Debug.Print`|[DebugPrintStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.DebugPrintStatementNode.html)|`Simple`|The object-qualified form whose owner is `Debug`.|

`Debug` is written only as this statement and `Debug.Assert`: any other use of the name is a syntax error, as it is in
MS-VBA (see [**RD-VBAL §5.4.2.23** Assert Statement](rd-vbal.5.4.2.23.assert-statement.md)).

An output list is a list of expressions, so in `Debug.Print x = 0` and `Owner.Print x = 0` the output list is the comparison `x = 0`: neither is a
Let statement, whose target is never the `Print` of an object with an output list
([**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)).

The output list these nodes carry is described in [5.4.5.8.1 Output Lists](#54581-output-lists) below. See
[**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

### The bare `Print`

Whether there is a bare `Print` at all is the language's to say
([`SupportedLanguage.HasBarePrint`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Workspace.SupportedLanguage.html)):

|Language|A `Print` with no file number|
|---|---|
|RD-VBA|Not a statement. `Print` is a reserved identifier of VBA, illegal as a name and with no semantics, and it is no recognized statement inside a procedure. (Only the Immediate window accepts it, as `Debug.Print`; that is not procedure scope.) It is [`VBC09331`](../diagnostics/vbc09331.md), the same as any other name the language does not declare.|
|VB6|Undefined, for now: it is the `Print` member of the form or report it is written in, and the platform has no forms yet.|
|BASIC|A statement. It writes to the session's own output - the one `Debug.Print` writes to - by the same output rules ([5.4.5.8.1](#54581-output-lists)).|

In BASIC it differs from `Debug.Print` in one way: it is not a debug statement, and is lowered in every release build, including one that drops
`Debug.Print`, which then leaves no instruction at all ([**RD-VBAL §3.5.1** InstructionList](rd-vbal.3.5.1.instructionlist.md)). It is what an interactive shell - which is written in BASIC -
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
A number is followed by a space, and preceded by one where its sign would be: a negative number's sign takes that place, so `1` is printed `" 1 "`
and `-1` is printed `"-1 "`, as in MS-VBA (recorded in Excel). The Runtime Semantics of **MS-VBAL §5.4.5.8** insert a space before every number,
so the specification prints `-1` as `" -1 "`.

Every form of `Print` is a text channel: a `String` is written as its whole characters, and a trailing byte is not
written ([**RD-VBAL §2.4.1.2** VBStringType](rd-vbal.2.4.1.intrinsic-types.md#2412-vbstringtype)).

An object is printed as what it stands for, its default member's value, as the value it is (` 7 ` for an Excel range that
holds 7). As in MS-VBA (recorded in Excel), for `Print #` and `Debug.Print` alike, `Nothing` is error 91 and an object
whose default member takes an argument (a `Collection`) error 450.

## Implementation

`Print #` writes through the channel's character output surface,
[IFileChannelOutput](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannelOutput.html), which it shares with
`Write #`. `Width #` sets that surface's maximum line length
([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).

## 5.4.5.8.1 Output Lists

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.8.1** Output Lists](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/630ce2fe-abf1-4aae-a126-1c6567ac2a41).

`PrintStatementNode` and `ObjectPrintExpressionNode` share one output-list shape:

|Node|Represents|
|---|---|
|[PrintOutputItemNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Expressions.PrintOutputItemNode.html)|An output item: a value and/or a `;` or `,` separator.|
|[PrintSpcClauseNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Expressions.PrintSpcClauseNode.html)|A `Spc` clause.|
|[PrintTabClauseNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Expressions.PrintTabClauseNode.html)|A `Tab` clause.|

> [!TIP]
> In a parsed output list, a value and the `;` or `,` after it are always two separate `PrintOutputItemNode`s: one
> value-only, one separator-only. The grammar reads them so: an output item and a separator are two rules of their own.
> A consumer of `Items` should walk the list as it is rather than assume `(value, separator)` pairs: two separators can
> follow each other (`1;;2`), and two values with a space between them have none (`1 2`).

> [!NOTE]
> **RD-VBAL diverges from MS-VBAL** in what may stand between two output items.
>
> **MS-VBAL §5.4.5.8.1** reads `output-list = *output-item`, so an output item may follow another with nothing between
> them: `Debug.Print 1(2)` is the two items `1` and `(2)`. The VBE does not keep such a line as it is written: it lists
> it again with a `;` between the items (`Debug.Print 1; (2)`), in every form of the statement, `Write #` included.
>
> RD-VBA requires a space, a `;` or a `,` between two output items, and reports two items written side by side as
> [`VBC00061`](../diagnostics/vbc00061.md), at the second one, once for each statement. A name right before
> parentheses is not two items: in `Debug.Print F(2)`, `F` is called with the argument `2`, as in MS-VBA.
>
> Nor is a name, spaces, then parentheses. **MS-VBAL §3.3** makes the spaces before a token part of it, so
> `Debug.Print F (2)` reads both as the index expression `F(2)` and as the two items `F` and `(2)`. MS-VBA reads the
> index expression, and the VBE writes the line `Debug.Print F(2)`. RD-VBA reads it as MS-VBA does, in an output item,
> an operand of one (`Debug.Print x + F (2)`) and the arguments of a call within one (`Debug.Print F (G (1))`), as in any
> expression ([**RD-VBAL §5.6.13** Index Expressions](rd-vbal.5.6.13.index-expressions.md)). A value that is no name stays
> two items: `Debug.Print 1 (2)` prints 1, then 2.

See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).
