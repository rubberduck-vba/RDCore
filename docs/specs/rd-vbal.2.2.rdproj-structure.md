# 2.2. RDPROJ Structure
> [!NOTE]
> This specification may be incomplete at this time.

Because the **truth of a RD-VBA program is its source code**, RD-VBA source code lives directly on the _file system_, within a structured system of folders. Since doing this completely decouples a VBA project from its _host document_, there needs to be a data structure somewhere that _explicitly_ defines the structure and content of a project.


---
## 2.2.1. Conventions

- Because the **truth of a RD-VBA workspace is in the file system**, the oganization of the folders designated as _workspaces_ constitutes meaningful metadata that structures both the physical file system and the _workspace folders_ (projects) under it.
- A _workspace folder_ (project) may exist in itself, or under a _workspace_ directory.
- A **.rdworkspace** file describes a LSP _workspace_, or a `VBProjectGroup`.
- A **.rdproj** file describes a LSP _workspace folder_, or a `VBProject`.
- These files **do not have a name, only an extension**: instead, the **name of the folder** they are located in _is_ the canonical _identifier name_ of the _workspace_ or _workspace folder_.
- The term _workspace root_ describes either:
  - The physical _file system location_ (full path) of the folder that contains the `.rdworkspace` file, in the context of file-level operations;
  - An absolute file `Uri` pointing to that location, in most other contexts.


> [!IMPORTANT]
> _Workspace Folder_ (project) names **must** therefore be valid `PascalCase` identifier names, however the uniqueness of the _workspace folder_ names under a given _workspace_ is mandated by the LSP specification, *not by the file system*.

> [!WARNING]
> Because RD-VBA **identifier names are semantically case-insensitive**, folder names "`project1`" and "`Project1`" are **considered as identical** _regardless_ of whether or not the underlying file system supports it. 


---
## 2.2.2. WorkspaceFile

RD-VBA is able to discover a _file system folder_ as a _workspace_ when the folder contains a text file named `.rdworkspace` that can be successfully _deserialized_ into a `WorkspaceFile`. This folder is then the _root_ of that _workspace_.

The model describes the content of the workspace:

```javascript
{
  "Folders": [],
  "Files": []
}
```

| Member | Description |
| --- | --- |
| Folders | An array of folder names that are directly under the _root_. Each one of these folder is expected to contain a _serialized_ `ProjectFile`. |
| Files | An array of file names from the _root_ folder, that are included in the workspace. |

> [!TIP]
> This model is intended to natively support VB6 _project groups_ (.vbg).


---
## 2.2.3. ProjectFile
> This specification may be incomplete at this time.

RD-VBA is able to discover a _file system folder_ as a _workspace folder_ when the folder contains a text file named `.rdproj` that can be successfully _deserialized_ into a `ProjectFile`. This folder is then the _root_ of that _workspace folder_. 

```javascript
{
  "Version": "",
  "Configuration": [],
  "ProjectInfo": RDCoreProject
}
```

| Member | Description |
| --- | --- |
| Version | A `string` value containing the RD-VBA _language core_ version the project file was _serialized_ with. |
| Configuration | A `string` value containing the location (relative path) of any configuration files to bind at run-time. |
| ProjectInfo | An object that describes the content of a RD-VBA project. |

> [!TIP]
> This model is intended to natively support VB6 _vbproject_ (.vbp).


### 2.2.3.1 RDCoreProject
A _serializable_ model representing a RD-VBA _project_.

```javascript
{
  "Name": "",
  "References": [RDCoreReference],
  "Modules": [RDCodeModule],
  "OtherFiles": [RDCoreFile],
  "Folders": []
}
```

