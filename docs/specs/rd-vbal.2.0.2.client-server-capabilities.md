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
default), VB6 (`vb6`) and the platform's BASIC (`basic`, which an interactive shell is written in). The language is what decides the dialect's table: where the variable an undeclared name declares lives
([**MS-VBAL §5.6.10**](rd-vbal.5.6.10.simple-name-expressions.md)), and which statements exist at all ([**RD-VBAL §5.4.5.8**](rd-vbal.5.4.5.8.print-statement.md)).
The standard library is not part of the table: it is `VBA` whatever the language ([**RD-VBAL §6.0**](rd-vbal.6.0.standard-library.md)).

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
|[`SessionInteraction`](../api/RDCore.SDK.Client.SessionInteraction.html)|`rdcore/session/interaction/msgBox`, `.../inputBox`|`LanguageServer`|Relays the dialogs a program opens (`MsgBox`, `InputBox`) to a client that expects this capability, which answers them. The interactive shell expects it, and asks at its console (see [below](#rdcoresessioninteraction)).|
|[`SessionCompletion`](../api/RDCore.SDK.Client.SessionCompletion.html)|`rdcore/session/completion`|`LanguageServer`|Lists what can be written at a position of a module the client supplies, with the list `textDocument/completion` answers with. For a line typed at a prompt, which is in no document (see [below](#textdocumentcompletion-and-rdcoresessioncompletion)).|

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

### `textDocument/completion` and `rdcore/session/completion`

The language server answers `textDocument/completion` (**LSP 3.17** §Completion Request) for the documents it holds, a `.rdc` program included. Its trigger characters are `.` and `!`. The list is complete (`isIncomplete = false`): a client narrows it by what is typed after it. Items are not resolved (`resolveProvider = false`).

What the list offers depends on what is being written, and where. The language server reads two things:

- **The line before the cursor** ([`CompletionSite`](../api/RDCore.SDK.Model.Symbols.Completion.CompletionSite.html)): the name being typed, and the statement it is in, as tokens ([`CompletionStatement`](../api/RDCore.SDK.Model.Symbols.Completion.CompletionStatement.html)). Is a statement, an operand or an operator next?
- **Where the line is in the module** ([`CompletionPlacement`](../api/RDCore.SDK.Model.Symbols.Completion.CompletionPlacement.html)): in the declarations section or in a procedure, which kind of procedure, which blocks are open around it, and the procedure's labels. This is read off the text, not the parse: a `For` whose `Next` is not written yet is still open.

|Written|Offered|
|---|---|
|A declaration, in the declarations section|What can be declared there. `Option` anywhere in it, with only the options the module does not state yet: each is stated once at most. `Event`, `Implements`, `Friend` and `WithEvents` in a class module only. No statement and no name.|
|A statement, in a procedure|The names visible from the procedure, nearest first (its locals and parameters, the module's members, the project's, the libraries', the standard library's). The statement keywords. The statements of the blocks the line is in (`Next` and `Exit For` in a `For`, `Loop` and `Exit Do` in a `Do`, `Case` and `End Select` in a `Select Case`, `Else` and `End If` in a block `If`). `Exit` and `End` for the kind of procedure. `Me` and `RaiseEvent` in a class module only.|
|An operand|The names, and the keywords an expression starts with (`Not`, `New`, `Nothing`, `True`). `AddressOf` only in an argument.|
|After a complete operand|The operators (`And`, `Mod`, `Like`, `Is`), and the keyword the statement goes on with (`Then`, `To`, `Step`, `In`). No name.|
|A name being declared (`Dim `, `Sub `, `Const `)|Nothing: it is a new name. After it, `As`. In a parameter list, `ByVal`, `ByRef`, `Optional` and `ParamArray`.|
|A statement's own words|`Exit ` the blocks and the procedure it can exit; `End ` what it can end; `GoTo `, `GoSub `, `Resume ` and `On Error GoTo ` the procedure's labels and line numbers; `RaiseEvent ` the class's events; `Implements ` and `TypeOf x Is ` the classes; `Option `, `Property `, `Declare `, `Open … For ` their keywords.|
|A member, after `.` or `!`|The members of the type of the expression before it (`w.Grow(2).Parts.`), or the members of the project or module it names (`VBA.`, `Module1.`). After a `.` with nothing before it, the members of the object of the enclosing `With` block.|
|A type, after `As`|The intrinsic types; the classes, user-defined types and Enums the project and the libraries declare; the projects and the libraries, which qualify a type name; and `New`. No procedural module, local or other keyword.|
|A class, after `New`|The classes that can be created, and the projects and the libraries.|
|In a comment or a string literal|Nothing.|

Which keyword is legal where is decided by [`CompletionRules`](../api/RDCore.SDK.Model.Symbols.Completion.CompletionRules.html). A `Private` member is offered only in its own module. A member a library hides (`VB_MemberFlags`) is never offered.

An `Object` or a `Variant` has no members known when the code is written. For a variable of one, the list offers the members of every class the module `Set`s it to an object of with `CreateObject` and a constant identifier (`Set o = CreateObject("Scripting.Dictionary")`), wherever the module does it ([`ObjectClassInference`](../api/RDCore.SDK.Semantics.Static.ObjectClassInference.html)). A class of a library the project does not reference is read from the library's description, and its names are still not names of the project. This is a fact for tooling only: the variable keeps its declared type, nothing about the class is a compile error, and the member stays late-bound when the code runs.

The line being typed seldom parses. The language server takes it out of the module before the module is parsed. For a member access, it puts it back as an assignment of the expression the member is accessed on, whose type comes from the static semantics. The module is composed with the rest of the workspace and the referenced libraries. Nothing of it is sent to the environment host.

`rdcore/session/completion` ([`SessionCompletionParams`](../api/RDCore.SDK.Platform.Protocol.SessionCompletionParams.html)) answers the same list for a module the client supplies with its source. The module stands for the workspace module of the same name. The interactive shell asks it for the line at its prompt, which is not in the program until it is entered.

A line at a client's prompt can also be one of the client's own commands. A client registers the verbs of its commands with `rdcore/session/commands` ([`SessionCommandsParams`](../api/RDCore.SDK.Platform.Protocol.SessionCommandsParams.html)), each described by a [`VerbDescriptor`](../api/RDCore.SDK.Platform.Commands.VerbDescriptor.html): the forms its arguments take, which are keywords, choices, line numbers, expressions, paths and text.

A verb is registered once. It is registered again only when its descriptor changes (a choice of what the client has more or fewer of), and then it replaces what it was. A completion request names the verb and what is typed after it (`Command`), and nothing more of the verb travels. The language server completes each argument as described ([`VerbCompletion`](../api/RDCore.SDK.Platform.Commands.VerbCompletion.html)):

- a keyword or a choice with its values;
- a line number with the numbered lines of the module, each shown with its statement;
- an expression where it is evaluated: before the line its form names (`EvaluatedAt`), in that line's procedure and blocks, which is where a breakpoint's program waits. An expression that names no line, or a line the module does not have, is completed at module level: the module's names and the project's, and no procedure's locals;
- a path or text with nothing.

A verb the client did not register is offered nothing. The interactive shell registers `BREAK` (`CLEAR`, `TOGGLE <line>`, `<line> [condition]`, its condition evaluated at its line) and `THEME` (`LIST`, `<theme>`) the first time one is completed. A command it does not describe is offered nothing after its verb.

### `rdcore/session/interaction`

A program runs in the environment host; the person it asks is at a client. A `MsgBox` or an `InputBox` travels in two hops, and the program waits for the answer as it waits for a dialog:

1. The environment host asks the language server (`rdcore/host/interaction/msgBox`, `rdcore/host/interaction/inputBox`).
2. The language server asks its client (`rdcore/session/interaction/msgBox`, `.../inputBox`) when the client expects [`SessionInteraction`](../api/RDCore.SDK.Client.SessionInteraction.html). Otherwise it answers that nobody was asked (`Handled = false`), and the environment host shows the dialog of the platform it runs on: a Windows dialog on Windows, and none elsewhere (the dialog answers as though it had been confirmed at once).

The request carries the lines the program completed before it opened the dialog (`Output`), so that the client shows them first; they are not repeated in the answer to `rdcore/session/execute`. An `InputBox` that was cancelled answers `Cancelled = true`, which the program is told with `vbNullString`.

The interactive shell draws a dialog as a box with a border, centred on the console, with its title in the top edge and its message wrapped inside. A `MsgBox` ends with a row of buttons, chosen with the arrow keys, <kbd>Tab</kbd>, <kbd>Enter</kbd>, or a button's key. Its icon has a column of its own, and the lines of the message hang under it. An `InputBox` ends with a line to type on, which starts with the default text selected and scrolls inside the box. <kbd>Esc</kbd> closes either at window level.

---
> ⏮️ [**RD-VBAL §2.0.1** Supported Languages](rd-vbal.2.0.1.supported-languages.md) | ⏭️ [**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.md)
