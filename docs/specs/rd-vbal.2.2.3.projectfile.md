# 2.2.3 ProjectFile

> [!NOTE]
> This specification may be incomplete at this time.

RD-VBA discovers a _file system folder_ as a _workspace folder_ when the folder contains a text file named `.rdproj` that can be successfully _deserialized_ into a [`ProjectFile`](../api/RDCore.SDK.Workspace.ProjectFile.html). That folder is then the _root_ of that _workspace folder_.

A folder that contains no `.rdproj` is the project it holds: RD-VBA synthesizes one in memory ([`IProjectFileLoader.LoadOrSynthesizeAsync`](../api/RDCore.SDK.Workspace.IProjectFileLoader.html)), and opening the folder writes nothing into it.

- Every source file of the workspace's language (**RD-VBAL §2.0.2.1.1**) under the folder, at any depth, is a module. A folder whose name starts with a dot (`.git`, `.vscode`) is not searched, nor a link to another folder.
- Each folder that holds a module, at any depth, is a folder of the project.
- Paths are relative to the root, separated by `/`, in ordinal order. Of two files that declare the same `VB_Name`, the first in that order is the module (**RD-VBAL §2.2.3.3**).
- The project is named after the folder when the folder's name is a valid _identifier name_ other than `VBA`, and `VBAProject` otherwise, as the VBE names a new project.

