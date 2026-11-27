# 5.1 Module Body Structure

This section corresponds to [**MS-VBAL §5.1** Module Body Structure](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/700b6540-33f4-4c9b-a151-cf1ee646e5ee).

## Several modules in one file

MS-VBAL defines the body of one module (`procedural-module-body`, `class-module-body`). **RD-VBA addition**, where a file may have statements outside
its procedures: a source file may also declare modules in blocks, each with a body of its own. What is outside every block is the file's own module.

|File|Has blocks|
|---|---|
|A `.vbs`, in any workspace|Always.|
|A module of a VBA or VB6 workspace|Only with the `Workspace.FileLevelStatements` setting on. Without it, a block's header and end lines are lines of the file's own module, and a syntax error there, as in MS-VBA.|

With the setting on, a VBA or VB6 module is a script, and has a `(Script)` entry point, only when it writes a statement outside its procedures; a class
module never is one. A `.vbs` is always a script.

```vb
' Backup.bas, the FileLevelStatements setting on: the file's own module, and two modules declared in blocks
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
|`[Public \| Private] [Partial] Module Name`|A standard module.|The first `End Module` after the header.|
|`[Public \| Private] [Partial] [Creatable] Class Name`|A class module.|The first `End Class` after the header.|

- The header and the end are each on a line of their own, with nothing else on it but a `'` comment.
- The modifiers come in this order only, each at most once. A line that writes them otherwise is no header.
- Blocks do not nest: a header inside a block is part of the block's code.
- A header that no end of its kind follows declares no block. A line that continues the line before it (` _`) is neither a header nor an end.
  Such a line stays in the file's own module, where it is a syntax error.

### Static Semantics

