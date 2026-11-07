# 2.2.1 Conventions

The **truth of an RD-VBA workspace is in the file system**. The organization of the folders designated as _workspaces_ constitutes meaningful metadata. It structures both the physical file system and the _workspace folders_ (projects) under a workspace.

A _workspace folder_ (project) may exist by itself, or under a _workspace_ directory.

|File|Describes, in LSP terms|Describes, in VBA terms|
|---|---|---|
|`.rdworkspace`|An LSP _workspace_|A `VBProjectGroup`|
|`.rdproj`|An LSP _workspace folder_|A `VBProject`|

These files **do not have a name, only an extension**. The **name of the folder** a `.rdworkspace` or `.rdproj` file is located in is the canonical _identifier name_ of the _workspace_ or _workspace folder_.

## Workspace root

The term _workspace root_ describes the folder that contains the `.rdworkspace` file:

|Context|_Workspace root_|
|---|---|
|File-level operations|The physical _file system location_ (full path) of the folder that contains the `.rdworkspace` file.|
|Most other contexts|An absolute file `Uri` pointing to the folder that contains the `.rdworkspace` file.|

## Folder names

> [!IMPORTANT]
> Because folder names are identifier names, _workspace folder_ (project) names **must** be valid `PascalCase` identifier names.

The uniqueness of the _workspace folder_ names under a given _workspace_ is mandated by the LSP specification, _not by the file system_.

> [!WARNING]
> RD-VBA **identifier names are semantically case-insensitive**. Folder names "`project1`" and "`Project1`" are **considered identical**, regardless of whether the underlying file system distinguishes them.
