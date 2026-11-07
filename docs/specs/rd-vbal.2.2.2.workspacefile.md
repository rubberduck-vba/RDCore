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
|`Folders`|array|An array of folder names that are directly under the _workspace root_. Each of these folders is expected to contain a _serialized_ `ProjectFile` (see [**RD-VBAL §2.2.3** ProjectFile](rd-vbal.2.2.3.projectfile.md)).|
|`Files`|array|An array of file names from the _root_ folder that are included in the workspace.|

> [!TIP]
> This model is intended to natively support VB6 _project groups_ (`.vbg`).
