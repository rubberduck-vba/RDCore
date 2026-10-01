# 5.4.5.12 Get Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.12** Get Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/60c6f92b-d1fc-484b-91d1-6ba5246334b4).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html)|`Simple`|`Token`: `Get`.|

See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

## Runtime Semantics

`Get` reads through the record surface of a file channel, which it shares with `Put`
([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).

`Get` resolves its target with the same target resolution the Let assignment statement uses
([**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)). It Let-assigns what it reads to its
target, as **MS-VBAL §5.4.5.12** specifies.

### Record Format

`Get` reads the record format `Put` writes:
[**MS-VBAL §5.4.5.11** Put Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/46eeacb8-7a06-4ec8-9736-eea42de4eeca)'s
Variant type descriptors and binary widths. The format is a wire format rather than a behaviour, and follows
MS-VBA's so that a file RD-VBA writes is a file MS-VBA reads. See
[**RD-VBAL §5.4.5.11** Put Statement](rd-vbal.5.4.5.11.put-statement.md) for the format.

`Get` reads each member of a UDT in declaration order, recursively through a nested UDT. `Get` moves the
serialized size of a UDT. `Put #1, , myRecord` and `Get #1, , myRecord` serialize and deserialize a whole UDT in
one statement.

### Record Positioning

`Get` positions records as `Put` does
([**RD-VBAL §5.4.5.11** Put Statement](rd-vbal.5.4.5.11.put-statement.md)):

- Record number 1 is byte 0 of the file. The record number and the file-pointer-position are one quantity, and
  `Seek` and `Get` must agree on it
  ([**RD-VBAL §5.4.5.3** Seek Statement](rd-vbal.5.4.5.3.seek-statement.md)).
- A `Random` channel whose `Open` statement declared no `Len` clause counts positions in 128-byte records.

## Implementation

|Name|Role|
|---|---|
|[IFileChannel](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannel.html)|A file channel. Its record surface is the one `Put` and `Get` use.|
|`RecordDataFormat`|Implements the `Put`/`Get` record format.|
|`LetAssignmentEvaluator.TryResolveTarget`|Resolves the target: the same target resolution `Input #`, `Line Input #` and the Let assignment statement use.|

---
> ⏮️ [**RD-VBAL §5.4.5.11** Put Statement](rd-vbal.5.4.5.11.put-statement.md) | ⏭️ [**RD-VBAL §5.4.5.13** Name Statement](rd-vbal.5.4.5.13.name-statement.md)
