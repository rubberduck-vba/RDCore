# 6.1.2.5 FileSystem

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.5** FileSystem](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/bf77cb3b-1e75-4dad-b849-56d661ce1990).

The `FileSystem` module is represented in the SDK by the interface
[IStdFileSystemModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdFileSystemModule.html), and implemented by `RDCore.Runtime.StdLib.StdFileSystem`.

What a member does to the machine - a directory listed, a file copied or deleted, the current directory changed - goes through the session's
[IFileChannels](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannels.html), the one seam every file statement goes through
([**RD-VBAL §5.4.5**](rd-vbal.5.4.5.file-statements.md)): the channels know which files are open, which most of these members ask, and the seam is what
an administrator restricts and a test fakes. What the specification leaves to the implementation, and the errors it does not name, are **MS-VBA**'s,
recorded in Excel; where **MS-VBA** does other than the specification says, RD-VBA does what **MS-VBA** does, and the notes below say so.


## 6.1.2.5.1 Public Functions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.5.1** Public Functions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a7ed1298-8f98-41ac-a121-6349f4a3a152).

|§|Member|Notes|
|---|---|---|
|6.1.2.5.1.1|[CurDir/CurDir$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/8e82dffc-c920-4ed9-9960-cbd9b6613069)|✅ A drive is its first letter: error `68` for a letter that is no drive, `5` for a drive that is no letter. The format of a drive is implementation-defined: a letter, as on Windows - a file system that has none (Linux) has no drive a letter names.|
|6.1.2.5.1.2|[Dir](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/bebf1c39-de9f-4e6e-b25b-f05dbf77f091)|✅ Names with no directory part, spelled as the file system spells them, matched whatever their case and given in the order of their upper-case spelling - the directory the pattern is in being found as the file system finds it, by its case on one whose names are (Linux) - `.` and `..` first, with `vbDirectory`, which matches files too. A hidden or a system file only with `vbHidden` or `vbSystem`. A pattern ending in a separator is what is in the directory; what matches nothing, a directory that does not exist included, is a zero-length `String`. With no pattern, the next match, then a zero-length `String`, then error `5`.|
|6.1.2.5.1.3|[EOF](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6822235f-d1ca-4880-9a01-c311268ebdb8)|✅ `Random` and `Binary`: `True` once the last `Get` could not read an entire record - reading the last byte is not enough. Error `52` for a file number that is not open.|
|6.1.2.5.1.4|[FileAttr](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a9b90423-b7ed-4d5e-850a-edd7e8e76cb3)|✅ Only `ReturnType` `1`: `2`, the handle, is error `5` in 64-bit **MS-VBA**, and in RD-VBA, which has none to give; so is any other.|
|6.1.2.5.1.5|[FileDateTime](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c2f27ce8-ae8e-4698-993d-c488df2535bd)|✅ When it was last modified, a directory's too. Error `53` for what does not exist, `52` for a pattern.|
|6.1.2.5.1.6|[FileLen](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/59ca6577-d781-4b83-b384-edca362726a4)|✅ The length on the file system, not what a channel has written yet: `0` for a file open for `Output`, the length it had for one open for `Append`, and what was written for one open `Binary` - which is not what the specification says ("the size of the file immediately before it was opened"), but what **MS-VBA** gives. A directory's is `0`. Error `53`, `52` as `FileDateTime`.|
|6.1.2.5.1.7|[FreeFile](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1d34a82b-051a-4b36-9c9a-0d35819c2f08)|✅|
|6.1.2.5.1.8|[Loc](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/911ae09f-00f1-4e77-8f51-5cd5dbd761c6)|✅ `Random`, `Binary`: the last record or byte read or written, `0` before either. Sequential, as **MS-VBA** counts it: `Input`, the bytes read divided by 128, plus 1 - the first block is read when the file is opened; `Output` and `Append`, the bytes written divided by 128.|
|6.1.2.5.1.9|[LOF](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9094d070-3a35-44c2-a2ab-75497b8aa58b)|✅ What has been written included.|
|6.1.2.5.1.10|[Seek](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/70ceea52-55ed-4ea4-a9e0-804586da69a6)|✅ For `Append`, counted from where the appending began, as **MS-VBA** counts it: `1` once the file is opened.|


## 6.1.2.5.2 Public Subroutines

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.5.2** Public Subroutines](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/edf27283-49d4-4aea-a681-721532898a66).

|§|Member|Notes|
|---|---|---|
|6.1.2.5.2.1|[ChDir](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/167a7437-6a7f-4b0a-919a-4bfeeea57622)|✅ Error `76` for a directory that does not exist, or a zero-length `String`.|
|6.1.2.5.2.2|[ChDrive](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/43cb6c5a-3cff-442f-80ba-4817a19ccd4e)|✅ Error `68` for a letter that is no drive.|
|6.1.2.5.2.3|[FileCopy](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4f065ccf-ce8e-4ef9-afef-c5f133bdd768)|✅ Replaces the destination. A source open for `Input` is copied - the specification says "if the Source file is currently open, an error occurs", which **MS-VBA** raises (`55`) for any other mode only; so it does for an open destination. Error `53` for a source that does not exist, `52` for a pattern, `76` for a destination whose directory does not exist, `75` for one that is a directory.|
|6.1.2.5.2.4|[Kill](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/56c9177f-946f-4545-8b22-b22d5c3828ad)|✅ Not a directory, nor a hidden or a system file: `Kill "*"` leaves them. Error `53` when nothing matches, `55` when a match is open, `75` when one is read-only.|
|6.1.2.5.2.5|[MkDir](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5c10bd46-1471-4785-9663-e20699c1207b)|✅ Error `75` when a directory or a file of the name exists, `76` when the directory it would be in does not.|
|6.1.2.5.2.6|[RmDir](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/da2d0043-0672-4783-ba76-ae823663d073)|✅ Error `75` for one that holds a file or a directory, `76` for one that does not exist - a file, a pattern.|
|6.1.2.5.2.7|[SetAttr](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6ba633de-e14f-41f8-a301-32aad27e4bf7)|✅ Exactly the attributes given: `vbReadOnly`, `vbHidden`, `vbSystem`, `vbArchive` - any other is error `5`. A file open for `Input` is given them - the specification says setting those of an open file is an error, which **MS-VBA** raises (`55`) for any other mode only. Error `53` for what does not exist, `52` for a pattern.|

---
> ⏮️ [**RD-VBAL §6.1.2.4** DateTime Module](rd-vbal.6.1.2.4.datetime-module.md) | ⏭️ [**RD-VBAL §6.1.2.6** Financial](rd-vbal.6.1.2.6.financial.md)
