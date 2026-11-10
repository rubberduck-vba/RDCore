# 5.4.5.2 Close and Reset Statements

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.2** Close and Reset Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/73ef1ac2-4da7-4cda-b2a3-5984a8649ded).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[KeywordStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html)|`Simple`|`Token`: `Close` or `Reset`.|

See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

## Static Semantics

> [!NOTE]
> Reserved. This section has no content yet.

## Runtime Semantics

> [!NOTE]
> Reserved. This section has no content yet.

## Implementation

Like every file statement, `Close` and `Reset` run through the session's file-channel shim,
[IFileChannels](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannels.html)
([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).
