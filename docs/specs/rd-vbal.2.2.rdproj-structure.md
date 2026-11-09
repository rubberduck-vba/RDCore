# 2.2 RDPROJ Structure

> [!NOTE]
> This specification may be incomplete at this time.

The **truth of an RD-VBA program is its source code**. RD-VBA source code lives directly on the _file system_, within a structured system of folders.

Keeping the source code on the file system completely decouples a VBA project from its _host document_. A data structure is therefore needed that _explicitly_ defines the structure and content of a project.

|File|Model|Specification|
|---|---|---|
|`.rdworkspace`|`WorkspaceFile`|[**RD-VBAL §2.2.2** WorkspaceFile](rd-vbal.2.2.2.workspacefile.md)|
|`.rdproj`|[`ProjectFile`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Workspace.ProjectFile.html)|[**RD-VBAL §2.2.3** ProjectFile](rd-vbal.2.2.3.projectfile.md)|

[`VBSourceProjectType`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.Complex.VBSourceProjectType.html) corresponds to the content of an RD-VBA workspace folder (`.rdproj`) (see [**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md)).

---
## In this section

|§|Title|
|---|---|
|2.2.1|[Conventions](rd-vbal.2.2.1.conventions.md)|
|2.2.2|[WorkspaceFile](rd-vbal.2.2.2.workspacefile.md)|
|2.2.3|[ProjectFile](rd-vbal.2.2.3.projectfile.md)|