| Member | Description |
| --- | --- |
| Name | A `string` value containing the name the project file was _serialized_ with. |
| References | An array of [RDCoreReference](#2232-rdcorereference) describing the project references.|
| Modules | An array of [RDCoreModule](#2232-rdcoremodule) describing the project source files. |
| OtherFiles | An array of [RDCoreFile](#2233-rdcorefile) describing any non-source files included in the project. |
| Folders | An array of `string` values containing the names of all the folders in the project, whether they contain source code files or not.|

> [!IMPORTANT]
> The `Name` of a project **must** be a valid _identifier name_ that **should not** be `VBA`, or any other _reserved identifier_ name.
> The reason for wording of the above statement is that whether a _source project_ can reference a _different project_ that has the same name, is explicitly specified as _host-dependent_ behavior. For all intents and purposes, a _RD-VBA host environment_ **should** very explicitly deny the addition of any such ambiguous project references.


### 2.2.3.2. RDCoreReference
Describes a _reference_ within a _project_.

```javascript
{
  "Name": "",
  "Guid": "",
  "AbsolutePath": "",
  "Major": 0,
  "Minor": 0,
  "IsUnremovable": false
}
```

| Member | Description |
| --- | --- |
| Name | The _identifier name_ (token) used in workspace source code to reference this library. |
| Guid | A _Globally Unique Identifier_ optionally identifying the referenced library in a _host-defined application registry_. |
| AbsolutePath | The full path to the physical location of the referenced library, if it exists. |
| Major | The _major_ version number of the referenced library, if available. |
| Minor | The _minor_ version number of the referenced library, if available. |
| IsUnremovable | A _soft indicator_ marking the reference as _unremovable_ from a LSP _client_. | 

> [!NOTE]
> 🧩 A supplied `Guid` necessarily refers to a COM registered library that implies platform-specific _Windows Registry_ lookups to resolve;
> The _environment host_ may use _implementation-dependent_ alternative means to provide _symbols_ and _semantics_ for such references.


### 2.2.3.2 RDCoreModule
Describes the modules (source files) of a _workspace folder_.

```javascript
{
  "Name": "",
  "Super": null
}
```

| Member | Description |
| --- | --- |
| Name | The _identifier name_ (token) used in workspace source code to reference this module. |
| DocClassType | A `string` value that can be parsed as a member of the `DocClassType` enumeration. |

>[!IMPORTANT]
> The value of the `Name` property of a `RDCoreModule` must be unique across the entire _workspace_ and is always supplied by a `VB_Name` _attribute_. In case of a mismatch with the value of a `VB_Name` attribute, _the attribute value always takes precedence_.


#### 2.2.3.2.1 DocClassType Enum
> [!NOTE]
> 🧩 This `enum` type is intended to be extended as additional _document modules_ are explicitly supported.

The `DocClassType` enumeration defines constants that internally map _extensible modules_ to certain specific class types:

|Name|Value|
|---|---|
|Unknown|0|
|ExcelWorkbook|1|
|ExcelWorksheet|2|
|AccessForm|3|
|AccessReport|4|

_Document modules_ can only be added to a VBA project via the _host application_ that defines them; exporting them from a MS-VBA project via the _VBIDE Extensibility API_ produces a `.cls` file that would then re-import as a _class module_. Rubberduck historically exported them with a `.doccls` extension to distinguish the two class types, since the base class metadata is externally defined.

While RD-VBA can load the necessary symbols for these interfaces, their implementation belongs to their respective _host application_. In other words, RD-VBA cannot create a `Workbook` host document nor a `Worksheet` module in its `Sheets` collection, because that's the job of _Microsoft Excel_.

Instead, RD-VBA identifies the required interfaces using this `enum`, and this allows _static semantics_ to correctly identify all the members and available events. 

Workspace source code that is directly dependent on a _host document_ necessarily requires an appropriate _host_ to evaluate correctly; in such cases the RD-VBA runtime implementation is free to fire up an _automation host_ process as needed, if such a host exists in the runtime environment.

> 🎯 A more portable approach would be to refactor the MS-VBA legacy code such that any host-dependent calls are decoupled from the logic. RDCore semantic analysis capabilities should provide ample support for all the diagnostics and refactoring tools that would be needed to do this.


### 2.2.3.3 RDCoreFile
Describes additional(non-source) files contained in a _workspace folder_ but not necessarily given to a _language server_ for processing.

> 💡 This means, for example, that a `RDCoreProject` could include `README.md`, `CONTRIBUTING.md`, and `LICENCE.md` text/markdown files that would always be bundled with the project, but ignored by the RD-VBA _language core_.

```javascript
{
  "Name": "",
}
```

| Member | Description |
| --- | --- |
| Name | The _identifier name_ (token) used in workspace source code to reference this module. |


---
> ⏮️ [**RD-VBAL§2.1** Computational Environment](rd-vbal.2.1.implicit-storage.html) | ⏭️ [**RD-VBAL§2.3** Application Host](rd-vbal.2.3.application-host.html)

