# 2.2.2 WorkspaceFile

RD-VBA discovers a _file system folder_ as a _workspace_ when the folder contains a text file named `.rdworkspace` that can be successfully _deserialized_ into a `WorkspaceFile`. That folder is then the _root_ of that _workspace_.

The `WorkspaceFile` model describes the content of the workspace:

```javascript
{
  "Folders": [],
  "Files": []
}
```

|Member|Type|Description|
|---|---|---|
|`Folders`|array|The names of the folders directly under the _workspace root_ that are projects of the workspace, in order. Each of these folders is expected to contain a _serialized_ `ProjectFile` (see [**RD-VBAL §2.2.3** ProjectFile](rd-vbal.2.2.3.projectfile.md)).|
|`Files`|array|An array of file names from the _root_ folder that are included in the workspace.|

> [!TIP]
> This model is intended to natively support VB6 _project groups_ (`.vbg`).

## Folders

An entry of `Folders` is the name of a folder directly under the workspace root, written relative to it. The client refuses an entry that breaks this rule,
logs a warning that names the entry and the `.rdworkspace`, and loads the other folders.

|An entry that|Is refused as (`WorkspaceFolderRefusal`)|
|---|---|
|Is an absolute path (`C:\Lib`, `/lib`)|`Absolute`|
|Has a `..` segment (`..\Lib`)|`OutsideRoot`|
|Is `.`, or has a path separator (`Lib\Core`)|`NotDirectlyUnderRoot`|
|Names a folder the file lists earlier, in any letter case|`Duplicate`|
|Names no folder under the workspace root|`Missing`|

> [!NOTE]
> **Not implemented.** The language server does not publish `RDC31002` (WorkspaceFolderRefused) against the `.rdworkspace` for a refused entry.

## Opening a workspace

A client opened on a folder reads the `.rdworkspace` in that folder, or else the one in its parent folder when that file lists the folder. Its `initialize`
request then gives the language server:

- `rootUri`: the folder the client opened, which is the _primary project_. A client opened on the workspace root gives the first folder it loads.
- `workspaceFolders`: every folder the file lists and the client loads, in the order of the file.

A folder that no `.rdworkspace` describes this way is opened on its own: `rootUri` is that folder, and the request has no `workspaceFolders`.

## Implementation

|Type or member|Role|
|---|---|
|`WorkspaceFolderList.Read`|Reads the workspace a client opens on a folder: its folders, its refused entries, and its primary folder.|
|`RefusedWorkspaceFolder`|A refused entry and its `WorkspaceFolderRefusal`, with the warning the user is given.|
|`RDCoreClientApp.ReadWorkspaceFolders`|Reads the workspace of the platform's client app before the `initialize` request.|
|`RDCoreClientApp.OnWorkspaceFolderRefused`|Logs a refused entry as a warning; a client app overrides it to show its user.|
