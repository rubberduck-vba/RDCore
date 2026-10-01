# 2.0.2 Client/Server Capabilities

🧩 RDCore operates on a **capability-driven host model**. The capabilities of platform extensions are negotiated with the RD-VBA _environment host_ (see [**RD-VBAL §1.1.1** Platform Extensions](rd-vbal.1.1.1.platform-extensions.md)).

The RDCore platform is a set of cooperating processes:

- an LSP client (`rdc.exe` in _client mode_);
- the language server;
- the parsing server;
- the RD-VBA _environment host_ (`rdc.exe` in _host mode_);
- any number of _extension_ servers.

Every link between two of these processes is a JSON-RPC connection. Every such connection carries two handshakes:

|Handshake|Protocol|Purpose|
|---|---|---|
|`initialize`/`initialized`|Standard LSP|The LSP connection handshake.|
|`rdcore/platform/initialize`|Non-LSP|Exchanges _platform capabilities_ (see [**RD-VBAL §2.0.2.1** The `rdcore/platform/initialize` handshake](#2021-the-rdcoreplatforminitialize-handshake)).|

The LSP layer of a platform connection is kept _pure LSP_. Platform capabilities are **not** carried in the `experimental` field of the LSP `initialize` request: they are exchanged by a dedicated request immediately after the LSP `initialized` notification.

An implementation that commands client-side _workspace edits_ must ensure that the LSP client supports the capabilities required for the requested edits (see [**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md)).

## 2.0.2.1 The `rdcore/platform/initialize` handshake

1. Once the LSP `initialized` notification has been sent on a platform connection, the _connecting_ side sends an `rdcore/platform/initialize` request (client → server).
2. The responding side answers with the component it is and the capabilities it provides.
3. The connecting side awaits the response before considering the connection _ready_.

|Message|Type|Member|Description|
|---|---|---|---|
|Request|[`PlatformInitializeParams`](../api/RDCore.SDK.Platform.Protocol.PlatformInitializeParams.html)|`ExpectedComponent`|The [`CoreServerComponent`](../api/RDCore.SDK.Client.CoreServerComponent.html) the caller believes it is connecting to.|
|||`Expected`|A [`CorePlatformClientCapabilities`](../api/RDCore.SDK.Client.CorePlatformClientCapabilities.html) describing the capabilities the caller expects the peer to provide.|
|Response|[`PlatformInitializeResult`](../api/RDCore.SDK.Platform.Protocol.PlatformInitializeResult.html)|`Component`|The peer's own `CoreServerComponent`.|
|||`Provided`|The flat list of _capability type names_ (e.g. `"ParseFullDocument"`) the peer provides.|

The responding side builds `Provided` by _reflecting_ the `[assembly: ProvidesCorePlatformClientCapability<T>]` attributes ([`ProvidesCorePlatformClientCapabilityAttribute<T>`](../api/RDCore.SDK.Client.ProvidesCorePlatformClientCapabilityAttribute-1.html)) declared on its entry assembly. A component's platform capability set is therefore a compile-time property of the build rather than runtime configuration.

The `rdcore/platform/initialize` method is answered by a handler ([`PlatformInitializeHandler`](../api/RDCore.SDK.Server.Handlers.Platform.PlatformInitializeHandler.html)) that the SDK registers on every RDCore server, alongside the LSP `shutdown`, `exit`, and `$/setTrace` handlers.

The handshake is _informational_:

- the response is retained, as [`IRDCoreClientApp`](../api/RDCore.SDK.Client.IRDCoreClientApp.html)`.PlatformInfo`;
- the response is logged;
- `PlatformInitializeResult.Provides<T>()` lets a caller test for a capability.

> [!NOTE]
> **Not implemented.** The platform does not refuse a connection whose peer reports the wrong component, or a missing required capability, in the `rdcore/platform/initialize` response.

## 2.0.2.1.1 The language

The platform serves several members of the BASIC family ([`SupportedLanguages`](../api/RDCore.SDK.Workspace.SupportedLanguages.html)): RD-VBA (`vba`, the
default), VB6 (`vb6`) and the platform's BASIC (`basic`, which an interactive shell is written in). The language is what decides the dialect's table: the name
of the standard library ([**RD-VBAL §6.0**](rd-vbal.6.0.standard-library.md)), where the variable an undeclared name declares lives
([**MS-VBAL §5.6.10**](rd-vbal.5.6.10.simple-name-expressions.md)), and which statements exist at all ([**RD-VBAL §5.4.5.8**](rd-vbal.5.4.5.8.print-statement.md)).

A client says which one it is in the `initializationOptions` of its LSP `initialize` request - the part of the protocol that exists for a setting no standard
capability describes ([RDCoreInitializationOptions](../api/RDCore.SDK.Platform.Protocol.RDCoreInitializationOptions.html)):

```json
{ "language": "basic" }
```

The server applies it before it builds anything from the request, and what the client sends wins over the server's own `Configuration:Workspace:Language`
setting (`--language` on its command line, which is how a server that a client spawns is told, and how the language server tells the servers it starts).
A language the platform does not serve is logged and ignored.

> [!NOTE]
> The language was once two settings: BASIC's one difference, the scope of an implicit declaration, was a setting of its own, made when BASIC was not a dialect the
> platform defined. It is now the language's, and the setting is gone.

## 2.0.2.2 Platform components

|`CoreServerComponent`|Process|Role|
|---|---|---|
|`ClientApp`|`rdc.exe` (default mode)|An LSP client. Cannot be started by another platform process. Also runs in _command mode_ (`rdc.exe <verb>`), in which it advertises the `CliCommand` capability.|
|`LanguageServer`|`RDCore.LanguageServer.exe`|The platform coordinator; owns the child servers.|
|`ParsingServer`|`RDCore.ParseServer.exe`|A stateless syntax service.|
|`EnvironmentHost`|`rdc.exe` with `RDCORE_MODE=host`|Owns the RD-VBA runtime environment.|
|`Extension`|_(varies)_|A platform extension server. Discovered from its `extension.manifest.json` and brought up by the language server during platform assembly.|

If the validation result of an extension is `NoFlags`, the host may initiate the LSP connection handshake and capabilities exchange with the extension server (see [**RD-VBAL §1.1.5** Extension Manifest](rd-vbal.1.1.5.extension-manifest.md)).

The **RDCore.Diagnostics** extension is always brought up during platform assembly (see [**RD-VBAL §2.6.5** Diagnostics Pipeline](rd-vbal.2.6.5.diagnostics-pipeline.md)).

## 2.0.2.3 Defined capabilities

This catalogue is intended to document every platform capability the SDK defines. Each capability is a [`CorePlatformClientCapability`](../api/RDCore.SDK.Client.CorePlatformClientCapability.html) record. Where a capability implies an out-of-band request, it also has a non-LSP method.

|Capability|Method|Provided by|Description|
|---|---|---|---|
|[`ParseFullDocument`](../api/RDCore.SDK.Client.ParseFullDocument.html)|`rdcore/parser/document`|`ParsingServer`|Lets the language server request a parse result for a source fragment it supplies directly. The parser never reads source from the filesystem. A request may optionally be anchored at a position within a larger document, so that the locations reported for a sub-range fragment are in that document's coordinates.|
|[`DefineSymbols`](../api/RDCore.SDK.Client.DefineSymbols.html)|`rdcore/host/symbols/define`|`EnvironmentHost`|Lets the language server send a module's member symbol descriptors to the environment host for definition in its runtime session (see [below](#rdcorehostsymbolsdefine)).|
|[`CliCommand`](../api/RDCore.SDK.Client.CliCommand.html)|_(none: in-process CLI dispatch)_|`ClientApp`, `Extension`|Advertises that the declaring component contributes `rdc.exe` command-mode verbs. The CLI (`rdc.exe`) declares it for its native verbs. An extension declares it so that `rdc.exe describe-ext` records the capability in the extension's manifest (see [**RD-VBAL §1.1.5** Extension Manifest](rd-vbal.1.1.5.extension-manifest.md)).|
|[`DiagnoseDocument`](../api/RDCore.SDK.Client.DiagnoseDocument.html)|`rdcore/diagnostics/document`|`Extension`|Advertised by a _diagnostics provider_: a platform extension whose manifest advertises this capability. A provider declares it with `[assembly: ProvidesCorePlatformClientCapability<DiagnoseDocument>]` (see [below](#rdcorediagnosticsdocument)).|

> [!NOTE]
> The handshake for every capability listed is _informational_ (see [**RD-VBAL §2.0.2.1** The `rdcore/platform/initialize` handshake](#2021-the-rdcoreplatforminitialize-handshake)).

Capability providers for extensions distributed through the RDCore Platform Cloud Infrastructure: see [**RD-VBAL §1.1.6** Capabilities Provider](rd-vbal.1.1.6.capabilities-provider.md).

### `rdcore/host/symbols/define`

`rdcore/host/symbols/define` resolves a declared type name as seen from the module being defined. It resolves the name against the standard library's own types as well as the intrinsic types (see [**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.md)).

The project scope is an ancestor of a module's own scope and of nothing else (see [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).

👉 In the host session, `Dim d As VbDayOfWeek` binds to the standard library's `VbDayOfWeek` type, not to [`VBUnknownType`](../api/RDCore.SDK.Model.Types.VBUnknownType.html).

### `rdcore/diagnostics/document`

Diagnostics use the LSP 3.17 pull model (`textDocument/diagnostic`). The language server sends the parsed `ModuleParseResult` to every registered provider over the internal `rdcore/diagnostics/document` request. Only this language-server-to-provider hop of the diagnostics pipeline is an RDCore request.

The request carries the parse result as a [`PlatformJson`](../api/RDCore.SDK.Platform.Protocol.PlatformJson.html) string, because the syntax tree is polymorphic. See [**RD-VBAL §2.6.5** Diagnostics Pipeline](rd-vbal.2.6.5.diagnostics-pipeline.md).

### `rdcore/session/execute`

The `rdcore/session/execute` result ([`ExecuteSessionResult`](../api/RDCore.SDK.Platform.Protocol.ExecuteSessionResult.html)) carries a run-time error's code, title, source, position, line number and a structured stack trace.

The interactive shell renders a run-time error from this result, as:

- an icon;
- the title, with the program's own line number;
- the diagnostic code and description (see [**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md));
- `Err.Source`;
- the stack trace.

---
> ⏮️ [**RD-VBAL §2.0.1** Supported Languages](rd-vbal.2.0.1.supported-languages.md) | ⏭️ [**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.md)
