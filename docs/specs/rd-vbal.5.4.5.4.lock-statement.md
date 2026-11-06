# 5.4.5.4 Lock Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.4** Lock Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5ff8a0e5-4e44-45a3-92a6-3c77cea3e3c5).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[FileLockStatementNode](../api/RDCore.SDK.Model.AST.Statements.FileLockStatementNode.html)|`Simple`|`Token`: `Lock`. `StartRecord` and `EndRecord` are named properties rather than positional inputs. An absent `start-record-number` stays absent: the node does not fill in 1.|

`Lock` and `Unlock` have an AST node of their own, `FileLockStatementNode`. Their `record-range` has three shapes.
`Lock #1, 5` and `Lock #1, To 5` are different record ranges that each carry one expression, which is why
`StartRecord` and `EndRecord` are named. See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

## Runtime Semantics

|Statement|Records locked|
|---|---|
|`Lock #1, 5`|Record 5.|
|`Lock #1, To 5`|Records 1 through 5.|
|`Lock #1, 2 To 5`|Records 2 through 5.|

MS-VBAL says of an absent `start-record-number`: "the effect is as if `<start-record-number>` consisted of the
integer number token 1". That is a runtime semantic, not something the program wrote: `FileLockStatementNode`
leaves the start record absent, and `Lock #1, To 5` locks from record 1 when it runs.

> [!NOTE]
> **Not implemented.** `Lock` and `Unlock` do not apply an OS-level lock. **MS-VBAL §5.4.5.4** leaves applying
> an OS-level lock implementation-defined.

## Implementation

Like every file statement, `Lock` runs through the session's file-channel shim,
[IFileChannels](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannels.html)
([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).
