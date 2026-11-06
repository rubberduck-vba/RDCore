# 5.4.5.6 Line Input Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.6** Line Input Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/dab5496c-a151-4d69-adc4-bb5effc066e9).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html)|`Simple`|`Token`: `"Line Input"` ([Tokens](../api/RDCore.SDK.Model.Tokens.html)`.LineInput`).|

See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

## Runtime Semantics

`Line Input #` reads from the channel's character input surface. It Let-assigns what it reads to its target, as
**MS-VBAL §5.4.5.6** specifies.

`Line Input #` resolves its target with the same target resolution the Let assignment statement uses
([**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)).

## Implementation

|Name|Role|
|---|---|
|[IFileChannelInput](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannelInput.html)|The character input surface of a channel, shared with `Input #` ([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).|
|`LetAssignmentEvaluator.TryResolveTarget`|Resolves the target: the same target resolution `Input #`, `Get` and the Let assignment statement use.|
