# 5.4.5 File Statements

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.5** File Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2fd9c1be-0d9a-4b29-b5ac-c9d51ce483cf).

MS-VBAL describes the file I/O statements in one section, **MS-VBAL §5.4.5**. RD-VBA executes every statement of
that section, and `Name`, which MS-VBAL does not specify and the
[VBA language reference](https://learn.microsoft.com/en-us/office/vba/language/reference/user-interface-help/name-statement) does.

|Statement|MS-VBAL|RD-VBAL|
|---|---|---|
|`Open`|[**MS-VBAL §5.4.5.1** Open Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/29a62f38-5bf6-4e08-9dae-0094e377058b)|[**RD-VBAL §5.4.5.1** Open Statement](rd-vbal.5.4.5.1.open-statement.md)|
|`Close`, `Reset`|[**MS-VBAL §5.4.5.2** Close and Reset Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/73ef1ac2-4da7-4cda-b2a3-5984a8649ded)|[**RD-VBAL §5.4.5.2** Close and Reset Statements](rd-vbal.5.4.5.2.close-and-reset-statements.md)|
|`Seek`|[**MS-VBAL §5.4.5.3** Seek Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fec0271d-31ed-4e3d-bff4-13f3b7f09f3b)|[**RD-VBAL §5.4.5.3** Seek Statement](rd-vbal.5.4.5.3.seek-statement.md)|
|`Lock`|[**MS-VBAL §5.4.5.4** Lock Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5ff8a0e5-4e44-45a3-92a6-3c77cea3e3c5)|[**RD-VBAL §5.4.5.4** Lock Statement](rd-vbal.5.4.5.4.lock-statement.md)|
|`Unlock`|[**MS-VBAL §5.4.5.5** Unlock Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/102f53f2-0393-4df1-8fe8-6f23f2d58d14)|[**RD-VBAL §5.4.5.5** Unlock Statement](rd-vbal.5.4.5.5.unlock-statement.md)|
|`Line Input #`|[**MS-VBAL §5.4.5.6** Line Input Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/dab5496c-a151-4d69-adc4-bb5effc066e9)|[**RD-VBAL §5.4.5.6** Line Input Statement](rd-vbal.5.4.5.6.line-input-statement.md)|
|`Width #`|[**MS-VBAL §5.4.5.7** Width Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e427933b-d398-424b-9dbc-8cb91dece4cc)|[**RD-VBAL §5.4.5.7** Width Statement](rd-vbal.5.4.5.7.width-statement.md)|
|`Print #`|[**MS-VBAL §5.4.5.8** Print Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6f427c3d-2471-4cd8-8cae-2e2951918b51)|[**RD-VBAL §5.4.5.8** Print Statement](rd-vbal.5.4.5.8.print-statement.md)|
|`Write #`|[**MS-VBAL §5.4.5.9** Write Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7d516617-3cbc-4cb1-88f7-d64e8e640a07)|[**RD-VBAL §5.4.5.9** Write Statement](rd-vbal.5.4.5.9.write-statement.md)|
|`Input #`|[**MS-VBAL §5.4.5.10** Input Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f41b8636-a3f5-4501-b1a9-78058017c232)|[**RD-VBAL §5.4.5.10** Input Statement](rd-vbal.5.4.5.10.input-statement.md)|
|`Put`|[**MS-VBAL §5.4.5.11** Put Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/46eeacb8-7a06-4ec8-9736-eea42de4eeca)|[**RD-VBAL §5.4.5.11** Put Statement](rd-vbal.5.4.5.11.put-statement.md)|
|`Get`|[**MS-VBAL §5.4.5.12** Get Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/60c6f92b-d1fc-484b-91d1-6ba5246334b4)|[**RD-VBAL §5.4.5.12** Get Statement](rd-vbal.5.4.5.12.get-statement.md)|
|`Name`|_Not specified._|[**RD-VBAL §5.4.5.13** Name Statement](rd-vbal.5.4.5.13.name-statement.md)|

The AST node of each file statement is catalogued in
[**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md). Every file statement lowers to a `Simple`
instruction ([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

## The File-Channel Shim

All file statements run through one session-level shim: the
[IFileChannels](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannels.html) /
[IFileChannel](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannel.html) interfaces
([**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)).

`IFileChannels` holds numbered channels. Each channel records the mode it was opened under.

An administrator can restrict or redirect file I/O at the shim. A test can replace the shim with a fake.

## Statement, Mode and Access

**MS-VBAL §5.4.5.1** gives one statement/mode/access table for the file statements.
[FileStatementAccess](../api/RDCore.SDK.Runtime.Abstract.Execution.FileStatementAccess.html) holds that table in
one place, so that the twelve file statements do not each restate it.

## Channel Surfaces

A file channel has a character surface in each direction (output and input), and a record surface.

|Surface|Used by|Interface|
|---|---|---|
|Character output|`Print #`, `Write #`|[IFileChannelOutput](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannelOutput.html)|
|Character input|`Line Input #`, `Input #`|[IFileChannelInput](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannelInput.html)|
|Record|`Put`, `Get`|`IFileChannel`|

`IFileChannelOutput` carries a maximum line length, which `Width #` sets
([**RD-VBAL §5.4.5.7** Width Statement](rd-vbal.5.4.5.7.width-statement.md)).

The text format that `Write #` writes and `Input #` reads is described in
[**RD-VBAL §5.4.5.9** Write Statement](rd-vbal.5.4.5.9.write-statement.md). The record format that `Put` writes
and `Get` reads is described in [**RD-VBAL §5.4.5.11** Put Statement](rd-vbal.5.4.5.11.put-statement.md).

## Reading Statements

`Input #`, `Line Input #` and `Get` Let-assign what they read to their targets. They resolve those targets with
the same target resolution the Let assignment statement uses
([**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)).

## Implementation

|Name|Role|
|---|---|
|`IFileChannels`|The session-level shim every file statement runs through: the numbered channels, each with the mode it was opened under.|
|`IFileChannel`|One numbered channel. Its record surface is the one `Put` and `Get` use.|
|`IFileChannelOutput`|The character output surface of a channel, used by `Print #` and `Write #`. It carries the maximum line length `Width #` sets.|
|`IFileChannelInput`|The character input surface of a channel, used by `Line Input #` and `Input #`.|
|`FileStatementAccess`|**MS-VBAL §5.4.5.1**'s statement/mode/access table, held in one place.|
|`LetAssignmentEvaluator.TryResolveTarget`|The target resolution `Input #`, `Line Input #` and `Get` use: the same target resolution the Let assignment statement uses.|
|`RecordDataFormat`|The `Put`/`Get` record format ([**RD-VBAL §5.4.5.11** Put Statement](rd-vbal.5.4.5.11.put-statement.md)).|

---
> ⏮️ [**RD-VBAL §5.4.4.3** Error Statement](rd-vbal.5.4.4.3.error-statement.md) | ⏭️ [**RD-VBAL §5.4.5.1** Open Statement](rd-vbal.5.4.5.1.open-statement.md)
