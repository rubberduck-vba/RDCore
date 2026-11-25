# 5.1 Module Body Structure

This section corresponds to [**MS-VBAL §5.1** Module Body Structure](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/700b6540-33f4-4c9b-a151-cf1ee646e5ee).

## Several modules in one file

MS-VBAL defines the body of one module (`procedural-module-body`, `class-module-body`). **RD-VBA addition**, in every dialect: a source file may also
declare modules in blocks, each with a body of its own. What is outside every block is the file's own module.

```vb
' Backup.bas: the file's own module, and two modules declared in blocks
Public Sub Main()
    Logger.Log "start"
End Sub

Module Logger
Public Sub Log(ByVal text As String)
    Debug.Print text
End Sub
End Module

Class Account ' the ledger
Public Balance As Currency
End Class
```

### Syntax

|Header|Declares|Ends at|
|---|---|---|
|`Module Name`|A standard module.|The first `End Module` after the header.|
|`Class Name`|A class module.|The first `End Class` after the header.|

- The header and the end are each on a line of their own, with nothing else on it but a `'` comment.
- Blocks do not nest: a header inside a block is part of the block's code.
- A header that no end of its kind follows declares no block. A line that continues the line before it (` _`) is neither a header nor an end.
  Such a line stays in the file's own module, where it is a syntax error.

### Static Semantics

|Aspect|Rule|
|---|---|
|Name|The header names the module. An `Attribute VB_Name` inside a block does not rename it, and is not the name of the file's own module either.|
|Identity|A synthetic path under the file: the file's URI with the module's name as one more segment (`file:///c:/ws/Backup.bas/Logger`).|
|File|No file has that path. The module's language, and whether it is a script, are those of its file.|
|File-level statements|A block's module has none, in a `.vbs` too: a statement outside its procedures is a syntax error, as in a `.bas`.|
|Positions|Every node of a block's module has its position in the file, so an editor shows it where it is written.|
|Syntax errors|Each module is parsed on its own. An error in one block leaves the trees of the other modules as they are.|
|Header and end lines|They are in no module's tree. The parse keeps each as trivia of the file, and a comment on either line is a comment of the file's own module.|
|Defining|The language server, `rdc run`, `rdc build` and `rdc bench` define each module of the file on its own: the file's own module first, then each block's, named by its header. A built program carries the modules of each file.|
|Editing|A block that an edit takes out of its file, or renames, takes its module out of the environment host, unless another file of the project declares that name. A file that leaves the workspace takes out every module of it.|

> [!NOTE]
> **Not implemented.** The analysis session of a project other than the primary one keeps the module of a block that an edit takes out of its file. VBA and VB6
> have no blocks. No diagnostic reports a block in a VBA or VB6 workspace, and no code action extracts a block into a module file of its own. Two blocks of
> the same name are not reported either.

### Implementation

|Type or member|Role|
|---|---|
|[`FileBlocks`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Workspace.FileBlocks.html)|Finds the blocks of a file's text.|
|[`ModuleParseResult.BlockModules`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.ModuleParseResult.html)|The module of each block: its name, kind, address, range and tree.|
|[`RDCoreDocument`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Workspace.RDCoreDocument.html)|Maps a file to its modules and back: `BlockUri`, `FileOf`, `ModuleAt`.|
|[`BlockBoundaryTriviaNode`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Abstract.BlockBoundaryTriviaNode.html)|A block's header or end line, before its comment, as trivia of the file.|
|`ModuleParser`|Parses the file's own module with the lines of its blocks blank, then each block from the line after its header.|
