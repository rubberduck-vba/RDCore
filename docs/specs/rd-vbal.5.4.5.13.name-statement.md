# 5.4.5.13 Name Statement

> [!NOTE]
> **MS-VBAL does not specify this statement.** It is part of the grammar the parser accepts, and an extension of
> **MS-VBAL §5.4.5** File Statements, described by the
> [VBA language reference](https://learn.microsoft.com/en-us/office/vba/language/reference/user-interface-help/name-statement).
> Where that reference is silent, the choice is the platform's, and is marked as such.

`Name` renames a disk file, directory or folder, and moves it when the new path is somewhere else.

```vb
Name "C:\MYDIR\OLDFILE" As "C:\YOURDIR\NEWFILE"   ' Move and rename a file.
```

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html)|`Simple`|`Token`: `Name`. Two inputs: the old path name, then the new one.|

See [**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md).

## Runtime Semantics

Both operands are String expressions, Let-coerced to `String`. The new path name is the name the file or directory has afterwards.

|Rule|Effect|
|---|---|
|The old path names a file, or a directory.|It is renamed, and moved to the new path's directory when that is a different one.|
|The old path names neither.|Runtime error 53 (File not found), or 76 (Path not found) when the directory it is in does not exist.|
|The new path already exists.|Runtime error 58 (File already exists). Nothing is changed.|
|The directory the new path is in does not exist.|Runtime error 76 (Path not found). `Name` creates no file, directory or folder.|
|The old path is a file that is open, or a directory with an open file in it.|Runtime error 55 (File already open). It has to be closed first ([**RD-VBAL §5.4.5.2** Close and Reset Statements](rd-vbal.5.4.5.2.close-and-reset-statements.md)).|
|A directory is moved to another drive.|Runtime error 74 (Can't rename with different drive). A file can be moved across drives.|
|Either path has a `*` or a `?` in it.|Runtime error 52 (Bad file name or number). Neither is a pattern.|
|Access is refused, or the device or path stops it otherwise.|Runtime error 70 (Permission denied), or 75 (Path/File access error).|

> [!NOTE]
> The reference says that a wildcard is not allowed, and does not say which error it is. Error 52 is the platform's choice.

The open-file rule is the file-channel shim's, because the channels are what know which files are open
([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).

## Implementation

|Type or member|Role|
|---|---|
|[IFileChannels](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannels.html)`.TryRename`|Renames, and says why it could not (**RDCore.SDK**).|
|`RDCore.Runtime.Semantics.Statements.FileStatementRuntimeSemantics.ExecuteName`|Evaluates the operands and raises the error (**RDCore.Runtime**).|

---
> ⏮️ [**RD-VBAL §5.4.5.12** Get Statement](rd-vbal.5.4.5.12.get-statement.md) | ⏭️ [**RD-VBAL §5.5** Implicit coercion](rd-vbal.5.5.implicit-coercion.md)
