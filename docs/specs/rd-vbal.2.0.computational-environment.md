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

The RDCore platform is a set of cooperating processes — an LSP client (`rdc.exe` in _client mode_), the language server, the parsing server, the RD-VBA _environment host_ (`rdc.exe` in _host mode_), and any number of _extension_ servers. Every link between two of these processes is a JSON-RPC connection that carries the standard **LSP `initialize`/`initialized`** handshake _plus_ a second, **non-LSP handshake** that exchanges _platform capabilities_.

The LSP layer is kept _pure LSP_: platform capabilities do **not** ride on the LSP `initialize` `experimental` field. They are exchanged by a dedicated request immediately after `initialized`.

### 2.0.2.1 The `rdcore/platform/initialize` handshake

Once the LSP `initialized` notification has been sent on a platform connection, the _connecting_ side sends an `rdcore/platform/initialize` request (client → server) and awaits the response before considering the connection _ready_.

|Message|Shape|
|---|---|
|Request — [`PlatformInitializeParams`](../api/RDCore.SDK.Platform.Protocol.PlatformInitializeParams.html)|`ExpectedComponent`: the [`CoreServerComponent`](../api/RDCore.SDK.Client.CoreServerComponent.html) the caller believes it is connecting to. `Expected`: a [`CorePlatformClientCapabilities`](../api/RDCore.SDK.Client.CorePlatformClientCapabilities.html) describing the capabilities the caller expects the peer to provide.|
|Response — [`PlatformInitializeResult`](../api/RDCore.SDK.Platform.Protocol.PlatformInitializeResult.html)|`Component`: the peer's own [`CoreServerComponent`](../api/RDCore.SDK.Client.CoreServerComponent.html). `Provided`: the flat list of _capability type names_ (e.g. `"ParseFullDocument"`) the peer actually provides.|

The responding side builds `Provided` by _reflecting_ the `[assembly: ProvidesCorePlatformClientCapability<T>]` attributes declared on its entry assembly, so a component's capability set is a compile-time property of the build rather than runtime configuration. The `rdcore/platform/initialize` method itself is answered by a handler the SDK registers on every RDCore server (alongside the LSP `shutdown`, `exit`, and `$/setTrace` handlers).

> [!NOTE]
> The handshake is currently _informational_. The response is retained (`IRDCoreClientApp.PlatformInfo`) and logged, and `PlatformInitializeResult.Provides<T>()` lets a caller test for a capability, but the platform does not yet _refuse_ a connection whose peer reports the wrong component or a missing required capability. Enforcement is a later milestone.

### 2.0.2.2 Platform components

|`CoreServerComponent`|Process|Role|
|---|---|---|
|`ClientApp`|`rdc.exe` (default)|An LSP client. Cannot be started by another platform process. Also runs in _command mode_ (`rdc.exe <verb>`), which advertises the `CliCommand` capability.|
|`LanguageServer`|`RDCore.LanguageServer.exe`|Platform coordinator; owns the child servers.|
|`ParsingServer`|`RDCore.ParseServer.exe`|Stateless syntax service.|
|`EnvironmentHost`|`rdc.exe` with `RDCORE_MODE=host`|Owns the RD-VBA runtime environment.|
|`Extension`|_(varies)_|A platform extension server. Discovered from its `extension.manifest.json` and brought up by the language server during platform assembly.|

### 2.0.2.3 Defined capabilities

This catalogue is intended to _exhaustively_ document the platform capabilities the SDK defines. Each capability is a [`CorePlatformClientCapability`](../api/RDCore.SDK.Client.CorePlatformClientCapability.html) record and, where it implies an out-of-band request, a non-LSP method.

|Capability|Method|Provided by|Description|
|---|---|---|---|
|[`ParseFullDocument`](../api/RDCore.SDK.Client.ParseFullDocument.html)|`rdcore/parser/document`|`ParsingServer`|Lets the language server request a parse result containing the full syntax tree of a specified workspace document.|
|[`DefineSymbols`](../api/RDCore.SDK.Client.DefineSymbols.html)|`rdcore/host/symbols/define`|`EnvironmentHost`|Lets the language server send a module's member symbol descriptors to the environment host for definition in its runtime session.|
|[`CliCommand`](../api/RDCore.SDK.Client.CliCommand.html)|_(none — in-process CLI dispatch)_|`ClientApp`, `Extension`|Advertises that the declaring component contributes `rdc.exe` command-mode verbs. The CLI declares it for its native verbs; an extension declares it so `rdc.exe describe-ext` records the capability in its manifest.|

> [!NOTE]
> Anchored-offset (fragment) parsing and further environment-host runtime operations will be added here as they are implemented. The handshake for every capability listed is still _informational_ (see 2.0.2.1).

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
