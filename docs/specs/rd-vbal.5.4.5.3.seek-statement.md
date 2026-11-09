# 5.4.5.3 Seek Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.3** Seek Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fec0271d-31ed-4e3d-bff4-13f3b7f09f3b).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[KeywordStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html)|`Simple`|`Token`: `Seek`.|

See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

## Runtime Semantics

**MS-VBAL §5.4.5.3** counts the file-pointer-position from 1.

The record number of the `Put` and `Get` statements and the file-pointer-position are one quantity, and `Seek` and
`Get` must agree on it. Record number 1 is therefore byte 0 of the file; see
[**RD-VBAL §5.4.5.11** Put Statement](rd-vbal.5.4.5.11.put-statement.md) and
[**RD-VBAL §5.4.5.12** Get Statement](rd-vbal.5.4.5.12.get-statement.md).

## Implementation

Like every file statement, `Seek` runs through the session's file-channel shim,
[IFileChannels](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannels.html)
([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).
