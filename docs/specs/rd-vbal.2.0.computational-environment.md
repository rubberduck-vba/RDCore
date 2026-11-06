# 2.0 RD-VBA Computational Environment

> [!NOTE]
> This specification may be incomplete at this time.

> [**MS-VBAL §2** VBA Computational Environment](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/46dd2a34-a53c-4cc8-8a59-fcbbb5fdae6d)  
> VBA is a programming language used to define computer programs that perform computations that occur within a specific computational environment called a _VBA Environment_. A _VBA Environment_ is **typically hosted** and controlled by another computer application called the _host application_. The _host application_ controls and invokes computational processes within its hosted _VBA Environment_. The _host application_ can also make available within its hosted _VBA Environment_ computational resources that enable VBA programs to access _host application_ data and host computational processes. The remainder of this section defines the key computational concepts of the _VBA Environment_.

## Host

👉 An RD-VBA program runs inside a _host_, but that host is `rdc.exe` rather than a _Microsoft Office_ application. This should not affect general _semantic compatibility_.

> [!NOTE]
> Hosting RD-VBA programs in `rdc.exe` rather than a _Microsoft Office_ application has unresolved implications with regard to _run-time interoperability_.

> 🎯 `rdc.exe` is a command-line interface (CLI) application whose role is to **assemble and host** the _library_ that is defined by the source code in a _workspace program_.

🎯 `rdc.exe` is an RD-VBA _environment host_ (see [**RD-VBAL §1.0** Introduction](rd-vbal.1.0.introduction.md)). In the **RDCore** ecosystem, it is the default RD-VBA _environment host_ (see [**RD-VBAL §1.1.1** Platform Extensions](rd-vbal.1.1.1.platform-extensions.md)).

_Compilation_ in RD-VBA is the responsibility of the _environment host_, i.e. the `rdc.exe` console client. Compilation is normally not a concern for any other RD-VBA client or IDE (see [**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md)).

## Workspaces

In RD-VBA, the concepts of a _workspace_ and of a _workspace folder_ are defined by the _Language Server Protocol_ (LSP v3.17). A _workspace program_ is an executable in-memory representation of an RD-VBA _workspace_.

> [!TIP]
> In LSP, a **Workspace Folder** corresponds essentially to a `VBProject`, and a **Workspace** corresponds to a _project group_.

Because workspaces and workspace folders are LSP concepts, an RD-VBA project must necessarily stand on its own and _physically exist_ in the file system. This constitutes a _fundamental paradigm shift_ for VBA code.

See [**RD-VBAL §2.2** RDPROJ Structure](rd-vbal.2.2.rdproj-structure.md) for the files that describe a workspace and its workspace folders.

---
## In this section

|§|Title|
|---|---|
|2.0.1|[Supported Languages](rd-vbal.2.0.1.supported-languages.md)|
|2.0.2|[Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md)|
|2.1|[Implicit Storage](rd-vbal.2.1.implicit-storage.md)|
|2.2|[RDPROJ Structure](rd-vbal.2.2.rdproj-structure.md)|
|2.3|[Application Host](rd-vbal.2.3.application-host.md)|
|2.4|[Static Types](rd-vbal.2.4.static-types.md)|
|2.5|[Runtime Values](rd-vbal.2.5.runtime-values.md)|
|2.6|[Diagnostics](rd-vbal.2.6.diagnostics.md)|
