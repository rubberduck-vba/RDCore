# 3.4.3 File Statements

MS-VBAL groups the file I/O statements under one umbrella section, [**MS-VBAL §5.4.5** File Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2fd9c1be-0d9a-4b29-b5ac-c9d51ce483cf). Each file statement is cross-referenced below to its MS-VBAL section and to the RD-VBAL page that describes its implementation; see also [**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md).

|Statement|Node type|MS-VBAL|RD-VBAL|
|---|---|---|---|
|`Open`|[OpenStatementNode](../api/RDCore.SDK.Model.AST.Statements.OpenStatementNode.html); see [Open Statement](#open-statement)|[**MS-VBAL §5.4.5.1** Open Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/29a62f38-5bf6-4e08-9dae-0094e377058b)|[**RD-VBAL §5.4.5.1** Open Statement](rd-vbal.5.4.5.1.open-statement.md)|
|`Close`, `Reset`|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `Close` or `Reset`)|[**MS-VBAL §5.4.5.2** Close and Reset Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/73ef1ac2-4da7-4cda-b2a3-5984a8649ded)|[**RD-VBAL §5.4.5.2** Close and Reset Statements](rd-vbal.5.4.5.2.close-and-reset-statements.md)|
|`Seek`|`KeywordStatementNode` (`Token`: `Seek`)|[**MS-VBAL §5.4.5.3** Seek Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fec0271d-31ed-4e3d-bff4-13f3b7f09f3b)|[**RD-VBAL §5.4.5.3** Seek Statement](rd-vbal.5.4.5.3.seek-statement.md)|
|`Lock`|[FileLockStatementNode](../api/RDCore.SDK.Model.AST.Statements.FileLockStatementNode.html) (`Token`: `Lock`); see [Lock and Unlock Statements](#lock-and-unlock-statements)|[**MS-VBAL §5.4.5.4** Lock Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5ff8a0e5-4e44-45a3-92a6-3c77cea3e3c5)|[**RD-VBAL §5.4.5.4** Lock Statement](rd-vbal.5.4.5.4.lock-statement.md)|
|`Unlock`|`FileLockStatementNode` (`Token`: `Unlock`)|[**MS-VBAL §5.4.5.5** Unlock Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/102f53f2-0393-4df1-8fe8-6f23f2d58d14)|[**RD-VBAL §5.4.5.5** Unlock Statement](rd-vbal.5.4.5.5.unlock-statement.md)|
|`Line Input #`|`KeywordStatementNode` (`Token`: `"Line Input"`)|[**MS-VBAL §5.4.5.6** Line Input Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/dab5496c-a151-4d69-adc4-bb5effc066e9)|[**RD-VBAL §5.4.5.6** Line Input Statement](rd-vbal.5.4.5.6.line-input-statement.md)|
|`Width #`|`KeywordStatementNode` (`Token`: `Width`)|[**MS-VBAL §5.4.5.7** Width Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e427933b-d398-424b-9dbc-8cb91dece4cc)|[**RD-VBAL §5.4.5.7** Width Statement](rd-vbal.5.4.5.7.width-statement.md)|
|`Print #` (file-number form)|[PrintStatementNode](../api/RDCore.SDK.Model.AST.Statements.PrintStatementNode.html) (`Token`: `Print`)|[**MS-VBAL §5.4.5.8** Print Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6f427c3d-2471-4cd8-8cae-2e2951918b51)|[**RD-VBAL §5.4.5.8** Print Statement](rd-vbal.5.4.5.8.print-statement.md)|
|`Owner.Print` (object-qualified form)|[ObjectPrintExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.ObjectPrintExpressionNode.html)|**MS-VBAL §5.4.5.8**|[**RD-VBAL §5.4.5.8** Print Statement](rd-vbal.5.4.5.8.print-statement.md)|
|`Debug.Print`|[DebugPrintStatementNode](../api/RDCore.SDK.Model.AST.Statements.DebugPrintStatementNode.html)|**MS-VBAL §5.4.5.8**|[**RD-VBAL §5.4.5.8** Print Statement](rd-vbal.5.4.5.8.print-statement.md)|
|`Write #`|`PrintStatementNode` (`Token`: `Write`)|[**MS-VBAL §5.4.5.9** Write Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7d516617-3cbc-4cb1-88f7-d64e8e640a07)|[**RD-VBAL §5.4.5.9** Write Statement](rd-vbal.5.4.5.9.write-statement.md)|
|`Input #`|`KeywordStatementNode` (`Token`: `Input`)|[**MS-VBAL §5.4.5.10** Input Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f41b8636-a3f5-4501-b1a9-78058017c232)|[**RD-VBAL §5.4.5.10** Input Statement](rd-vbal.5.4.5.10.input-statement.md)|
|`Put #`|`KeywordStatementNode` (`Token`: `Put`)|[**MS-VBAL §5.4.5.11** Put Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/46eeacb8-7a06-4ec8-9736-eea42de4eeca)|[**RD-VBAL §5.4.5.11** Put Statement](rd-vbal.5.4.5.11.put-statement.md)|
|`Get #`|`KeywordStatementNode` (`Token`: `Get`)|[**MS-VBAL §5.4.5.12** Get Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/60c6f92b-d1fc-484b-91d1-6ba5246334b4)|[**RD-VBAL §5.4.5.12** Get Statement](rd-vbal.5.4.5.12.get-statement.md)|
|`Name`|`KeywordStatementNode` (`Token`: `Name`)|_Not specified._|[**RD-VBAL §5.4.5.13** Name Statement](rd-vbal.5.4.5.13.name-statement.md)|

## Open Statement

`OpenStatementNode`'s `Mode`, `Access` and `Lock` are keyword choices, not expressions. They are typed [VBFileMode](../api/RDCore.SDK.Model.AST.Statements.VBFileMode.html), [VBFileAccessMode](../api/RDCore.SDK.Model.AST.Statements.VBFileAccessMode.html) and [VBFileLockMode](../api/RDCore.SDK.Model.AST.Statements.VBFileLockMode.html).

## Lock and Unlock Statements

`Lock` and `Unlock` have an AST node of their own, `FileLockStatementNode`, because their `record-range` (**MS-VBAL §5.4.5.4**) has three shapes and a flat input list could tell only two of them apart:

```
record-range = start-record-number / ([start-record-number] "To" end-record-number)
```

`Lock #1, 5` locks record 5, and `Lock #1, To 5` locks records 1 through 5. Each carries one expression, so a flat input list cannot distinguish them. `FileLockStatementNode`'s `StartRecord` and `EndRecord` are therefore named properties rather than positional inputs:

|Statement|`StartRecord`|`EndRecord`|
|---|---|---|
|`Lock #1, 5`|`5`|`null`|
|`Lock #1, 2 To 5`|`2`|`5`|
|`Lock #1, To 5`|`null`|`5`|

An absent `start-record-number` stays absent in `FileLockStatementNode`: the node does not fill in 1. MS-VBAL says of an absent `start-record-number`: "the effect is as if `<start-record-number>` consisted of the integer number token 1". The implied start record 1 is a runtime semantic, not something the program wrote, so the node does not model it; see [**RD-VBAL §5.4.5.4** Lock Statement](rd-vbal.5.4.5.4.lock-statement.md).

## Output Lists

`PrintStatementNode`, `ObjectPrintExpressionNode` and `DebugPrintStatementNode` share one output-list shape ([**MS-VBAL §5.4.5.8.1** Output Lists](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/630ce2fe-abf1-4aae-a126-1c6567ac2a41)):

|Node|Represents|
|---|---|
|[PrintOutputItemNode](../api/RDCore.SDK.Model.AST.Expressions.PrintOutputItemNode.html)|An output item: a value and/or a `;` or `,` separator.|
|[PrintSpcClauseNode](../api/RDCore.SDK.Model.AST.Expressions.PrintSpcClauseNode.html)|A `Spc` clause.|
|[PrintTabClauseNode](../api/RDCore.SDK.Model.AST.Expressions.PrintTabClauseNode.html)|A `Tab` clause.|

> [!TIP]
> In a parsed output list, a value and its trailing separator are always two separate, alternating `PrintOutputItemNode`s: one value-only, one separator-only. MS-VBAL's `outputItem` grammar rule admits combining a value and its separator into one output item, but the generated parser never takes that alternative. A consumer of `Items` should walk the alternating list rather than assume `(value, separator)` pairs.

> [!NOTE]
> **Not implemented.** The `?` shorthand for `Print` has no lexer or grammar token.

---
> ⏮️ [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md) | ⏭️ [**RD-VBAL §3.5.0** Instructions](rd-vbal.3.5.0.instructions.md)
