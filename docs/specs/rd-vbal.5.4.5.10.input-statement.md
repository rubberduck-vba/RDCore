# 5.4.5.10 Input Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.10** Input Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f41b8636-a3f5-4501-b1a9-78058017c232).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html)|`Simple`|`Token`: `Input`.|

See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

## Runtime Semantics

`Input #` reads the text format `Write #` writes: a `String` is a quoted string, and the format includes the tokens
`#TRUE#`, `#NULL#`, `#ERROR n#` and `#yyyy-mm-dd hh:mm:ss#`. See
[**RD-VBAL §5.4.5.9** Write Statement](rd-vbal.5.4.5.9.write-statement.md) for the format.

The number of characters `Input #` reads depends on the declared type of the variable it reads into, so `Input #`
resolves the target before it reads the target's field. For its target, `Input #`:

1. Resolves the target variable, with the same target resolution the Let assignment statement uses
   ([**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)).
2. Reads the target's field from the file, through the channel's character input surface.
3. Let-assigns what it read to the target, as **MS-VBAL §5.4.5.10** specifies.

## Implementation

|Name|Role|
|---|---|
|[IFileChannelInput](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannelInput.html)|The character input surface of a channel, shared with `Line Input #` ([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).|
|`LetAssignmentEvaluator.TryResolveTarget`|Resolves the target: the same target resolution `Line Input #`, `Get` and the Let assignment statement use.|
