# 5.4.5.1 Open Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.1** Open Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/29a62f38-5bf6-4e08-9dae-0094e377058b).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[OpenStatementNode](../api/RDCore.SDK.Model.AST.Statements.OpenStatementNode.html)|`Simple`|`Mode`, `Access` and `Lock` are keyword choices, not expressions. They are typed [VBFileMode](../api/RDCore.SDK.Model.AST.Statements.VBFileMode.html), [VBFileAccessMode](../api/RDCore.SDK.Model.AST.Statements.VBFileAccessMode.html) and [VBFileLockMode](../api/RDCore.SDK.Model.AST.Statements.VBFileLockMode.html).|

See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

## Runtime Semantics

The session's file-channel shim holds the numbered channels `Open` opens. Each channel records the mode it was
opened under ([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).

The statement/mode/access table of **MS-VBAL §5.4.5.1** is held once, for every file statement; see
[**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md).

**MS-VBAL §5.4.5.1** constrains the `Len` clause but does not say what an absent `Len` clause means. A `Random`
channel whose `Open` statement declared no `Len` clause counts positions in 128-byte records, MS-VBA's own
default record length; see [**RD-VBAL §5.4.5.11** Put Statement](rd-vbal.5.4.5.11.put-statement.md).

## Implementation

|Name|Role|
|---|---|
|[IFileChannels](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannels.html)|Holds the numbered channels, each with the mode it was opened under.|
|`FileStatementAccess`|Holds **MS-VBAL §5.4.5.1**'s statement/mode/access table.|

## 5.4.5.1.1 File Numbers

This section corresponds to [**MS-VBAL §5.4.5.1.1** File Numbers](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/38cf0628-c62b-4cb5-be3e-865600a9bc59).

> [!NOTE]
> Reserved. This section has no content yet.