|Aspect|Rule|
|---|---|
|Name|The header names the module. An `Attribute VB_Name` inside a block does not rename it. A block without `Partial` named as another module of the project is [`VBC09386`](../diagnostics/vbc09386.md), at the name in its header; a `Partial` block of that name is a part of it ([Modules of parts](#modules-of-parts)).|
|Header lines|A block has no module file header and no attributes: an `Attribute`, `VERSION`, `BEGIN` or `Option Private Module` line inside a block is [`VBC00062`](../diagnostics/vbc00062.md).|
|Identity|A synthetic path under the file: the file's URI with the module's name as one more segment (`file:///c:/ws/Backup.bas/Logger`).|
|File|No file has that path. The module's language, and whether it is a script, are those of its file.|
|File-level statements|A block's module has none, in a `.vbs` too: a statement outside its procedures is a syntax error, as in a `.bas`.|
|Positions|Every node of a block's module has its position in the file, so an editor shows it where it is written. The language server's folding, selection ranges, outline and semantic tokens cover each module of the file, a block's included ([**RD-VBAL §2.0.2**](rd-vbal.2.0.2.client-server-capabilities.md#textdocumentfoldingrange)).|
|Syntax errors|Each module is parsed on its own. An error in one block leaves the trees of the other modules as they are. The language server analyzes each module of the file on its own, and reports each syntax error once, in the module it is in. An analyzer's finding about a block's module as a whole is on the line after its header, where its code starts; an edit of it that a waiting program does not take ([`RDC20001`](../diagnostics/rdc20001.md)) is on its header.|
|Header and end lines|They are in no module's tree. The parse keeps each as trivia of the file, and a comment on either line is a comment of the file's own module.|
|Defining|The language server, `rdc run`, `rdc build` and `rdc bench` define each module of the file: the file's own module first, then each block's, named by its header. The parts of one name are one module ([Modules of parts](#modules-of-parts)). A built program carries the modules its build assembled.|
|Editing|A block that an edit takes out of its file, or renames, takes its module out of its project's session in the environment host (the runtime session, or the project's analysis session), unless another file of the project declares that name. A file that leaves the workspace takes out every module of it.|
|Debugging|The debug adapter gives a breakpoint, or a line to go to, to the module whose lines hold it: a block's from its header to its end line, the file's own otherwise. A block's module runs on its own part of the parse. A block renamed by its header while the program waits is a module the program does not have: the program does not go on until it is run again.|

> [!NOTE]
> **Not implemented.** Completion and rename read each part of a module as a module of its own. No code action extracts a block into a module file of
> its own.

### Implementation

|Type or member|Role|
|---|---|
|[`FileBlocks`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Workspace.FileBlocks.html)|Finds the blocks of a file's text.|
|[`ModuleParseResult.BlockModules`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.ModuleParseResult.html)|The module of each block: its name, kind, address, range and tree.|
|[`RDCoreDocument`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Workspace.RDCoreDocument.html)|Maps a file to its modules and back: `BlockUri`, `FileOf`, `ModuleAt`.|
|[`BlockBoundaryTriviaNode`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Abstract.BlockBoundaryTriviaNode.html)|A block's header or end line, before its comment, as trivia of the file.|
|`ModuleParser`|Parses the file's own module with the lines of its blocks blank, then each block from the line after its header.|

## Modules of parts

**RD-VBA addition.** A `Partial` block is a part of the module of its name, in whichever file of the project it is written. The parts of one name are
one module: at most one plain part (a module file, or a block without `Partial`), and any number of `Partial` blocks.

```vb
' Program.bas
Private count As Long

Public Sub Main()
    Add 20
    Add 22
    Debug.Print count ' 42
End Sub
```

```vb
' Extra.bas
Partial Module Program
Private Sub Add(ByVal n As Long)
    count = count + n
End Sub
End Module
```

### Static Semantics

|Aspect|Rule|
|---|---|
|Kind|The plain part's, or the first part's when every part is `Partial`. A part of another kind is [`VBC09388`](../diagnostics/vbc09388.md).|
|Order|The plain part first, then the `Partial` parts in the order the project lists their files, each file's in the order it writes them.|
|Body|Every part's declarations, then every part's procedures: the module declares its names before its procedures, as one file writes them.|
|Scope|One module scope. A name two parts declare is [`VBC09301`](../diagnostics/vbc09301.md), even where one of them is written at the lines of an `#If` branch of the other part: branches are of one file.|
|Files|Each node keeps its part's file and position. A symbol, a dead `#If` branch and an error are each of their own part's file.|
|Options|An `Option` that a part states is the module's, for every part. Stated in two parts, it is stated twice: [`VBC09387`](../diagnostics/vbc09387.md).|
|Names|Two plain parts are two modules of one name, and so are two parts of one module in one file: [`VBC09386`](../diagnostics/vbc09386.md).|
|Refused|A module that `VBC09386`, `VBC09388` or `VBC09389` refuses does not load, none of its parts does, and a program that has it does not run.|

The parts state the module's directives. Each part that states one states it the same way; another way is [`VBC09389`](../diagnostics/vbc09389.md).

|Directive|A module file states it with|A block states it with|When no part states it|
|---|---|---|---|
|Exposed (`Class`)|`VB_Exposed`|`Public`, or `Private` for not exposed|Not exposed.|
|Creatable (`Class`)|`VB_Creatable`|`Creatable`|Creatable when a module file is a part, as a file that states nothing is; a class of blocks alone is not creatable.|
|Private (`Module`)|`Option Private Module`|`Private`, or `Public` for not private|Public.|
|Default instance|`VB_PredeclaredId`|`'@PredeclaredId`|None.|
|Extensible|`VB_Extensible`|No block form.|Not extensible.|
|Description|`VB_Description`, else `'@ModuleDescription("...")`|`'@ModuleDescription("...")`|None.|

A member's `'@Description("...")` describes it in its own part's file. `'@Exposed` in a `Class` block has no effect: the header alone says whether
other projects see the class.

> [!NOTE]
> **Not implemented.** No diagnostic reports a part that restates a directive the same way, `Creatable` on a class no other project sees, or
> `'@Exposed` in a `Class` block.

### Implementation

|Type or member|Role|
|---|---|
|[`BlockModule.IsPartial`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.BlockModule.html)|Whether a block's header writes `Partial`, with the visibility and `Creatable` it writes, and where it writes each word (`NameRange`, `KindRange` and the others).|
|[`DocStrings`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.DocStrings.html)|Reads the descriptions of a module, its members and its variables, from their attributes or their annotations.|
|`ModuleAssembly`|Makes one module of the parts of one name, with its directives and what its parts write to no effect, or refuses it with its errors.|
|`ProjectModules`|Assembles the modules of a project for the language server, and gives the errors of each module it refuses, by the file each is in.|
|`SyntaxTreeSymbolProvider`|Gives each symbol the file of the node that declares it.|