The synthesized project is written to `.rdproj` on its first change, a file the client created, renamed or deleted (see [**RD-VBAL §2.0.2**](rd-vbal.2.0.2.client-server-capabilities.md#workspacedidcreatefiles-didrenamefiles-and-diddeletefiles)), and is a project file like any other from then on. The language server is the only writer of a `.rdproj`.

```javascript
{
  "Version": "",
  "Configuration": [],
  "ProjectInfo": RDCoreProject
}
```

|Member|Type|Description|
|---|---|---|
|`Version`|`string`|The RD-VBA _language core_ version the project file was _serialized_ with.|
|`Configuration`|array of `string`|The locations (relative paths) of any configuration files to bind at run-time.|
|`ProjectInfo`|[`RDCoreProject`](#2231-rdcoreproject)|An object that describes the content of an RD-VBA project.|

> [!TIP]
> This model is intended to natively support VB6 _project files_ (`.vbp`).

## 2.2.3.1 RDCoreProject

[`RDCoreProject`](../api/RDCore.SDK.Workspace.RDCoreProject.html) is a _serializable_ model representing an RD-VBA _project_.

```javascript
{
  "Name": "",
  "References": [RDCoreReference],
  "Modules": [RDCoreModule],
  "OtherFiles": [RDCoreFile],
  "Folders": []
}
```

|Member|Type|Description|
|---|---|---|
|`Name`|`string`|The name the project file was _serialized_ with.|
|`References`|array of [`RDCoreReference`](#2232-rdcorereference)|The project references.|
|`Modules`|array of [`RDCoreModule`](#2233-rdcoremodule)|The project source files.|
|`OtherFiles`|array of [`RDCoreFile`](#2234-rdcorefile)|Any non-source files included in the project.|
|`Folders`|array of `string`|The names of all the folders in the project, whether they contain source code files or not.|

> [!IMPORTANT]
> The `Name` of a project **must** be a valid _identifier name_. It **should not** be `VBA`, or any other _reserved identifier_ name.

The rule is worded "should not" because whether a _source project_ can reference a _different project_ that has the same name is explicitly specified as _host-dependent_ behavior. An RD-VBA _host environment_ **should** explicitly deny the addition of any such ambiguous project reference.

The _standard library_ symbols are present in a project whether or not its `.rdproj` mentions the library at all (see [**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.md)).

## 2.2.3.2 RDCoreReference

[`RDCoreReference`](../api/RDCore.SDK.Workspace.RDCoreReference.html) describes a _reference_ within a _project_.

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

|Member|Type (default)|Description|
|---|---|---|
|`Name`|`string`|The _identifier name_ (token) used in workspace source code to reference this library.|
|`Guid`|`string`|A _Globally Unique Identifier_ optionally identifying the referenced library in a _host-defined application registry_.|
|`AbsolutePath`|`string`|The full path to the physical location of the referenced library, if it exists.|
|`Major`|number (`0`)|The _major_ version number of the referenced library, if available.|
|`Minor`|number (`0`)|The _minor_ version number of the referenced library, if available.|
|`IsUnremovable`|boolean (`false`)|A _soft indicator_ marking the reference as _unremovable_ from an LSP _client_.|

> [!NOTE]
> 🧩 A supplied `Guid` necessarily refers to a COM registered library. Resolving such a library implies platform-specific _Windows Registry_ lookups.
> The _environment host_ may use _implementation-dependent_ alternative means to provide _symbols_ and _semantics_ for such references.

👉 An `RDCoreReference` for a reference to a [`VBHostProjectType`](../api/RDCore.SDK.Model.Types.Complex.VBHostProjectType.html) project must have the "unremovable" flag (`IsUnremovable`) set (see [**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md)).

The order in which project references appear in the `.rdproj` file of a _workspace folder_ determines the _reference priority_ (see [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).

[`IRuntimeSession`](../api/RDCore.SDK.Runtime.Abstract.Execution.IRuntimeSession.html)`.References` is the runtime-facing view of the `.rdproj` `RDCoreReference` list (see [**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)).

## 2.2.3.3 RDCoreModule

[`RDCoreModule`](../api/RDCore.SDK.Workspace.RDCoreModule.html) describes the modules (source files) of a _workspace folder_.

```javascript
{
  "Name": "",
  "Super": null
}
```

|Member|Description|
|---|---|
|`Name`|The _identifier name_ (token) used in workspace source code to reference this module.|
|`DocClassType`|A `string` value that can be parsed as a member of the [`DocClassType`](#22331-docclasstype-enum) enumeration.|

> [!IMPORTANT]
> The value of the `Name` property of an `RDCoreModule` **must** be unique across the entire _workspace_.

The `Name` of an `RDCoreModule` is always supplied by a `VB_Name` _attribute_ (see [**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md)). In case of a mismatch between `Name` and the value of the `VB_Name` attribute, _the attribute value always takes precedence_.

### 2.2.3.3.1 DocClassType Enum

> [!NOTE]
> 🧩 This `enum` type is an extension point: it is intended to be extended as additional _document modules_ are explicitly supported.

The [`DocClassType`](../api/RDCore.SDK.Workspace.DocClassType.html) enumeration defines constants that internally map _extensible_ (document) modules to certain specific class types:

|Name|Value|
|---|---|
|`Unknown`|0|
|`ExcelWorkbook`|1|
|`ExcelWorksheet`|2|
|`AccessForm`|3|
|`AccessReport`|4|

_Document modules_ can only be added to a VBA project via the _host application_ that defines them. Exporting a document module from an MS-VBA project via the _VBIDE Extensibility API_ produces a `.cls` file, which would then re-import as a _class module_.

Rubberduck (the VBE add-in) exported document modules with a `.doccls` extension, to distinguish the two class types. Document modules are distinguished from class modules because a document module's base class metadata is externally defined.

RD-VBA can load the necessary symbols for document module interfaces, but their implementation belongs to their respective _host application_.

> [!NOTE]
> RD-VBA cannot create a `Workbook` host document, nor a `Worksheet` module in its `Sheets` collection, because that is the responsibility of _Microsoft Excel_.

Instead, RD-VBA identifies the interfaces a document module requires using the `DocClassType` enum. This allows _static semantics_ to correctly identify all the members and available events of a document module.

Workspace source code that is directly dependent on a _host document_ necessarily requires an appropriate _host_ to evaluate correctly. In such cases, the RD-VBA runtime implementation may start an _automation host_ process as needed, if such a host exists in the runtime environment.

> 🎯 A more portable approach is to refactor MS-VBA legacy code so that any host-dependent calls are decoupled from the logic. RDCore semantic analysis capabilities should support all the diagnostics and refactoring tools needed to do this.

## 2.2.3.4 RDCoreFile

[`RDCoreFile`](../api/RDCore.SDK.Workspace.RDCoreFile.html) describes additional (non-source) files contained in a _workspace folder_, but not necessarily given to a _language server_ for processing.

> [!TIP]
> For example, an `RDCoreProject` could include `README.md`, `CONTRIBUTING.md`, and `LICENCE.md` text/markdown files as `RDCoreFile` entries. These files would always be bundled with the project, but ignored by the RD-VBA _language core_.

```javascript
{
  "Name": ""
}
```

|Member|Description|
|---|---|
|`Name`|The _identifier name_ (token) used in workspace source code to reference this module.|

---
> ⏮️ [**RD-VBAL §2.2.2** WorkspaceFile](rd-vbal.2.2.2.workspacefile.md) | ⏭️ [**RD-VBAL §2.3** Application Host](rd-vbal.2.3.application-host.md)
