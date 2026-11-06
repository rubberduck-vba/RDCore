# 5.4.5.5 Unlock Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.5** Unlock Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/102f53f2-0393-4df1-8fe8-6f23f2d58d14).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[FileLockStatementNode](../api/RDCore.SDK.Model.AST.Statements.FileLockStatementNode.html)|`Simple`|`Token`: `Unlock`. `StartRecord` and `EndRecord` are named properties rather than positional inputs.|

`Lock` and `Unlock` share one AST node, `FileLockStatementNode`. Their `record-range` has three shapes, and
`StartRecord` and `EndRecord` are named because `Lock #1, 5` and `Lock #1, To 5` are different record ranges that
each carry one expression. See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

## Runtime Semantics

`Unlock` takes the same `record-range` as `Lock`; see
[**RD-VBAL §5.4.5.4** Lock Statement](rd-vbal.5.4.5.4.lock-statement.md).

> [!NOTE]
> **Not implemented.** `Lock` and `Unlock` do not apply an OS-level lock.
> [**MS-VBAL §5.4.5.4** Lock Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5ff8a0e5-4e44-45a3-92a6-3c77cea3e3c5)
> leaves applying an OS-level lock implementation-defined.

## Implementation

Like every file statement, `Unlock` runs through the session's file-channel shim,
[IFileChannels](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannels.html)
([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).
