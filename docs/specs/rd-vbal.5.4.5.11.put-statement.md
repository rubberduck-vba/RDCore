# 5.4.5.11 Put Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5.11** Put Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/46eeacb8-7a06-4ec8-9736-eea42de4eeca).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html)|`Simple`|`Token`: `Put`.|

See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

## Runtime Semantics

`Put` writes through the record surface of a file channel, which it shares with `Get`
([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)). This section describes the record
format and record positioning of both statements; see also
[**RD-VBAL §5.4.5.12** Get Statement](rd-vbal.5.4.5.12.get-statement.md).

### Record Format

`Put` and `Get` use **MS-VBAL §5.4.5.11**'s Variant type descriptors and binary widths. The record format is a wire
format rather than a behaviour.

The record format follows MS-VBA's, so that a file RD-VBA writes is a file MS-VBA reads.

|Data|In a record|
|---|---|
|`Boolean`|Two bytes. `True` is `FF FF`.|
|`String`, in `Random` mode|Carries a two-byte length prefix.|
|`String`, in `Binary` mode|Carries no length prefix.|
|`Variant`|Preceded by its two-byte type descriptor.|
|A `Variant` that holds an object, or `Nothing`|No record: error 13, as in MS-VBA (recorded in Excel) - once its descriptor, `vbObject` (`09 00`), is written in `Binary` mode; nothing is written in `Random` mode. A variable declared as an object, or a UDT with one in it, is a compile error.|
|A variable or a field of an `Enum`|The `Long` it holds, four bytes ([**RD-VBAL §5.2.3.4**](rd-vbal.5.2.3.module-declarations.md#5234-enum-declarations)).|
|A UDT|Each member of the UDT, in declaration order, recursively through a nested UDT (see [User-Defined Types](#user-defined-types)).|

### User-Defined Types

**MS-VBAL §5.4.5.11** and
[**MS-VBAL §5.4.5.12** Get Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/60c6f92b-d1fc-484b-91d1-6ba5246334b4)
say "the value of each member of the UDT is written to the file... in the order in which the members are
declared". `Put` writes, and `Get` reads, each member of a UDT in declaration order, recursively through a nested
UDT.

UDT field declaration order is the order `Put` writes a record in
([**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md)).

`Put #1, , myRecord` and `Get #1, , myRecord` serialize and deserialize a whole UDT in one statement.

`Put` and `Get` move the serialized size of a UDT: its fields concatenated, with no padding.

> 👉 Memory widths of UDT fields are not file widths. See
> [**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md) for the in-memory side.

|UDT field|In a record|
|---|---|
|Variable-length `String`|Its characters.|
|Fixed-length `String`|ANSI.|

### Record Positioning

MS-VBAL leaves two gaps in record positioning, and RD-VBA names its choice for each:

|Gap|RD-VBA's choice|
|---|---|
|The record length of a `Random` channel opened without a `Len` clause|The channel counts positions in 128-byte records.|
|The byte position of record 1|Record number 1 is byte 0 of the file.|

**Record length.** A `Random` channel whose `Open` statement declared no `Len` clause counts positions in 128-byte
records. 128 bytes is MS-VBA's own default record length.
[**MS-VBAL §5.4.5.1** Open Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/29a62f38-5bf6-4e08-9dae-0094e377058b)
constrains the `Len` clause but does not say what an absent `Len` clause means.

**Record 1.** **MS-VBAL §5.4.5.11** says the file position becomes "exactly `<record-number>` number of bytes
from the start". It also defaults the `Put` record number to the current file-pointer-position, which
[**MS-VBAL §5.4.5.3** Seek Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fec0271d-31ed-4e3d-bff4-13f3b7f09f3b)
counts from 1.

The record number and the file-pointer-position are one quantity, and `Seek` and `Get` must agree on it. That is
why record number 1 is byte 0 of the file
([**RD-VBAL §5.4.5.3** Seek Statement](rd-vbal.5.4.5.3.seek-statement.md)).

## Implementation

|Name|Role|
|---|---|
|`RecordDataFormat`|Implements the `Put`/`Get` record format: **MS-VBAL §5.4.5.11**'s Variant type descriptors and binary widths.|
|[IFileChannel](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannel.html)|A file channel. Its record surface is the one `Put` and `Get` use.|

---
> ⏮️ [**RD-VBAL §5.4.5.10** Input Statement](rd-vbal.5.4.5.10.input-statement.md) | ⏭️ [**RD-VBAL §5.4.5.12** Get Statement](rd-vbal.5.4.5.12.get-statement.md)
