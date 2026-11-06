# 5.4.5.7 Width Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.7** Width Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e427933b-d398-424b-9dbc-8cb91dece4cc).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html)|`Simple`|`Token`: `Width`.|

See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

## Runtime Semantics

`Width #` sets the maximum line length of the channel's character output surface: the surface `Print #` and
`Write #` write through ([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).

## Implementation

|Name|Role|
|---|---|
|[IFileChannelOutput](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannelOutput.html)|The character output surface of a channel. It carries the maximum line length `Width #` sets.|
