# 2.0 RD-VBA Computational Environment
> [!NOTE]
> This specification may be incomplete at this time.

> **MS-VBAL 2. VBA Computational Environment**  
> VBA is a programming language used to define computer programs that perform computations that occur within a specific computational environment called a _VBA Environment_. A _VBA Environment_ is **typically hosted** and controlled by another computer application called the _host application_. The _host application_ controls and invokes computational processes within its hosted _VBA Environment_. The _host application_ can also make availabel whtin its hosted _VBA Environment_ computational resources that enable VBA programs to access _host application_ data and host computational processes. The remainder of this section defines the key computational concepts of the _VBA Environment_.

👉 A **RD-VBA** program does run inside a *host*, but that host is `rdc.exe` rather than a _Microsoft Office_ application. This _does_ have yet-unresolved implications with regards to _run-time interoperability_, but should not affect general _semantic compatibility_.

> 🎯 `rdc.exe` is a command-line interface (CLI) application whose role is to **assemble and host** the _library_ that is defined by the source code in a _workspace program_. **This application is a work in progress**.

- In RD-VBA the concepts of a _workspace_ and of a _workspace folder_ are defined by the _Language Server Protocol_ (LSP v3.17);
- A _workspace program_ is an executable in-memory representation of a RD-VBA _workspace_;

> [!TIP]
> In LSP, a **Workspace Folder** corresponds essentially to a `VBProject`, and a **Workspace** corresponds to a _project group_.

This means a RD-VBA project must necessarily stand on its own and _physically exist_ in the file system, which constitutes a _fundamental paradigm shift_ for VBA code.

---
## 2.0.1 Supported Languages

A **RD-VBA** _environment host_ may configure language-level _restrictions_ or _extensions_, depending on the _capabilities_ of the _host application_:

- `VBA` refers to the _Visual Basic for Applications_ language as per the **MS-VBAL** language specification;
- `VB6` largely refers to the same language definition, without the restrictions around attribute semantics and with a limited set of additional semantics;
- `VBX` refers to _extended RD-VBA_; an _environment host_ that signals support for this language code may support semantics that would be _illegal_ in **VB6** or **VBA**;
- `VBS` refers to a _diminished_ language specification that removes `Option Explicit` and _declared types_, forcing the use of _duck-typing_ using implicit `Variant` declarations;
- `BASIC` refers to a _diminished_ language specification that removes _procedure scopes_, forcing the use of `REM` for comments (this makes _annotations_ unavailable), _line numbers_ and `GoSub`/`Return` for control flow; `Do...Loop` and `Do...While` constructs are undefined, forcing the use of `While...Wend` constructs; etc.

This list is _prioritized_ but not intended to be exhaustive; additional _dialects_ may be supported by different **RD-VBA** _hosts_.

> 🎯 The _scope_ of the **RDCore SDK** minimally covers `VBA`, _then_ `VB6`, _then_ `VBX`, and so on.  
> 👉 The LSP paradigm shift _alone_ brings RD-VBA much closer to how VB6 works already.  


---
## 2.0.2 Client/Server Capabilities
> [!NOTE]
> This documentation is incomplete.

This section intends to exhaustively document all supported RDCore platform capabilities.

|Capability|Description|Platform Version|
|---|---|---|
| | | |

> [!NOTE]
> **First and third party extensions** distributed through the **RDCore Platform Cloud Infrastructure** _MAY_ use a _capability provider_ that _MAY_ validate the availability of certain advanced capabilities by **requiring 2FA authentication**, the validation of an **active subscription** (free or paid), and the validation of the _signed build_ against the certified distribution channel build.


---
## In this section
- [**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.html)
- [**RD-VBAL §2.2** Project Structure](rd-vbal.2.2.rdproj-structure.html)
- [**RD-VBAL §2.3** Application Host](rd-vbal.2.3.application-host.html)
- [**RD-VBAL §2.4** Static Types](rd-vbal.2.4.static-types.html)
- [**RD-VBAL §2.5** Runtime Values](rd-vbal.2.5.runtime-values.html)


---
> ⏮️ [**RD-VBAL §1.0** Introduction](rd-vbal.1.0.introduction.html) | ⏭️ [**RD-VBAL §3.0** Syntax Tree](rd-vbal.3.0.syntax-tree.html)  
