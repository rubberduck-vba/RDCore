# 2.0.2 Client/Server Capabilities

RDCore operates on a **capability-driven host model**. The capabilities of platform extensions are negotiated with the RD-VBA _environment host_ (see [**RD-VBAL §1.1.1** Platform Extensions](rd-vbal.1.1.1.platform-extensions.md)).

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
|Request|[`PlatformInitializeParams`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.PlatformInitializeParams.html)|`ExpectedComponent`|The [`CoreServerComponent`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.CoreServerComponent.html) the caller believes it is connecting to.|
|||`Expected`|A [`CorePlatformClientCapabilities`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.CorePlatformClientCapabilities.html) describing the capabilities the caller expects the peer to provide.|
|Response|[`PlatformInitializeResult`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.PlatformInitializeResult.html)|`Component`|The peer's own `CoreServerComponent`.|
|||`Provided`|The flat list of _capability type names_ (e.g. `"ParseFullDocument"`) the peer provides.|

The responding side builds `Provided` by _reflecting_ the `[assembly: ProvidesCorePlatformClientCapability<T>]` attributes ([`ProvidesCorePlatformClientCapabilityAttribute<T>`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.ProvidesCorePlatformClientCapabilityAttribute-1.html)) declared on its entry assembly. A component's platform capability set is therefore a compile-time property of the build rather than runtime configuration.

The `rdcore/platform/initialize` method is answered by a handler (`PlatformInitializeHandler`) that the SDK registers on every RDCore server, alongside the LSP `shutdown`, `exit`, and `$/setTrace` handlers.

The handshake is _informational_:

- the response is retained, as [`IRDCoreClientApp`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.IRDCoreClientApp.html)`.PlatformInfo`;
- the response is logged;
- `PlatformInitializeResult.Provides<T>()` lets a caller test for a capability.

> [!NOTE]
> The platform does not refuse a connection whose peer reports the wrong component, or a missing required capability, in the `rdcore/platform/initialize` response.

## 2.0.2.1.1 The language

The platform serves several members of the BASIC family ([`SupportedLanguages`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Workspace.SupportedLanguages.html)): RD-VBA (`vba`, the
default), VB6 (`vb6`) and the platform's BASIC (`basic`, which an interactive shell is written in). The language is what decides the dialect's table: where the variable an undeclared name declares lives
([**MS-VBAL §5.6.10**](rd-vbal.5.6.10.simple-name-expressions.md)), and which statements exist at all ([**RD-VBAL §5.4.5.8**](rd-vbal.5.4.5.8.print-statement.md)).
The standard library is not part of the table: it is `VBA` whatever the language ([**RD-VBAL §6.0**](rd-vbal.6.0.standard-library.md)).

A client says which one it is in the `initializationOptions` of its LSP `initialize` request, the part of the protocol that exists for a setting no standard
capability describes ([RDCoreInitializationOptions](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.RDCoreInitializationOptions.html)):

```json
{ "language": "basic" }
```

The server applies it before it builds anything from the request, and what the client sends wins over the server's own `Configuration:Workspace:Language`
setting (`--language` on its command line, which is how a server that a client spawns is told, and how the language server tells the servers it starts).
A language the platform does not serve is logged and ignored.

## 2.0.2.1.2 The language of the user interface

What a server says (the messages of its diagnostics, why a rename is refused, the titles of its code actions, its hovers) is said in the language the
client's user interface is in: the `locale` of the `initialize` request (**LSP 3.17**), such as `fr-CA` or `en`
(`ClientLocale`). Every server of the platform applies it before it builds anything from the request,
and a language server tells the servers it starts the same `locale`, so that the diagnostics an extension finds are in the client's language too. A culture
reads its own language's messages, or its parent's: `fr-FR` and `fr-CA` read the French ones, and every other culture (`en-GB`, the invariant culture) reads
the English ones. Only the language of the user interface is the client's: how numbers and dates are written and read does not depend on it. A `locale`
the platform does not know is logged and ignored; a client that sends none is answered in the language the server's process starts in.

## 2.0.2.1.3 What the server asks before it answers initialize

The language server loads its workspace while it answers `initialize`, and what it finds there can need the user's answer: a project file it cannot read is
offered to be regenerated ([**RD-VBAL §2.2.3**](rd-vbal.2.2.3.projectfile.md)). LSP allows a server to ask with `window/showMessageRequest` before it
answers `initialize`, and the server waits for the answer, so a client that cannot answer then would wait forever for the answer to `initialize`.

The `rdc` shell is such a client: OmniSharp's language client (0.19.9) sends nothing before it is initialized except its `initialize` request, its
`initialized` notification, and responses that carry a result; every other message waits until it is initialized. Error responses are held as well,
including the answer of a handler that fails, of a client whose user cannot be asked, and of a client that has no handler for the request.

So before it answers `initialize`, the server asks only a client whose `initializationOptions` say both of these:

- `answersDuringInitialize` is `true` ([`AnswersDuringInitialize`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.RDCoreInitializationOptions.html)): the client
  answers with a choice or with none, and never with an error;
- the client is not `unattended`.

```json
{ "language": "basic", "answersDuringInitialize": true }
```

A client that does not say so is not asked: the server does without the answer, as it does when a user closes the question, and says why the project did
not open (`window/showMessage`). The `rdc` shell says so when someone is typing at it, and not when its input is a script or it runs `--unattended`.

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

This catalogue lists the platform capabilities the SDK defines. Each capability is a [`CorePlatformClientCapability`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.CorePlatformClientCapability.html) record. Where a capability implies an out-of-band request, it also has a non-LSP method.

|Capability|Method|Provided by|Description|
|---|---|---|---|
|[`ParseFullDocument`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.ParseFullDocument.html)|`rdcore/parser/document`|`ParsingServer`|Lets the language server request a parse result for a source fragment it supplies directly. The parser never reads source from the filesystem. A request may optionally be anchored at a position within a larger document, so that the locations reported for a sub-range fragment are in that document's coordinates.|
|[`ParseTokens`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.ParseTokens.html)|`rdcore/parser/tokens`|`ParsingServer`|Lets the language server request the lexical tokens of a text, which is what a document is highlighted by.|
|[`DefineSymbols`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.DefineSymbols.html)|`rdcore/host/symbols/define`|`EnvironmentHost`|Lets the language server send a module's member symbol descriptors to the environment host for definition in its runtime session (see [below](#rdcorehostsymbolsdefine)).|
|[`ProjectReload`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.ProjectReload.html)|`rdcore/host/project/reload`|`EnvironmentHost`|Lets the language server ask the environment host to compose its runtime session again from the project file as it is now, once a client said the `.rdproj` changed (see [below](#workspacedidchangewatchedfiles)).|
|[`SemanticAnalysis`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.SemanticAnalysis.html)|`rdcore/host/semantics`|`EnvironmentHost`|Runs the semantic analysis pass over the code the environment host holds, and answers with its model, which is what a diagnostics extension analyzes.|
|[`SessionStatus`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.SessionStatus.html)|`rdcore/session/status`, `rdcore/host/session/status`|`LanguageServer`, `EnvironmentHost`|Reports the state of the runtime session, including how much of its memory is reserved, allocated and free. The language server answers its client, and asks the environment host that owns the session.|
|[`SessionExecute`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.SessionExecute.html)|`rdcore/session/execute`, `rdcore/host/execute`|`LanguageServer`, `EnvironmentHost`|Runs a procedure of a module the client supplies, and reports what it printed (see [below](#rdcoresessionexecute)).|
|[`SessionDiscard`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.SessionDiscard.html)|`rdcore/session/discard`, `rdcore/host/discard`|`LanguageServer`, `EnvironmentHost`|Takes a module out of the runtime session: what it declared and what it held go, as a program's variables do when the program is cleared.|
|[`ProgramDebugging`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.ProgramDebugging.html)|`rdcore/session/debug/*`, `rdcore/host/debug/*`|`LanguageServer`, `EnvironmentHost`|Runs a program under a debugger: it waits where a `Stop` or a break puts it (**MS-VBAL §5.4.2.11**), and is resumed, stepped, paused or ended.|
|[`SessionMemoryAccess`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.SessionMemoryAccess.html)|`rdcore/session/memory/peek`, `.../poke`, `rdcore/host/memory/peek`, `.../poke`|`LanguageServer`, `EnvironmentHost`|Reads and writes single bytes of the runtime session's memory (`PEEK` and `POKE`), unchecked.|
|[`SessionAnalyze`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.SessionAnalyze.html)|`rdcore/session/analyze`|`LanguageServer`|Analyzes a module the client supplies, and reports the diagnostics its providers found: the counterpart to the LSP diagnostics pull for a client with no editor.|
|[`CliCommand`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.CliCommand.html)|_(none: in-process CLI dispatch)_|`ClientApp`, `Extension`|Advertises that the declaring component contributes `rdc.exe` command-mode verbs. The CLI (`rdc.exe`) declares it for its native verbs. An extension declares it so that `rdc.exe describe-ext` records the capability in the extension's manifest (see [**RD-VBAL §1.1.5** Extension Manifest](rd-vbal.1.1.5.extension-manifest.md)).|
|[`DiagnoseDocument`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.DiagnoseDocument.html)|`rdcore/diagnostics/document`|`Extension`|Advertised by a _diagnostics provider_: a platform extension whose manifest advertises this capability. A provider declares it with `[assembly: ProvidesCorePlatformClientCapability<DiagnoseDocument>]` (see [below](#rdcorediagnosticsdocument)).|
|[`SessionInteraction`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.SessionInteraction.html)|`rdcore/session/interaction/msgBox`, `.../inputBox`|`LanguageServer`|Relays the dialogs a program opens (`MsgBox`, `InputBox`) to a client that expects this capability, which answers them. The interactive shell expects it, and asks at its console (see [below](#rdcoresessioninteraction)).|
|[`Unattended`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.Unattended.html)|_(none: expected by the client)_|`LanguageServer`|Says that nobody is there to answer a dialog: a client that expects it runs programs unattended (a CI run, `rdc --unattended`). A dialog a program opens (`MsgBox`, `InputBox`, a form shown) then fails the run with `VBR50001`, which no `On Error` of the program catches. It wins over `SessionInteraction`.|
|[`SessionCompletion`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.SessionCompletion.html)|`rdcore/session/completion`|`LanguageServer`|Lists what can be written at a position of a module the client supplies, with the list `textDocument/completion` answers with. For a line typed at a prompt, which is in no document (see [below](#textdocumentcompletion-and-rdcoresessioncompletion)).|
|[`SessionSignatureHelp`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.SessionSignatureHelp.html)|`rdcore/session/signatureHelp`|`LanguageServer`|Says what the call being written at a position of a module the client supplies takes, with the signature help `textDocument/signatureHelp` answers with. For a line typed at a prompt (see [below](#textdocumentsignaturehelp-and-rdcoresessionsignaturehelp)).|
|[`RubberduckIndenterSettings`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.RubberduckIndenterSettings.html)|`rdcore/formatting/indenterSettings`, `rdcore/formatting/indenterSettings/apply`|`LanguageServer`|Asks the client for the indenter settings its user had in Rubberduck, as Rubberduck has them, and asks it to take the `Formatting` settings mapped from them as its own. Offered once on the first start (see [below](#rdcoreformattingindentersettings)).|
|[`SessionSemanticTokens`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.SessionSemanticTokens.html)|`rdcore/session/semanticTokens`|`LanguageServer`|Tells the semantic tokens of code fragments the client supplies, with the legend and the encoding `textDocument/semanticTokens/full` answers with. For code a client shows that is in no document, such as the condition of a breakpoint (see [below](#textdocumentsemantictokensfull-and-rdcoresessionsemantictokens)).|
|[`Refactoring`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.Refactoring.html)|`rdcore/refactor/prepare`, `rdcore/refactor/checkIdentifier`, `rdcore/refactor/apply`|`LanguageServer`|Says what a refactoring asks its user and what an identifier the user is typing raises, and answers the edit it makes with the user's answers: for a client that shows a dialog of its own before a refactoring is applied (see [below](#rdcorerefactorprepare-rdcorerefactorcheckidentifier-and-rdcorerefactorapply)).|

> [!NOTE]
> The handshake for every capability listed is _informational_ (see [**RD-VBAL §2.0.2.1** The `rdcore/platform/initialize` handshake](#2021-the-rdcoreplatforminitialize-handshake)).

Capability providers for extensions distributed through the RDCore Platform Cloud Infrastructure: see [**RD-VBAL §1.1.6** Capabilities Provider](rd-vbal.1.1.6.capabilities-provider.md).

### `rdcore/host/symbols/define`

`rdcore/host/symbols/define` resolves a declared type name as seen from the module being defined. It resolves the name against the standard library's own types as well as the intrinsic types (see [**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.md)).

The project scope is an ancestor of a module's own scope and of nothing else (see [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).

In the host session, `Dim d As VbDayOfWeek` binds to the standard library's `VbDayOfWeek` type, not to [`VBUnknownType`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBUnknownType.html).

### `rdcore/diagnostics/document`

Diagnostics use the LSP 3.17 pull model (`textDocument/diagnostic`). The language server sends the parsed `ModuleParseResult` to every registered provider over the internal `rdcore/diagnostics/document` request. Only this language-server-to-provider hop of the diagnostics pipeline is an RDCore request.

The request carries the parse result as a [`PlatformJson`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.PlatformJson.html) string, because the syntax tree is polymorphic. See [**RD-VBAL §2.6.5** Diagnostics Pipeline](rd-vbal.2.6.5.diagnostics-pipeline.md).

### `PlatformJson` payloads

A payload the platform's components exchange as a `PlatformJson` string (a parse result, a semantic model, symbols) is the model it carries, written so that
what every node repeats is written once. The model each end has is the same: a reader gets every location and identity back with its document.

|What|How it is written|
|---|---|
|A range|Four numbers: `[startLine, startCharacter, endLine, endCharacter]`.|
|A location|`{"Uri": …, "Range": […]}`, where the `Uri` is written only when it is not the one written last in the payload; a location with none has the last one read.|
|A node's identity|`{"DocumentUri": …, "Lineage": […]}`, where the `DocumentUri` is written only when it is not the one written last in the payload, the same way.|
|A node's `SourceLocation`|Not written where it is the node's own `Location`, which it is read back from.|
|A `null`|Not written: a property that is not there reads back as `null`, or as its declared default.|
|A syntax tree|`{"$nodes": [...], "$root": n}`: an array of every node of the tree, and the index of its root in the array.|
|A node a node holds|Its index in the array of its tree, a bare number. A sequence of nodes is an array of indexes.|

A syntax tree is written flat, so a payload's depth does not grow with the depth of the code: a chain of thousands of additions, or hundreds of nested
parentheses, is written and read with the same stack as one addition.

- The outermost node of a tree that a payload holds, a module's node in a parse result, is written as a tree: `$nodes` holds every node of it, and `$root`
  is the index of the node itself.
- Each node in `$nodes` is an object with the properties it always has, its `$type` among them. A property that holds a node is written as that node's
  index in `$nodes`, and a property that holds a sequence of nodes as an array of indexes.
- Every node comes after the nodes it holds, so an index names a node already read: a reader can construct each node in one pass, start to end. An index
  that names no node read before it is an error in the payload.
- A node that two properties hold is written once, and both properties hold its index: it is read back as one node, not two copies of it.

A payload is written and read start to end in one call, so the document a location or an identity is read with is the one written before it. The payload of
one module writes its path once or twice, rather than three times a node: for a module of 2,150 lines, the parse result is about 1.3 MB of JSON. Anything
serialized with the same options outside a payload ([`SyntaxNodeJson.Options`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Abstract.SyntaxNodeJson.html), directly)
writes every document.

### `rdcore/session/execute`

The `rdcore/session/execute` result ([`ExecuteResult`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.ExecuteResult.html)) carries a run-time error's code, title, source, position, line number and a structured stack trace.

The interactive shell renders a run-time error from this result, as:

- an icon;
- the title, with the program's own line number;
- the diagnostic code and description (see [**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md));
- `Err.Source`;
- the stack trace.

The module a client runs is a module of the runtime session. A client can also run a module of its own, which the project does not list, by saying so with
`sessionModule` ([`SessionExecuteParams.SessionModule`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.SessionExecuteParams.html)).
The interactive shell does this for its program. When the session has no module of that name, the module is defined as a standard module of the runtime session at
the workspace root, as the code of an Immediate window is, and the project file is not changed. From then on the session has it: a later run finds it, with or
without `sessionModule`, until the project is reloaded and the session composed again. Without `sessionModule`, a module the session does not have is not found.

A module of the client's own cannot be named after the project or a library it references. The project, the libraries it references and its procedural modules are
matched in one tier (**MS-VBAL §5.6.10**), so the name would match twice, and a name qualified by it, such as `VBA.Len`, would be invalid. Such a run is not found,
and the result says why.

A workspace whose project file cannot be read loads no project, and the client is told why. What the client runs is still run there, as in a project with no modules. A client
that opens no folder has no workspace, and what it runs is not found.

### Documents and their addresses

A document is addressed by the URI the client gives it (**LSP 3.17** §URI), of any scheme. The language server answers for it at that address, and publishes what it finds of it there.

|A document|Is|
|---|---|
|A file of the workspace's folder (`file:`)|A module of the workspace's project, as its project file lists it.|
|A file outside the workspace's folder|A module of the workspace's project too, while the client has it open: a program a shell loads is one.|
|A document that is no file (`untitled:Untitled-1`, or any other scheme)|The client's alone: a project of its own, addressed under its own URI ([`SymbolUri.RootOf`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.SymbolUri.html)). It is parsed and gets its syntax diagnostics, its outline, its folding ranges and its completions, which offer nothing of the workspace's modules; it is no module of the workspace, and the environment host has no module of it. It is gone once the client closes it.|
|Any document of a server started on no folder (`rootUri` of `null`)|A project of its own, as a document that is no file is.|

A URI is the document's however the client spells it: `file:///c%3A/Work/Module1.bas` is the document `file:///C:/Work/Module1.bas` is, and a root of a project given as a URI is the root its path is ([`SymbolUri.Root`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.SymbolUri.html)). A relative path is no URI, and addresses no root.

### Requests while the client edits

An RDCore server answers every request it receives, and runs a notification that changes a document (`textDocument/didOpen`, `didChange`, `didClose`) in order, after the requests already running ([`ServerRequestScheduling.WithEveryRequestAnswered`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Server.ServerRequestScheduling.html)).
An RDCore client answers every request a server makes in the same way: a `window/showMessageRequest` that waits for its user is still answered when a
`$/progress` or a `window/workDoneProgress/create` arrives in the meantime ([`RDCoreClientApp`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.RDCoreClientApp.html)).

A request that is made of what a document was would make a keystroke wait for it, and the completion a client sends after the keystroke too. So `textDocument/diagnostic` and `textDocument/semanticTokens/full` give way to a change ([`IncomingDocumentChanges`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Server.IncomingDocumentChanges.html)):

- **The change is known as it arrives.** The server reads which document a change is to as the message arrives, long before its turn comes.
- **A request about that document stops.** It is answered `ContentModified` (-32801), which **LSP 3.17** §Response Message has a server answer "if it detects a content change in its unprocessed messages". The client asks again for the document as it is now.
  - This holds for a request that is running when the change arrives, and for one that starts before the change is applied.
  - A change to another document changes nothing for it.
- **A request the client cancels ends at once,** answered `RequestCancelled` (-32800), whatever the work under it is still doing.

In HexIDE, a completion sent right after a keystroke, while the diagnostics and the semantic tokens of a 2,100-line module are being found, waits for neither.

### `workspace/diagnostic`

The language server answers `workspace/diagnostic` (**LSP 3.17** §Workspace Diagnostics) with a report for every module of the workspace, whether the client has it open or not: the modules a client never opened are analyzed as they are on disk (`diagnosticProvider.workspaceDiagnostics = true`). The files a project lists as other files are none of them ([**RD-VBAL §2.2.3.4**](rd-vbal.2.2.3.projectfile.md#2234-rdcorefile)).

|Each module's report|Is|
|---|---|
|`kind`|`full`, with every finding there is for the module (what `textDocument/diagnostic` answers for it), or `unchanged`, when the client sends the module's result id (`previousResultIds`) and the module has not changed since.|
|`resultId`|The module's, as `textDocument/diagnostic` gives it ([**RD-VBAL §2.6.5**](rd-vbal.2.6.5.diagnostics-pipeline.md#result-identity-and-staleness)): a client can mix the two pulls.|
|`uri`|The module's document.|
|`version`|The version the client gave the module, for a module it has open; `null` for one it has not.|

A pull analyzes every module, so:

- **A client that gives a `partialResultToken` is sent each module's report as it is made,** as a partial result (`$/progress`), and an empty answer. A client that does not is answered every report at once.
- **The work is reported to the `workDoneToken`,** module by module.
- **A change to any document gives the pull up,** answered `ContentModified` (-32801) as the change arrives: the keystroke, and the client's next request, don't wait for the whole workspace ([`IncomingDocumentChanges.WhileWorkspaceCurrentAsync`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Server.IncomingDocumentChanges.html)). The client pulls again, sending the result ids it has, and is answered `unchanged` for every module that did not change.
- **A dialog of a program that is open declines the pull,** as it does `textDocument/diagnostic`'s ([below](#rdcoresessioninteraction)).

### Refreshing what the client shows

What a client shows of a module (its diagnostics, its semantic tokens, its inlay hints, its code lenses) is of the module, and of what the other modules declare and how they use it. A procedure that another module calls, removed, makes the call an error there; a call added to it is one more reference in its code lens. A client pulls these for a document it changes; for the others, the server asks it to pull them again:

|Message|Direction|Does|
|---|---|---|
|`workspace/diagnostic/refresh`|Server to client|Asks a client to pull the diagnostics of every document again. Sent to a client whose `workspace.diagnostics.refreshSupport` is `true`.|
|`workspace/semanticTokens/refresh`|Server to client|The same, for semantic tokens (`workspace.semanticTokens.refreshSupport`).|
|`workspace/inlayHint/refresh`|Server to client|The same, for inlay hints (`workspace.inlayHint.refreshSupport`).|
|`workspace/codeLens/refresh`|Server to client|The same, for code lenses (`workspace.codeLens.refreshSupport`).|

Each is a request, which the client answers; a client that sent none of a capability is not asked. The server asks after:

- **A change of a module that the others can tell,** once the environment host has it: the module's options and attributes, its module-level declarations, the signature of a procedure, a value written in any of them, and the names a procedure's body uses and what it declares (`ModuleSurface`). The workspace is defined again in its three phases first, so that what the host has of the other modules (what their declarations are bound to, what their code calls) is of what the module declares now. A change that no other module can tell asks nothing: a literal, an operator or a comment in a procedure's body, a statement that uses the same names, whitespace.
- **A module that is gone,** closed with no file under it.
- **A reload of the project** that changed the workspace ([`workspace/didChangeWatchedFiles`](#workspacedidchangewatchedfiles), [file operations](#workspacedidcreatefiles-didrenamefiles-and-diddeletefiles), `rdcore.importProject`).
- **A change of the live settings** ([`workspace/configuration`](#workspaceconfiguration-and-workspacedidchangeconfiguration)).
- **A pull declined while a dialog of a program was open,** once the last dialog is closed: `workspace/diagnostic/refresh` alone, and the result ids are what they were ([`rdcore/session/interaction`](#rdcoresessioninteraction)).

The client is asked once for a burst of changes (each keystroke of a name is one), when the burst is over (300 ms). A pull that comes before the request is not answered `unchanged` for a document whose findings may have changed: a diagnostic report's result id is of the workspace too ([**RD-VBAL §2.6.5**](rd-vbal.2.6.5.diagnostics-pipeline.md#result-identity-and-staleness)).

OmniSharp 0.19.9's `SendDiagnosticRefresh`, `SendSemanticTokensRefresh`, `SendInlayHintRefresh` and `SendCodeLensRefresh` write each refresh as a notification, with no `id`, which a client that keeps to the protocol does not take for the request: the server sends each as the request it is.

### Requests that fail

A request a server refuses is answered `RequestFailed` (-32803), with a message that says why on one line, in the language of the user interface, for the client to show its user: a rename to a name that is taken, a command whose file cannot be written. A request whose handler fails unexpectedly is answered `InternalError` (-32603), with the exception's message on one line. It never carries the stack, nor the paths of the files the platform was built from. The whole of it is written to the server's own log (`ServerErrorAnswers.WithOneLineErrors`).

### `textDocument/inlayHint`

The language server answers `textDocument/inlayHint` (**LSP 3.17** §Inlay Hint Request) with, before each positional argument of the calls in the range,
the name of the parameter it is given to: `MsgBox Prompt: "Hello", Buttons: vbOKOnly`
(`ParameterHints`). A call is an argument list after a name the static pass binds to a
procedure, a function or a property, of the workspace or of a library it references. The subscripts of an array have no hint. A named argument, and every
argument after it, has none; nor has an argument that is a name written as its parameter's, or an argument of a `ParamArray` after its first. Each hint is
of kind `Parameter`, complete as it is answered (no `inlayHint/resolve`). For a `.rdc` program, positions are in its lines.

After the name of each variable or parameter that is declared without a type, and is a `Variant` (**MS-VBAL §5.2.3.1.5**), the answer has a hint `As Variant` of kind `Type`, padded on its left: `Dim x` is shown `Dim x As Variant` (`ImplicitTypeHints`). A declaration with an `As` clause or a type-declaration character has none; nor has a name that a `Def<Type>` directive of the module covers, a constant, or a parameter that is an array. The hint is after the array bounds of a variable (`Dim a(10)` is shown `Dim a(10) As Variant`).

The interactive shell asks for the hints of its program once for the whole listing, and shows them in `LIST`, faint, when `INLAYS ON` has been said (`INLAYS OFF` stops; they are off until then).

### `textDocument/completion` and `rdcore/session/completion`

The language server answers `textDocument/completion` (**LSP 3.17** §Completion Request) for the documents it holds, a `.rdc` program included. Its trigger characters are `.` and `!`, and `(`, `,` and a space, which ask for the list of an argument as a value of it starts: that list is answered only for an argument whose parameter is an `Enum`, and only where a value starts, which is after the `(`, a comma, a `:=` or the name of a call statement, or after an operator that combines flags (`+`, `Or`, `And`, `Xor`, `Not`: `MsgBox "a", vbYesNo + `). Nothing is answered anywhere else, a space after an operand included, so that a client is not given a whole list at every comma, and the workspace is not composed for nothing. The list is complete (`isIncomplete = false`): a client narrows it by what is typed after it. Items are not resolved (`resolveProvider = false`).

What is typed of the name when the list is asked for narrows it already: an item is in the list when its label has the letters typed, in their order, in any case ([`CompletionCandidates.Matching`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.Completion.CompletionCandidates.html)): `gtc` keeps `GetCount`. Whatever a client narrows by as more is typed (the start of a label, a part of it, letters apart), an item it could show is in the list, so the list stays complete. A list asked for where nothing is typed yet (`Ctrl+Space` after a space) has every name. An item has no `insertText` and no `filterText`: both are its label, which is what a client takes without them.

In the interactive shell's prompt, an item that is a snippet of one line (`insertTextFormat` `Snippet`) is put in place with its tab stops: the first is selected, and what is typed replaces it in each of its places at once. <kbd>Tab</kbd> goes to the next tab stop and <kbd>Shift</kbd>+<kbd>Tab</kbd> back to the one before, unless a completion list is open, as one opens on a word typed in a tab stop: then <kbd>Tab</kbd> puts the selected name in place, and the next <kbd>Tab</kbd> goes on, as an editor has it. The snippet is done at its last tab stop, at <kbd>Esc</kbd>, at <kbd>Enter</kbd>, or once the cursor leaves the tab stop. A snippet of more lines than one, or one larger than a snippet to insert once written, is put in place as its name.

What the list offers depends on what is being written, and where. The language server reads two things:

- **The line before the cursor** ([`CompletionSite`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.Completion.CompletionSite.html)): the name being typed, and the statement it is in, as tokens ([`CompletionStatement`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.Completion.CompletionStatement.html)). Is a statement, an operand or an operator next?
- **Where the line is in the module** ([`CompletionPlacement`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.Completion.CompletionPlacement.html)): in the declarations section or in a procedure, which kind of procedure, which blocks are open around it, and the procedure's labels. This is read off the text, not the parse: a `For` whose `Next` is not written yet is still open.

|Written|Offered|
|---|---|
|A declaration, in the declarations section|What can be declared there. `Option` anywhere in it, with only the options the module does not state yet: each is stated once at most. `Event`, `Implements`, `Friend` and `WithEvents` in a class module only. No statement and no name.|
|A statement, in a procedure|The names visible from the procedure, nearest first (its locals and parameters, the module's members, the project's, the libraries', the standard library's). The statement keywords. The statements of the blocks the line is in (`Next` and `Exit For` in a `For`, `Loop` and `Exit Do` in a `Do`, `Case` and `End Select` in a `Select Case`, `Else` and `End If` in a block `If`). `Exit` and `End` for the kind of procedure. `Me` and `RaiseEvent` in a class module only.|
|An operand|The names, and the keywords an expression starts with (`Not`, `New`, `Nothing`, `True`). `AddressOf` only in an argument.|
|An argument whose parameter is an `Enum` (`MsgBox "Hello", `, `Paint Colour:=`)|The `Enum`'s constants first, by name, the first of them preselected (`VbMsgBoxStyle`'s for `MsgBox`'s `Buttons`), then what an operand is offered: the parameter is found as the signature help finds it, whether the call has parentheses, is qualified, or names the argument; an `Optional` one too. The interactive shell opens the list on the space after a `,` in a call's arguments, and asks for it as that trigger character does.|
|After a complete operand|The operators (`And`, `Mod`, `Like`, `Is`), and the keyword the statement goes on with (`Then`, `To`, `Step`, `In`). No name.|
|A name being declared (`Dim `, `Sub `, `Const `)|Nothing: it is a new name. After it, `As`. In a parameter list, `ByVal`, `ByRef`, `Optional` and `ParamArray`.|
|A statement's own words|`Exit ` the blocks and the procedure it can exit; `End ` what it can end; `GoTo `, `GoSub `, `Resume ` and `On Error GoTo ` the procedure's labels and line numbers; `RaiseEvent ` the class's events; `Implements ` and `TypeOf x Is ` the classes; `Option `, `Property `, `Declare `, `Open … For ` their keywords.|
|A member, after `.` or `!`|The members of the type of the expression before it (`w.Grow(2).Parts.`), or the members of the project or module it names (`VBA.`, `Module1.`). After a `.` with nothing before it, the members of the object of the enclosing `With` block.|
|A type, after `As`|The intrinsic types; the classes, user-defined types and Enums the project and the libraries declare; the projects and the libraries, which qualify a type name; and `New`. No procedural module, local or other keyword.|
|A class, after `New`|The classes that can be created, and the projects and the libraries.|
|In a comment or a string literal|Nothing.|

Which keyword is legal where is decided by [`CompletionRules`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.Completion.CompletionRules.html). A `Private` member is offered only in its own module. A member a library hides (`VB_MemberFlags`) is never offered.

An `Object` or a `Variant` has no members known when the code is written. For a variable of one, the list offers the members of every class the module `Set`s it to an object of with `CreateObject` and a constant identifier (`Set o = CreateObject("Scripting.Dictionary")`), wherever the module does it (`ObjectClassInference`). A class of a library the project does not reference is read from the library's description, and its names are still not names of the project. This is a fact for tooling only: the variable keeps its declared type, nothing about the class is a compile error, and the member stays late-bound when the code runs.

The line being typed seldom parses. The language server takes it out of the module before the module is parsed. For a member access, it puts it back as an assignment of the expression the member is accessed on, whose type comes from the static semantics. The module is composed with the rest of the workspace and the referenced libraries. Nothing of it is sent to the environment host.

`rdcore/session/completion` ([`SessionCompletionParams`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.SessionCompletionParams.html)) answers the same list for a module the client supplies with its source. The module stands for the workspace module of the same name. The interactive shell asks it for the line at its prompt, which is not in the program until it is entered.

A line at a client's prompt can also be one of the client's own commands. A client registers the verbs of its commands with `rdcore/session/commands` ([`SessionCommandsParams`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.SessionCommandsParams.html)), each described by a [`VerbDescriptor`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Commands.VerbDescriptor.html): the forms its arguments take, which are keywords, choices, line numbers, expressions, paths, text, settings and their values.

A verb is registered once. It is registered again only when its descriptor changes (a choice of what the client has more or fewer of), and then it replaces what it was. A completion request names the verb and what is typed after it (`Command`), and nothing more of the verb travels. The language server completes each argument as described (`VerbCompletion`):

- a keyword or a choice with its values;
- a line number with the numbered lines of the module, each shown with its statement;
- an expression where it is evaluated: before the line its form names (`EvaluatedAt`), in that line's procedure and blocks, which is where a breakpoint's program waits. An expression that names no line, or a line the module does not have, is completed at module level: the module's names and the project's, and no procedure's locals;
- a setting a part at a time, as a member is: the sections of the settings the argument carries (`Settings`), then what is in the section written before the last `.`: a section of it, or a setting with its description. The client asks again after each `.`;
- the value of a setting with the values of the setting its form names (`ValueOf`), as it is written before it: `True` and `False`, or the names of a choice. Other values are offered nothing;
- a path or text with nothing.

A verb the client did not register is offered nothing. The interactive shell registers `BREAK` (`CLEAR`, `TOGGLE <line>`, `<line> [condition]`, its condition evaluated at its line), `FOLD` (`<line>`, completed with the lines a block of more than one line starts at: a line argument can say so, `VerbArgument.LineFilter`), `THEME` (`LIST`, `<theme>`) and `CONFIG` (`RESET [setting]`, `<setting> [value]`; [**RD-VBAL §2.3.3** Configuration](rd-vbal.2.3.3.configuration.md)) the first time one is completed. A command it does not describe is offered nothing after its verb. The list of what can be written after a described verb opens as the space after the verb is typed (and after each space in its arguments), before the first letter of an argument is, and narrows as the argument is typed; no <kbd>Ctrl</kbd>+<kbd>Space</kbd> is needed.

### `textDocument/signatureHelp` and `rdcore/session/signatureHelp`

The language server answers `textDocument/signatureHelp` (**LSP 3.17** §Signature Help) for the documents it holds. Its trigger characters are `(`, `,` and a space, which starts the arguments of a call statement; `)` triggers it again, for the call around the one it closes.

The call is read off the line before the cursor ([`SignatureSite`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.Completion.SignatureSite.html)):

|Written|The call|
|---|---|
|An argument list that is open after a name: `Left(s, `, `o.Item(`, `.Add(`|The innermost one. An argument list after no name (the parentheses of an expression) is no call, and the one around it is the call.|
|A name, a space, and arguments, at the start of a statement: `MsgBox "a", `|The call statement's own (**MS-VBAL §5.4.2.1**). An assignment is not one.|
|In a string literal or a comment|None.|

The argument at the cursor is counted by the commas before it in its argument list. A named argument (`Title:=`) is the parameter it names; an argument after the last parameter is the `ParamArray`'s, when the last parameter is one.

What is called is found as a completion list finds it: a name in the scope of the line, or a member of what its qualifier is, an object created by name included. The answer has one signature ([`SignatureDescription`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.Completion.SignatureDescription.html)): the procedure's signature on one line, each parameter by its range in the line, the active parameter, and what the procedure and each parameter do. An instance member's implicit `Me` is none of its parameters. The answer is `null` where no call is written, or what is called is no procedure.

What a procedure does is its doc string ([`SymbolProperties.DocString`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.Abstract.SymbolProperties.html)), a sentence:

|Procedure|Doc string|
|---|---|
|Of a module of the workspace|Its `Attribute <member>.VB_Description`.|
|Of a referenced library, or of an unreferenced one an object is created of by name|The `description` of the member in the library's description file.|
|Of the standard library|An entry of the platform's resources, in the language of the user interface, for the member and for each parameter. A `$` function has the doc strings of the one it is the `String` version of.|

> [!NOTE]
> The standard library's doc strings cover the `Strings`, `Interaction`, `Conversion`, `Math`, `Information` and `DateTime` modules, and the parameters of the members of `Strings`, `Interaction` and `DateTime` that take several, and of `Round` and `RGB`. The other modules' members have a signature and no doc string; a parameter with none shows none.

`rdcore/session/signatureHelp` ([`SessionSignatureHelpParams`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.SessionSignatureHelpParams.html)) answers the same for a module the client supplies with its source. The interactive shell asks it for the line at its prompt, and draws the signature under the line with the active parameter underlined, and what the procedure and that parameter do under it. A line that is one of the shell's commands is no call.

### `textDocument/semanticTokens/full` and `rdcore/session/semanticTokens`

The language server answers `textDocument/semanticTokens/full` (**LSP 3.17** §Semantic Tokens) for the documents it holds, with the legend of [`SemanticTokenLegend`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Source.SemanticTokenLegend.html): the lexical tokens of the parsing server, with a name told apart by what the module declares (a constant is `readonly`, the name of an enumeration, a user-defined type or a class module of the workspace is a `class`), unless it follows a `.` or a `!`, which makes it a member of something.

`rdcore/session/semanticTokens` ([`SessionSemanticTokensParams`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.SessionSemanticTokensParams.html)) answers the same for code fragments the client supplies, which are in no document: the condition of a breakpoint, say. The request has the complete source of a module and its name, which the names of the fragments are resolved against, and the fragments. The answer has the tokens of each fragment, in the order they were asked, encoded as `textDocument/semanticTokens/full` encodes them, with the first line of a fragment as line 0. Many fragments are asked in one request, so that coloring a list of them costs one round trip, not one each.

The interactive shell asks it once for the conditions of all the breakpoints `BREAK` lists, and draws each condition as `LIST` draws the same code. A server without the capability, or one that does not answer, leaves the conditions plain.

### `textDocument/rename` and `textDocument/prepareRename`

The language server answers `textDocument/prepareRename` and `textDocument/rename` (**LSP 3.17** §Rename Request, §Prepare Rename Request) for the documents it holds, a `.rdc` program included (`renameProvider.prepareProvider = true`). A rename renames a symbol: the names that refer to it, in every document of the workspace, and no other name: a text that is the same in a comment, a string, or another procedure's local is not renamed, except a string a library calls the symbol by (`SymbolRename`).

`textDocument/prepareRename` answers the range of the name at the position and the name the symbol is declared with (`placeholder`). `textDocument/rename` answers a `WorkspaceEdit` of `documentChanges`, one `TextDocumentEdit` per document whose text changes, at the version the server holds, to a client that says it takes them (`workspace.workspaceEdit.documentChanges`); any other client is answered the same edits as `changes`, by document, which name no version (**LSP 3.17** §WorkspaceEdit). An edit whose change annotation asks the user to confirm it is sent only to a client that takes change annotations (`changeAnnotationSupport`); an edit the client could not apply at all is refused, with why. An edit replaces a name only: a type-declaration character after it stays (`s$`).

What a name refers to is what the static semantics resolve it to (**MS-VBAL §5.6.10**, §5.6.12):

|Written|Refers to|
|---|---|
|A name in a procedure, a constant's expression, an array's bounds|What it resolves to in the scope it is written in.|
|A member, after a `.`, a `With` block's included|The member of the declared type of the object.|
|A named argument (`count:=`)|The parameter of the procedure that is called.|
|A member's attribute (`Attribute Value.VB_UserMemId`)|The member.|
|The key of a dictionary access (`c!key`)|Nothing: it is a string.|

The accessors of a property (`Get`, `Let`, `Set`) are one name, and are renamed together.

A string is renamed where a member of a library calls a member of a module by its name in it: the procedure name of `CallByName` (**MS-VBAL §6.1.2.8.1.1**), a macro `Application.Run` runs, a procedure `Application.OnTime` or `OnKey` calls later, or the `OnAction` of a control. The string can be qualified (`"Module1.Foo"`, `"'Book1.xlsm'!Foo"`); one that names another module of the workspace is not renamed. What a string says is only known when the code runs, so the user confirms these edits. A string left as it is names nothing the call can find, and the call fails only when the code runs.

A member accessed late-bound (on an `Object` or a `Variant`) with the symbol's name is renamed too, when the symbol is a member of a class that code outside the class can call (not `Private`). Which member the access calls is only known when the code runs (**MS-VBAL §5.6.12**), so the user confirms these edits as well. What is known of the classes the object may hold decides which accesses are proposed (`PossibleClasses`):

- An object may hold any class, unless something knows them all. An access on it is proposed.
- The code knows them all for a local or a `Private` variable of the module, when every `Set` of it names its class (`New Class1`, a variable or a function `As Class1`, `Me`, `CreateObject` of a class the project resolves, `Nothing`), and the variable is never the variable of a `For Each` loop or a whole argument of a call. An access on it is proposed only when one of those classes is the symbol's.
- **Extension point:** what else knows the classes (an extension) can say so too (`IPossibleClassesSource`).

> [!NOTE]
> **Not implemented.** The classes an environment-host session saw the object hold while it ran are not a source.

An access on an object whose known classes include the symbol's class is backed by what is known, and labeled so (`Late-bound: the object holds a Class1`). Any other access that is proposed is only possible (`Late-bound: the object may hold a Class1`). Each kind is confirmed on its own.

A client that takes change annotations is sent the edits of strings and of late-bound accesses annotated, with `needsConfirmation`, and asks its user itself. Any other client is asked first, with `window/showMessageRequest`: once for the strings, once for the backed late-bound accesses, and once for the possible ones. For each question it is sent the edits with them or without them, and nothing at all when the user chooses neither. A client that expects [`Unattended`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.Unattended.html) is asked nothing: it is sent the rest of the rename, without the edits of strings and of late-bound accesses.

A rename that is refused is answered with an error (`RequestFailed`) whose message says why, in the language of the user interface, and where:

|Refused|Because|
|---|---|
|The new name is empty, not an identifier, or a keyword (**MS-VBAL §3.3.5**, §3.3.5.2)|Code cannot declare it ([`VBIdentifier`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.VBIdentifier.html)). A letter of another script is a letter: `Données` is a name.|
|The new name is declared where the symbol is|Two declarations would have one name.|
|The new name means something nearer, or as near, where the code refers to the symbol|The code would refer to that instead.|
|The symbol would be nearer, or as near, than what the new name means where the code writes it already|The code would refer to the symbol instead.|
|What a name written as the symbol's refers to is not known|The rename could miss it.|
|The symbol is a module, a class, a `Type` or an `Enum`|A type is not renamed.|
|The symbol is an event, a `WithEvents` variable, a form's control, or a member of a class another implements|What is named after it would not be renamed with it.|
|The symbol is a procedure of a class module with an underscore in its name|It is named after the event it handles or the member it implements.|
|The symbol is an external procedure (`Declare`), or the workspace does not declare it|It is named by its library.|

A position is said the way the document says it: its file and line, and for a program of the BASIC, the number of its line.

### `textDocument/hover`

The language server answers `textDocument/hover` (**LSP 3.17** §Hover Request) for the documents it holds, over the name at the position, bound the way a
rename binds it (`SymbolHover`). A symbol of a library the workspace references is hovered as one
of the workspace is.

|Hovered|Declaration|Member of|
|---|---|---|
|A procedure, a function, a property|Its signature: `Public Function GetTotal() As Long`, `Public Property Get Value() As String`|Its module: `Helpers`, `VBA.Interaction`|
|A variable or a constant of a module|`Public Total As Long`, `Private Const Max As Long`|Its module|
|A local, a parameter|`Dim count As Long`, `Static p As Point`, `ByVal amount As Long`|Its procedure: `Program.Main`|
|An `Enum`, a `Type`|`Public Enum Colour`, `Public Type Point`|Its module|
|A member of an `Enum`, a field of a `Type`|`Red`, `X As Long`|Its `Enum` or `Type`: `Helpers.Colour`|
|A class or a module|`Class Class1`, `Module Helpers`|Nothing|

The hover is Markdown (the declaration in a block of VBA, then what declares it and the symbol's doc string) for a client that reads Markdown
(`hover.contentFormat`), and plain text for one that does not. "Member of" is said in the language of the user interface. No name, or a name whose meaning is
not known (a member accessed late-bound), has no hover.

### `textDocument/definition`

The language server answers `textDocument/definition` (**LSP 3.17** §Goto Definition Request) for the documents it holds, a `.rdc` program included. The name at
the position is bound the way a rename binds it (table above), so a navigation leads to what a rename of the name would rename
(`SymbolNavigation`).

|Named|Defined by|
|---|---|
|A variable, a constant, a parameter, a procedure, an event, a `Type` or `Enum` and their members|Its declaration.|
|A property|Each of its procedures: its `Property Get`, `Let` and `Set`.|
|A class or a module, after `As` or `New`|Its document, at its start.|
|A name declared in each branch of an `#If`|Each declaration in a branch that is compiled, or each declaration when none is known to be (**MS-VBAL §3.4**).|
|A member of a library, or a member accessed late-bound (on an `Object` or a `Variant`)|Nothing: the answer is empty.|

A client that reads links (`definition.linkSupport`) is answered `LocationLink`s: the name the navigation starts from (`originSelectionRange`), the whole
declaration (`targetRange`), and the name it declares (`targetSelectionRange`). Any other client is answered a `Location` per definition, at the name it
declares. Ranges are in the text of the document: for a program of the BASIC, its lines.

### `textDocument/references` and `textDocument/documentHighlight`

The language server answers `textDocument/references` (**LSP 3.17** §Find References Request) with each name, in every document it holds, that refers to the
symbol named at the position (`SymbolNavigation.ReferencesAt`): the names a rename of the
symbol would rename (table above), and, for a class, a module, a `Type` or an `Enum`, the names of the type in `As` clauses and after `New`. The names its
declarations declare are references when the client asks for them (`context.includeDeclaration`), and so are the attributes of a member
(`Attribute Foo.VB_Description`). A member accessed late-bound (on an `Object` or a `Variant`) is not known to refer to it, and is not a reference.

Each procedure of a property is referred to by the names that call it (**MS-VBAL §5.3.1.7**), although a rename renames all of them:

|The name|Refers to|
|---|---|
|Reads the property, or calls it with arguments (`Debug.Print c.Value`)|Its `Property Get`.|
|Is the target of a Let-assignment (`c.Value = 1`, `Let c.Value = 1`)|Its `Property Let`.|
|Is the target of a Set-assignment (`Set c.Item = Nothing`)|Its `Property Set`.|

The procedure is the one whose name is at the position, or the one the name there calls. The name of a function or a `Property Get` that its own body
assigns, or reads as a simple name (`Value = mValue` in `Property Get Value`, `Counter = Counter + 1` in `Function Counter`), is its function result variable
(**MS-VBAL §5.3.1**, §5.6.2.1): it calls no procedure, and refers to none. Its name with an argument list (`Fact(n - 1)` in `Function Fact`), in a call
statement, or after a `.` calls it again, and is a reference.

`textDocument/documentHighlight` (**LSP 3.17** §Document Highlights Request) answers every name in the document that a rename of the symbol would rename
there, its declaration and every procedure of a property included, each of kind `Text`: a name that reads the symbol and one that assigns it are not told apart.

### `textDocument/declaration`

The language server answers `textDocument/declaration` (**LSP 3.17** §Goto Declaration Request) the way it answers `textDocument/definition`, with one place:
the one that declares the symbol first, as it is written. A name declared in each branch of an `#If` is declared by the first branch, whichever branch is
compiled, so the answer does not change with the precompiler's constants; a property is declared by its first procedure. A symbol declared once is
declared where it is defined.

### `textDocument/documentSymbol` and `workspace/symbol`

The language server answers `textDocument/documentSymbol` (**LSP 3.17** §Document Symbols Request) with the outline of a document: its module, what the
module declares, and the members of its `Enum`s and `Type`s under them. Each symbol's range is its whole declaration, and its selection range the name it
declares. A procedure's locals are not in the outline. For a `.rdc` program, ranges are in its lines.

It answers `workspace/symbol` (**LSP 3.17** §Workspace Symbols Request) with the symbols of the outline of every document it holds whose name matches the
query (`SymbolSearch`), regardless of case. Each is named with what it is declared in
(`containerName`: the module, or the module and its `Enum` or `Type`) and located at the name it declares.

|The name|Comes|
|---|---|
|Is the query|First.|
|Begins with the query|Second.|
|Has each character of the query at the start of one of its words, in order (`gt` for `GetTotal`, `mi` for `max_items`)|Third.|
|Has the query in it|Fourth.|
|Has each character of the query, in order|Last.|

Names that match alike come by name. An empty query matches every symbol. At most 500 symbols are answered: a client asks again as its user types.

### `textDocument/codeAction`

The language server answers `textDocument/codeAction` (**LSP 3.17** §Code Action Request) for the documents it holds, with the kinds `quickfix`, `refactor.rename`, `refactor.extract.method`, `refactor.extract.gosub` and `source.rdcore.importProject`. A client that asks for some kinds only (`context.only`) is answered those and the kinds under them. An extraction and a conversion of a `GoSub` routine are the actions that can be resolved (`resolveProvider = true`, `codeAction/resolve`); every other action is complete as it is answered. A client that shows an action it cannot take (`codeAction.disabledSupport`) is also answered a refused extraction or conversion, with the refusal as its `disabled.reason`; one whose range holds nothing to refactor (no statement to extract, no `GoSub` routine at the position) is not answered.

An analyzer states the fix of a finding with it ([`DiagnosticFixes`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Extensibility.Diagnostics.DiagnosticFixes.html)), and the fix travels in the diagnostic's `data`, which a client sends back with the request. The platform makes the fix: an extension says what it is without knowing how the workspace is edited.

|Offered|Kind|Does|
|---|---|---|
|A rename that fixes a diagnostic, to each name its analyzer suggests|`quickfix`, preferred|Its `edit` is the rename's `WorkspaceEdit`. Offered only when the rename can be made.|
|A rename that fixes a diagnostic, to a name the user chooses|`quickfix`|Its `command` is `rdcore.rename`.|
|A rename of each name the range declares, or of the name at the position of an empty range|`refactor.rename`|Its `command` is `rdcore.rename`.|
|An import of each VB6 project (`.vbp`) of the workspace's folder, when it has no `.rdproj`|`source.rdcore.importProject`|Its `command` is `rdcore.importProject`, with the URI of the `.vbp`. Offered in every document.|
|An extraction of the statements of a selection as a procedure, when nothing refuses it|`refactor.extract.method`|For a client that expects [`Refactoring`](#rdcorerefactorprepare-and-rdcorerefactorapply), its `command` is `rdcore.refactor`. For any other, offered only when it changes nothing the user would have to accept: its `edit` is made with the defaults and verified. A client that resolves an action's `edit` (`dataSupport`, and `edit` among `resolveSupport.properties`) is offered the action without its `edit`, which is made when the client resolves the action; one resolved after its document has changed is refused (`RequestFailed`), with a message that asks the user to select the statements again.|
|A conversion of a `GoSub` routine to a procedure, when nothing refuses it (an `On…GoSub` of the routine in a single-line `If` does): at a position on its label, on a `GoSub` that names it or on one of its `Return`s, and for a selection whose extraction is refused for a `GoSub` to the routine or one of its `Return`s|`refactor.extract.gosub`|As an extraction: `rdcore.refactor` for a client that expects `Refactoring`, the `edit` with the defaults for any other, resolved the same way. One resolved after its document has changed is refused (`RequestFailed`), with a message that asks the user to ask for the conversion again.|

`rdcore.rename` ([`RefactoringCommands.Rename`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Semantics.Refactoring.RefactoringCommands.html)) is a command a client runs itself: its arguments are the URI of the document and the position of the name. The client asks its user for the new name, and sends `textDocument/rename` with it, unless the user cancels. The titles are in the language of the user interface.

|Diagnostic|Fixed by|
|---|---|
|[RDC01001](../diagnostics/rdc01001.md): a name that does not say what it is|A rename to a name the user chooses.|
|[RDC01002](../diagnostics/rdc01002.md): Hungarian notation|A rename to the word after the prefix (`strName` to `Name`), or to a name the user chooses.|

The interactive shell asks for them with `DUCKY`, for its program as the document it is: `DUCKY` for the whole program, `DUCKY 20` for line 20. It pulls the diagnostics of the program (`textDocument/diagnostic`), asks for the code actions of the range with those of them that are in it, lists the actions by number, and does the one whose number is typed; <kbd>Esc</kbd> does none. It runs `rdcore.rename` itself: it asks the platform what the name is (`textDocument/prepareRename`), asks for the new name with that one, and renames (`textDocument/rename`) unless <kbd>Esc</kbd> cancels. An edit is made to the program as a whole or not at all: not when it is of a version the program no longer is, and not when it edits a document the shell does not hold. A program that is no document (in a shell attached to a workspace, a program that was neither loaded nor saved) is to be saved first, which `DUCKY` says.

### `rdcore/refactor/prepare`, `rdcore/refactor/checkIdentifier` and `rdcore/refactor/apply`

A refactoring is offered to any client as a code action, which completes with its defaults. A client that shows a dialog of its own before a refactoring is applied expects [`Refactoring`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.Refactoring.html), and asks with the requests below, the same for every refactoring. The language server provides the capability, and serves Extract Method (`refactor.extract.method`) and Convert GoSub to Procedure (`refactor.extract.gosub`) with them.

**`rdcore/refactor/prepare`** ([`RefactorPrepareParams`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.RefactorPrepareParams.html)) names the refactoring by the kind of its code action, with the document and the range. It is answered what the refactoring asks its user, each choice with the answer it takes by default, and its notices ([`RefactorPrepareResult`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.RefactorPrepareResult.html)):

|A choice of kind|Is answered with|
|---|---|
|`Name`|A name. One the code cannot declare there is refused when the refactoring is applied, which `rdcore/refactor/checkIdentifier` says before.|
|`Option`|The `value` of one of its `options`.|
|`Flag`|`true` or `false`.|
|`List`|Each item's answers to its own choices, in the order the user puts the items in when `canReorder` is set. An item that is not answered is kept as it is, where it is.|

A notice of `rdcore/refactor/prepare` is a `Refusal`, which no answer changes, or a `Change` of what the code does, which the user accepts by its `code`. It says where (`location`) and, when it concerns another place too, that place (`related`).

The answer also gives the range the refactoring takes (`range`), refused or not, for a dialog to show what will change. For Extract Method, it is the whole statements the selection touches. It is `null` when there are none: the range holds no statement, or cuts a block.

**`rdcore/refactor/checkIdentifier`** ([`RefactorIdentifierCheckParams`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.RefactorIdentifierCheckParams.html)) checks an identifier the user is typing: whether it can be declared where the refactoring declares it, and what it would hide. It sends the same kind, document and range, with the answers so far, and a dialog sends it as its user types. The answer to each `Name` choice is checked; no other answer is. It is answered the notices the identifiers raise ([`RefactorIdentifierCheckResult`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.RefactorIdentifierCheckResult.html)), each with the choice it answers (`choice`, and `item` for a choice of an item of a list). It applies nothing. A name that is not answered raises nothing, and a range that `rdcore/refactor/prepare` refuses raises nothing more. A `Refusal` of an identifier refuses the refactoring with that identifier only: the user can type another.

For Extract Method:

|The name typed|Raises|
|---|---|
|One the code cannot declare there: a keyword, no identifier, a name the module or the procedure declares, or one the module writes where it means something else|A `Refusal`. `rdcore/refactor/apply` answers the same name with an error, which says the same.|
|A name declared outside the module, which the new procedure hides there|A `Note`, with the declaration it hides (`related`). The refactoring takes the name.|

**`rdcore/refactor/apply`** ([`RefactorApplyParams`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.RefactorApplyParams.html)) sends the same kind, document and range, with the answers and the codes of the changes the user accepts. A choice that is not answered takes its default. It is answered the edit ([`RefactorWorkspaceEdit`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.RefactorWorkspaceEdit.html)): the documents it creates, then the text edits of each document, of the version the client has. The client applies it as one change. A refactoring that is refused, or that would make a change the user did not accept, is answered with an error (`RequestFailed`, -32803) whose message says why, in the language of the user interface.

The messages are plain records of `RDCore.SDK.Protocol`, with no OmniSharp type: a client that has only that package writes and reads them.

### `workspace/executeCommand`

The language server runs the commands it advertises (`executeCommandProvider.commands`) when a client sends `workspace/executeCommand` (**LSP 3.17** §Execute a command) ([`IServerCommand`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Server.Commands.IServerCommand.html)). A command a client runs itself, such as `rdcore.rename`, is not advertised. A command that is refused is answered with an error (`RequestFailed`, `-32803`) whose message says why, in the language of the user interface, for the client to show its user. What a command asks its user is asked with `window/showMessageRequest`, its choices as the request's actions.

|Command|Arguments|Does|
|---|---|---|
|`rdcore.importProject`|None, a URI, or `{ "uri", "text", "overwrite" }`|Imports a VB6 project ([**RD-VBAL §2.2.3.5**](rd-vbal.2.2.3.projectfile.md#2235-importing-a-vb6-project)).|

`rdcore.importProject` imports the `.vbp` its URI names. A URI of a folder, or none, is that folder, or the workspace's: a folder with one `.vbp` imports it, a folder with several asks the user which one, and a folder with none is refused. `text` is the `.vbp` as the client holds it, which the server reads instead of the file. A folder that has a `.rdproj` already asks the user to replace it (`window/showMessageRequest`), and is refused unless the user chooses to; `overwrite: true` replaces it without asking anything, which is what a client that imports its `.vbp` again after each change sends. Either way, what the `.vbp` does not describe is kept ([**RD-VBAL §2.2.3.5**](rd-vbal.2.2.3.projectfile.md#2235-importing-a-vb6-project)). The project of the workspace's folder is written by the server, the only writer of its project file, and the workspace is reloaded with it: when that changes the workspace, the client is asked to refresh what it shows of every document, as for any change of the project ([Refreshing what the client shows](#refreshing-what-the-client-shows)). A project of another folder is written there, and the workspace is left as it is.

What the file system refuses is a refusal (`RequestFailed`): a `.vbp` that cannot be read, or a `.rdproj` that cannot be written (read-only, or in a folder that cannot be written to), is said on one line ("Couldn't write '…\.rdproj': access to the file is denied."), and the project is left as it was.

It answers what it wrote: the URI of the `.rdproj` (`projectFile`), the project's `name`, its number of `modules`, and what was skipped (`notes`), each with its `kind`, its `subject` as the `.vbp` writes it, and a `message`.

### `textDocument/codeLens` and `codeLens/resolve`

The language server answers `textDocument/codeLens` (**LSP 3.17** §Code Lens Request) with a lens at the name of each member a module declares (`SymbolLenses`): a `Sub`, a `Function`, each procedure of a property, a `Declare` statement, an `Event`, a `Type` and an `Enum`. A variable or a constant of the module has none, nor a field of a `Type` or a member of an `Enum`. A `.rdc` program has none: its procedure is the one its lines are the body of.

A lens is answered without its command (`resolveProvider = true`), since its references are found in every document of the workspace: a client resolves the lenses it shows (`codeLens/resolve`), and the server finds the references of each when it is resolved. Its references are those of `textDocument/references` at its name, its declarations not included: the lens of a `Property Get` counts the names that read the property, and the lens of its `Property Let` or `Property Set` the names that assign it. A property that nothing assigns shows `0 references` above its `Property Let`. The result a function or a `Property Get` assigns or reads in its own body is no reference; a recursive call is.

|Resolved lens|Is|
|---|---|
|`command.title`|The count, in the language of the user interface: `0 references`, `1 reference`, `3 references` (`0 référence`, `1 référence`, `3 références`).|
|`command.command`|`rdcore.showReferences` ([`RefactoringCommands.ShowReferences`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Semantics.Refactoring.RefactoringCommands.html)), a command a client runs itself: it shows its user the references.|
|`command.arguments`|The URI of the document, the position of the name, and the references (`Location[]`): the arguments of VS Code's `editor.action.showReferences`.|

### `textDocument/typeDefinition` and `textDocument/implementation`

The language server answers `textDocument/typeDefinition` (**LSP 3.17** §Goto Type Definition Request) with where the type of the symbol named at the
position is defined: the class, `Type` or `Enum` a variable, a constant, a parameter or a function is declared as (for an array, its items' type). A name that
names a type leads to the type. An intrinsic type, or a type of a library, has no place in the workspace, and the answer is empty.

It answers `textDocument/implementation` (§Goto Implementation Request) with what implements the symbol (**MS-VBAL §5.2.4.2**): for a class, each class of
the workspace that implements it, at its `Implements` directive; for a member of a class, the procedure of each of those classes that implements it
(`IShape_Area` for `Area` of `IShape`, each accessor of a property). A class that no class implements, or a symbol that is neither a class nor a member of one,
has no implementation. Both are answered as `textDocument/definition` is: links, or locations for a client that does not read them.

### `workspace/configuration` and `workspace/didChangeConfiguration`

The settings a user changes while the platform runs travel as **LSP 3.17** has them (§Configuration Request, §DidChangeConfiguration Notification): the client
owns its user's settings, and a server asks for them.

|Message|Direction|Does|
|---|---|---|
|`workspace/configuration`|Server to client|Asks for the section `rdcore`: the live settings, keyed as the platform's configuration is (`rdcore.Server.Verbose`). A server asks when it is initialized, and whenever its client says they changed.|
|`workspace/didChangeConfiguration`|Client to server|Says the live settings changed. Its `settings` are not read: the server asks for the section.|
|`workspace/diagnostic/refresh`, and the other refreshes|Server to client|Asks a client to pull again what it shows of every document, once the settings changed ([Refreshing what the client shows](#refreshing-what-the-client-shows)).|

Every client of the platform says it answers `workspace/configuration` (`workspace.configuration`), and answers with what its component hands down
([`LiveSettingsProtocol`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Server.Configuration.LiveSettingsProtocol.html)): the interactive shell, with the live settings of its user's
settings; the language server, as the client of its components, with what its own client said. Every server asks, and sets what it is told on its settings
([**RD-VBAL §2.3.3** Configuration](rd-vbal.2.3.3.configuration.md#live-settings)). The language server says each change to the components it is the client of.

### `textDocument/selectionRange`

The language server answers `textDocument/selectionRange` (**LSP 3.17** §Selection Range Request) with what a selection at each position grows to, step by
step: each node of the syntax tree written around the position, from a name through its expression, its statement, the block of a structured statement and
the procedure, to everything the module declares (`SyntaxSelection`). Each step is in the next.
A position that no node is written around is answered with the empty range at it. For a `.rdc` program, ranges are in its lines.

### `textDocument/formatting` and `textDocument/rangeFormatting`

The language server answers `textDocument/formatting` (**LSP 3.17** §Document Formatting Request) with the edits that indent every line of a module, and
`textDocument/rangeFormatting` (§Document Range Formatting Request) with those of the lines a range is on; a range that ends at the start of a line, a selection
of whole lines, does not take in that line, and a range that starts or ends inside a continued statement takes in all of its lines. Only whitespace changes
([`ModuleIndenter`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Formatting.ModuleIndenter.html)). A line ends with a carriage return, a line feed, or both, as the protocol counts them.

A line is indented a level (`Formatting.IndentSpaces` spaces) for each body that encloses it in the syntax tree, whatever the range:

|Body|Indented|
|---|---|
|A procedure's|When `Formatting.IndentEntireProcedureBody` is on.|
|An `Enum`'s or a `Type`'s members|Always.|
|What an `If`, `ElseIf` or `Else` guards; a loop (`For`, `For Each`, `Do`, `While`); a `With` block|Always. A block's opener, its branches and its closer are at the block's level, and so is a line that has the closer after other code (`x = 1: End If`).|
|The `Case` lines of a `Select Case`, and what each guards|What a `Case` guards, always; the `Case` lines, when `Formatting.IndentCase` is on.|
|What an `#If`, `#ElseIf` or `#Else` guards|When `Formatting.IndentCompilerDirectives` is on.|

- **A line the syntax tree has no statement of**, code the parser could not build as it is typed, is indented by its keywords, from the level of where it is: a block's opener indents the lines after it, and its closer ends that. A block or an `#If` the closer of which is not written yet is open to the last line. A module is never refused.
- **A procedure whose header is written in each branch of an `#If`** (`#If VBA7 Then` / a header / `#Else` / a header / `#End If`, then one body) has the `#If` block as its header: its lines are at the procedure's level, and the body after the `#End If` is indented once.
- **A line number** stays at the start of its line, and so does a line label, as the VBE has it: the code after either is where the line's level has it, a space after the number or the label at least, and nothing after one that has no code.
- **A line continued from the one before** (`_`) lines up with the lines of its statement before it, when `Formatting.AlignContinuations` is on: with the argument it continues (the one after a comma, a named argument's value after its `:=`), the innermost argument list that is open, the value of an assignment, or what follows the name the statement starts with. When there is none of them, it is a level further in than the statement. Parentheses, commas and `=` in a string literal are not counted. A line that starts with `+` or `&` has the operator before that column, so that the operands line up, when `Formatting.IgnoreOperatorsInContinuations` is on. The lines of a declaration, of a member of an `Enum` or a `Type`, and of a comment are where their statement starts; what continues an end-of-line comment, or a comment on a line of its own inside a statement, is where that comment is. With `AlignContinuations` off, every line of a statement is where it starts.
- **A comment alone on its line** is at the level of the code around it, or where it is written when `Formatting.AlignCommentsWithCode` is off.
- **An empty line** is left as it is, emptied of its whitespace, or indented to its level, as `Formatting.EmptyLineHandlingMethod` says (`Ignore`, `Remove`, `Indent`).
- **In column 1**, whatever its level: an `#If`, `#Else` or `#Const` line (`Formatting.ForceCompilerDirectivesInColumn1`); a line that starts with `Debug.Print` (`Formatting.ForceDebugPrintInColumn1`), `Debug.Assert` (`ForceDebugAssertInColumn1`) or a `Stop` (`ForceStopInColumn1`), or any of the three (`ForceDebugStatementsInColumn1`).
- **The first blocks of a procedure**, the comments and the declarations (`Dim`, `Const`, `Static`, `Public`, `Private`) right after its header, in any order, up to its first other line or its first empty line, are in column 1 when `Formatting.IndentFirstCommentBlock` or `IndentFirstDeclarationBlock` is off. An empty line does not end them when `IgnoreEmptyLinesInFirstBlocks` is on. With `IndentEnumTypeAsProcedure` on, the first comments of an `Enum` or a `Type` are in column 1 when `IndentFirstCommentBlock` is off.
- **A declaration's `As`**, the first of a `Dim`, `Const`, `Static`, `Public` or `Private` line and of each line it is continued on, starts in column `Formatting.AlignDimColumn` when `Formatting.AlignDims` is on; a space before it at least.
- **An end-of-line comment** is where `Formatting.EndOfLineCommentStyle` has it: at the column it was written at (`Absolute`), after the gap it had (`SameGap`), twice `IndentSpaces` after the code (`StandardGap`), or in column `Formatting.EndOfLineCommentColumnSpaceAlignment` (`AlignInColumn`); a space after the code at least. An apostrophe in a string literal or in a bracketed name starts no comment.
- **The header of a class or a form module** (`VERSION`, `BEGIN` ... `END`) and its `Attribute` lines are left as they are written.
- **Between procedures** (a `Sub`, a `Function`, a `Property`, an `Enum` or a `Type`, with the comments right above it), there are `Formatting.LinesBetweenProcedures` empty lines when `Formatting.VerticallySpaceProcedures` is on, and so many after what is above the first procedure: its declarations, its `Option` statements, a `#Const`. With `Formatting.GroupRelatedProperties` on, the accessors of a property that follow one another have none between them. Nothing is spaced inside a procedure, an `Enum` or a `Type`, nor after an `#If`, `#ElseIf` or `#Else` line, the module's header or its attributes. An empty line the spacing writes is as `Formatting.EmptyLineHandlingMethod` has an empty line: indented to its level when it says `Indent`, empty otherwise. A range spaces only a gap it takes in whole, from the line above it to the first line of the procedure after it, so that it changes no line outside of it.

Only whitespace changes: an edit is a run of it that is not what it is to be (the indentation of a line, the gap before an `As` or a comment), or the empty lines between two procedures, from the end of the line before them, written with that line's line end.

The settings are the server's (`Formatting`, [**RD-VBAL §2.3.3**](rd-vbal.2.3.3.configuration.md#formatting)), read as they are when the request is made. Of the request's options (`FormattingOptions`), none is honoured: the `tabSize` is the editor's, which reaches the server through the configuration it owns, as `Formatting.IndentSpaces` when the user never set it; `insertSpaces: false` is not, since the code of a VBA module is indented with spaces as the VBE writes it; and `trimTrailingWhitespace`, `trimFinalNewlines` and `insertFinalNewline` are not either, since only the whitespace before the code of a line and between its parts is the indenter's. A change to the document that arrives meanwhile ends the request, answered `ContentModified` (-32801). A `.rdc` program is given no edit: the shell indents its lines itself, the same way (`ProgramIndenter`). It stores each statement indented by the blocks it is in, after the line's number. An edit marks the program to be indented again, and it is indented once, before anything reads it as a whole: `LIST`, `SAVE`, `RUN`, the document the language server holds. Lines pasted at the prompt are indented once, and are not told to the server one by one. A change of the shell's `Formatting` settings indents it again too. `SAVE` writes the program indented, so a `.rdc` file formatted by hand takes the shell's indentation once it is loaded and saved again. A program the indenter cannot read is kept as it was typed. The statements in no block are at the left whatever `Formatting.IndentEntireProcedureBody` says, since the procedure a program is the body of is one its text does not write.

> [!NOTE]
> Two of Rubberduck's indenter cases are not met: a line number on a line of its own inside a continued statement is not placed, and a hexadecimal line number
> is not listed in decimal, as the VBE lists a line from its compiled form.

### `textDocument/foldingRange`

The language server answers `textDocument/foldingRange` (**LSP 3.17** §Folding Range Request) with what folds in a module:

|What|From, to|Kind|
|---|---|---|
|A member declaration that spans more than one line: a procedure, a property accessor, a `Type`, an `Enum`, a `Declare` broken over lines|its first line, to its `End` statement|`region`|
|A block that spans more than one line, however deep: `If`, `Select Case`, `For`, `For Each`, a `Do` or `While` loop, `With`|its first line, to its `End` (`End If`, `Next`, `Loop`, ...)|none|

The branches of a block (`ElseIf`, `Else`, `Case`) fold with it, not apart; a single-line `If` does not fold. A client tells a block from a member by the kind.
For a `.rdc` program, ranges are in its lines, and the procedure its lines are the body of has none.

The interactive shell folds blocks only. `FOLD <line>` folds the block that starts at that line, or unfolds it; a line that starts none is the error
`NO FOLDING EXISTS AT <line>`. `LIST` has two lanes in its margin: a breakpoint's mark at the edge, then the mark of a block
(▽ open, ▲ folded; `-` and `+` on a console that draws no Unicode), next to the line. A folded block is listed as its first line, with how many lines it hides before its text (`20 [4 lines] If X < 0 Then`), faint as ghost text and cut with `…` to the console's
width. A breakpoint it hides shows in the margin of that line, an enabled one first, when the line has none of its own. A fold is on a line number, as a breakpoint is, and `NEW` and `LOAD` clear them.

### `workspace/didChangeWatchedFiles`

The language server reads the project file (`.rdproj`) when it initializes. The server is the project file's only writer: a client tells it which files it created,
renamed or deleted (see [`workspace/didCreateFiles`, `didRenameFiles` and `didDeleteFiles`](#workspacedidcreatefiles-didrenamefiles-and-diddeletefiles)), and does
not write the project file itself. A project file or a source file changed outside the editor (a `git checkout`, a module exported from the VBE, a hand edit)
is what a client says changed, as **LSP 3.17** has it (§DidChangeWatchedFiles Notification): the client owns the file system.

|Message|Direction|Does|
|---|---|---|
|`client/registerCapability`|Server to client|Asks a client that registers capabilities dynamically (`workspace.didChangeWatchedFiles.dynamicRegistration`) to watch `**/.rdproj`, created or changed, and the source files of every language the platform serves (`**/*.{bas,cls,frm,doccls,rdc}`), created, changed or deleted. A client that does not is not asked.|
|`workspace/didChangeWatchedFiles`|Client to server|Says files of the workspace changed. The server serves it whether or not it asked for it: a client that registers nothing sends it all the same. The workspace's `.rdproj`, created or changed, is read again (below); another folder's `.rdproj`, and a deletion, are left alone, and so is a project file that lists what the workspace is, as the one the server wrote does when a client that watches it says it changed. A file the project lists is read again ([below](#a-file-changed-outside-the-editor)); one it does not list is nothing of the workspace.|
|`rdcore/host/project/reload`|Language server to environment host|Asks the host to compose its runtime session again from the project file as it is now ([`ProjectReload`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.ProjectReload.html)). A class module's symbol is the project's, so a class the project gained is a type only once the session is composed again. A program that is running or waits keeps the session it runs in.|
|`workspace/diagnostic/refresh`, and the other refreshes|Server to client|Asks a client to pull again what it shows of every document, once the workspace is defined again ([Refreshing what the client shows](#refreshing-what-the-client-shows)).|
|`window/showMessage`|Server to client|Says why the workspace is as it was (the project file could not be read), or that a running program keeps its modules.|

On the notification, the language server (`WorkspaceReloadService`):

1. reads the project file again; one that cannot be read leaves the workspace as it was, and one that lists what the workspace is changes nothing;
2. loads and parses each file it lists that was not loaded, and takes out each one it no longer lists (a document the client has open stays: its text is the client's);
3. asks the environment host to compose its session again;
4. defines the whole workspace again in its three phases (its types declared, its declarations bound, its code loaded), each module replacing what the host held of it. When the host kept its session, a module the project lost is discarded from it (`rdcore/host/discard`) first;
5. asks the client to refresh what it shows of every document.

#### A file changed outside the editor

For a file the project lists that changed, was created or was deleted, the language server (`WorkspaceReloadService.ReloadFilesAsync`) reads it again: a module
is parsed again, a module whose file is gone is discarded from the environment host and its document is missing, and the whole workspace is defined again in its
three phases, since another module may refer to what changed. A document the client has open is the client's, and is not read; a file whose text is what was
loaded changes nothing, as when the server wrote it. The client is then asked to pull its diagnostics again.

#### The server's own watch

The client owns the file system. Only for a client that does not say it watches files (no `workspace.didChangeWatchedFiles` capability at all, with or without
dynamic registration) does the language server watch the workspace's files itself (`WorkspaceFileSystemWatcher`): its `.rdproj`, and the source files under its
root, except what a folder whose name starts with a dot holds (`.git`, `.github`). What it sees is served as if the client had said it. The events of a moment (a
save is several, a `git checkout` many) are gathered and served together, once none came for 300 ms.

The setting `Workspace.EnableFileSystemWatchers` (default `true`, read when the workspace loads) turns the server's watch off altogether: a file changed outside
the editor is then read when the server next starts.

### `workspace/didCreateFiles`, `didRenameFiles` and `didDeleteFiles`

A client that creates, renames or deletes files of the workspace says so as **LSP 3.17** has it (§Workspace, DidCreateFiles, DidRenameFiles and DidDeleteFiles
Notifications), and the server makes the project what the folder holds now: it writes the `.rdproj`, of which it is the only writer, and the workspace is made what the
project lists, as on [`workspace/didChangeWatchedFiles`](#workspacedidchangewatchedfiles) (steps 2 to 5). The server says it handles the three in its `initialize`
result (`workspace.fileOperations`), with filters for the source files of every language the platform serves (`**/*.bas`, `**/*.cls`, `**/*.frm`, `**/*.doccls`,
`**/*.rdc`) and for folders.

|Notification|The project|
|---|---|
|`workspace/didCreateFiles`|A source file of the workspace's language (**RD-VBAL §2.0.2.1.1**) is a module of the project, and each folder it is in a folder of the project. A created folder is added with the source files it holds. Another file is nothing of the project.|
|`workspace/didRenameFiles`|What the project lists is listed under its new path, a folder with what it holds; a document module keeps its document class. A module renamed to what is not a source file is no longer a module; a file renamed to a source file is one.|
|`workspace/didDeleteFiles`|What the project listed is no longer listed, a folder with what it held.|

What is outside the workspace root, and what a folder whose name starts with a dot holds (`.git`, `.github`), is nothing of the project. A module name is unique in a
project (**RD-VBAL §2.2.3.3**): a file created with the `VB_Name` of a module the project has is not added, and `window/showMessage` says so. The operations are made
in the order the client sent them: the notification returns once the project file is written and the workspace's documents are what it lists, so that what the
client sends next (`textDocument/didOpen` of the new path, `didChange`) is read after it. The environment host composing its session again and the symbols being defined
again follow, in the order the changes were made, and nothing the client sends waits for them: a host that is starting, or down, holds up none of it. The client's
diagnostics are asked to be pulled again once they are done.

> [!NOTE]
> OmniSharp 0.19.9 reads `workspace/didRenameFiles` with a `FileRename` that has only a `uri`, and neither the `oldUri` nor the `newUri` a client sends. RDCore reads it
> with a type of its own ([`DidRenameWorkspaceFilesParams`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.DidRenameWorkspaceFilesParams.html)), as the protocol states it.

A folder with no `.rdproj` is the project it holds (**RD-VBAL §2.2.3**): the first file operation writes it, and opening the folder does not.

### `window/workDoneProgress`

The language server reports the progress of its work to a client that shows it (`window.workDoneProgress`; **LSP 3.17** §Work Done Progress):

- **Work for a request:** reported to the token the request gave (`workDoneToken`). `rdcore/session/analyze` and `textDocument/diagnostic` report the parse, the semantic model, and each diagnostics provider as it answers.
- **Work the server does of its own:** reported to a token it creates (`window/workDoneProgress/create`). As the workspace comes up, it reports:
  - parsing the workspace, a module at a time;
  - composing the project: each module defined, then its code loaded;
  - the first analysis of every document.

Progress is for work that takes longer than an answer does. Work that is done within the threshold ([`WorkProgressReporter.DefaultThreshold`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Server.Progress.WorkProgressReporter.html), 300 ms) is reported nothing, and no token is created for it. Work that is still going on then begins, with the last step it reported. Once begun, it always ends, whatever comes of it; work that fails ends with its error's message. Its percentage only goes up.

The code that does the work reports to an `IProgress<WorkProgressStep>` ([`IWorkProgress`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Server.Progress.IWorkProgress.html)), and knows nothing of LSP.

A client tracks what it is reported with [`WorkProgressTracker`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.Progress.WorkProgressTracker.html). It shows the work for its requests (`TrackWorkDone`) and the work the server begins (`WorkProgressOrigin.Server`). The interactive shell draws each piece of work as a bar, as PowerShell's `Write-Progress` does:

- while a line is edited at the prompt, under it;
- while a command runs, the work for that command, on the line its output starts on, cleared before the output is written.

The work the server begins of its own is not drawn among a command's output.

### Call hierarchy and type hierarchy

The language server answers `textDocument/prepareCallHierarchy`, `callHierarchy/incomingCalls` and `callHierarchy/outgoingCalls` (**LSP 3.17** §Call
Hierarchy) (`SymbolHierarchy`):

- An item is a procedure, a function, or a procedure of a property, that the workspace declares; a member of a library is none. Its `data` is the identity of
  its symbol, which is all the server needs to find it again when the client sends it back.
- The incoming calls of an item are the procedures that refer to it, each with where in it the names are written; a name written outside of every procedure
  is a call from the module. A call is a name bound the way a rename binds it, whatever writes it.
- The outgoing calls of an item are the procedures of the workspace whose names its body writes.
- A function's own name in its body is how it is given its result, as often as it is a call: a procedure is never its own caller, so a recursive call is not
  shown.

It answers `textDocument/prepareTypeHierarchy`, `typeHierarchy/supertypes` and `typeHierarchy/subtypes` (§Type Hierarchy) for the class modules of the
workspace: the supertypes of a class are the classes of the workspace it implements, and its subtypes the classes that implement it
(**MS-VBAL §5.2.4.2**).

### `rdcore/session/interaction`

A program runs in the environment host; the person it asks is at a client. A `MsgBox` or an `InputBox` travels in two hops, and the program waits for the answer as it waits for a dialog:

1. The environment host asks the language server (`rdcore/host/interaction/msgBox`, `rdcore/host/interaction/inputBox`).
2. The language server asks its client (`rdcore/session/interaction/msgBox`, `.../inputBox`) when the client expects [`SessionInteraction`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.SessionInteraction.html). Otherwise it answers that nobody was asked (`Handled = false`), and the environment host shows the dialog of the platform it runs on: a Windows dialog on Windows, and none elsewhere (the dialog answers as though it had been confirmed at once).

The request carries the lines the program completed before it opened the dialog (`Output`), so that the client shows them first; they are not repeated in the answer to `rdcore/session/execute`. An `InputBox` that was cancelled answers `Cancelled = true`, which the program is told with `vbNullString`.

While a dialog is open, the program waits for the client, and what the server finds of the documents waits for the program. So a diagnostics pull from the client (`textDocument/diagnostic` or `workspace/diagnostic`) is not left waiting: one that comes while a dialog is open, or is still running when one opens, is answered at once with `ServerCancelled` (-32802) and `DiagnosticServerCancellationData { retriggerRequest: false }` (**LSP 3.17** §Document Diagnostics). A client must not retrigger it: it would only ask again, and be declined again, while the dialog is open. Once the last open dialog is closed (answered, or not answered because the client failed to answer it), and only if a pull was declined while it was open, the server asks the client to refresh its diagnostics and nothing else (`workspace/diagnostic/refresh`). It asks once the burst of refreshes is over ([above](#refreshing-what-the-client-shows)), and the client pulls again. A client that takes no refresh is declined all the same: the first pull it makes once no dialog is open is answered. Nothing else the client asks is declined while a dialog is open.

The interactive shell draws a dialog as a box with a border, centred on the console, with its title in the top edge and its message wrapped inside. A `MsgBox` ends with a row of buttons, chosen with the arrow keys, <kbd>Tab</kbd>, <kbd>Enter</kbd>, or a button's key. Its icon has a column of its own, and the lines of the message hang under it. An `InputBox` ends with a line to type on, which starts with the default text selected and scrolls inside the box. <kbd>Esc</kbd> closes either at window level.

### `rdcore/formatting/indenterSettings`

A user who had Rubberduck has indenter settings (**RD-VBAL §2.3.3**, the `Formatting` settings). Rubberduck keeps them on the user's machine, in its own configuration and in the
registry, and a client owns its configuration: the language server does not read the machine of the user, and writes none of the client's settings. A client that expects
[`RubberduckIndenterSettings`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.RubberduckIndenterSettings.html) reads them for it, and takes the mapped settings as its own. Two requests, both sent by the
language server to the client:

**`rdcore/formatting/indenterSettings`** (params `{}`) asks what the client found. The client answers what it read, _as it is_, and maps nothing:

```json
{ "source": "RubberduckConfig", "values": { "IndentCase": "true", "IndentSpaces": "2" }, "tabWidth": null }
```

|Field|Is|
|---|---|
|`source`|`RubberduckConfig`, `SmartIndenterRegistry` or `None`. A client on a platform that has no such settings, or that found none, answers `None` and an empty `values`.|
|`values`|For `RubberduckConfig`, the children of the `IndenterSettings` element of `%APPDATA%\Rubberduck\rubberduck.config` (XML): each element's name, and its text. For `SmartIndenterRegistry`, each value of the key `HKCU\Software\VB and VBA Program Settings\Office Automation Ltd.\Smart Indenter` (`IndentProc`, `DebugCol1`, `AlignDimCol`, `EOLComments`, ...): its name, a number for a `REG_DWORD` and text for a `REG_SZ`.|
|`tabWidth`|`TabWidth` of the VBA editor (`HKCU\Software\Microsoft\VBA\6.0\Common`, or `7.0`) for `SmartIndenterRegistry`, which the Smart Indenter indented by; `null` otherwise.|

Rubberduck's own configuration is read when it has indenter settings, and the registry otherwise. The language server maps what the client read to `Formatting` settings
([`IndenterSettingsImport`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Formatting.IndenterSettingsImport.html)), so that every client says the same thing and the mapping is made once. A name of
`RubberduckConfig` is the setting of that name. For `SmartIndenterRegistry`:

|Value|Setting|
|---|---|
|`IndentProc`, `IndentFirst`, `IndentDim`, `IndentCmt`|`IndentEntireProcedureBody`, `IndentFirstCommentBlock`, `IndentFirstDeclarationBlock`, `AlignCommentsWithCode`|
|`AlignContinued`, `AlignIgnoreOps`, `IndentCase`|`AlignContinuations`, `IgnoreOperatorsInContinuations`, `IndentCase`|
|`DebugCol1`|`ForceDebugStatementsInColumn1`, `ForceDebugPrintInColumn1`, `ForceDebugAssertInColumn1` and `ForceStopInColumn1`, all four|
|`CompilerCol1`, `IndentCompiler`, `AlignDim`|`ForceCompilerDirectivesInColumn1`, `IndentCompilerDirectives`, `AlignDims`|
|`AlignDimCol`, `EOLAlignCol`|`AlignDimColumn`, `EndOfLineCommentColumnSpaceAlignment`|
|`EOLComments`|`EndOfLineCommentStyle` (`Absolute`, `SameGap`, `StandardGap`; `AlignInCol` is `AlignInColumn`)|
|`tabWidth`|`IndentSpaces`|

A flag is `Y` or it is not (an empty one says nothing); a number is kept to the limits of its setting, and a negative one says nothing; a setting the source says nothing of keeps
its default.

**`rdcore/formatting/indenterSettings/apply`** asks the client to take what was mapped as its own:

```json
{ "source": "RubberduckConfig", "settings": { "Formatting.IndentCase": "True", "Formatting.IndentSpaces": "2" } }
```

The answer is `{ "applied": 2, "refused": ["Server.Verbose"] }`: the number of settings the client set (`0` when it declines), and the keys of the ones it did not take. A client that answers `applied` only is told to have left some out by their number, and a client that took them all, or declined, is told nothing more. The client sets them in its own configuration, as a change of its own (the interactive
shell writes them to the user's settings), and says so as it says any change (`workspace/didChangeConfiguration`); the language server sees them in the section it pulls. There is
no other way for the language server to write a setting of a client, and the client takes only the `Formatting` settings of the names Rubberduck has (the table above), refusing any other key.

The language server sends the requests only to a client that expects the capability:

- **On the first start**, once. When the client's first settings are in, the user set no `Formatting` setting, a file in the platform's user folder (`indenter-import.offered`) does not say it was offered, and the client found settings, the language server asks the user (`window/showMessageRequest`: _Import_ or _No thanks_) and remembers that it asked, whatever the answer:
  - a user who closes the question has answered it;
  - a client with nobody there to answer answers the request with an error instead, as the `rdc` shell does when its input is a script or it has ended, and the server asks again at the next start;
  - a client that expects [`Unattended`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.Unattended.html) is not asked at all that start, and is asked at a start where it does not expect it;
  - when the user chooses _Import_ and the client cannot apply the settings, the server asks again at the next start.
- **On request.** The command `rdcore.importIndenterSettings` ([`workspace/executeCommand`](#workspaceexecutecommand)) runs the two requests against the client that sent it, and answers `{ "source", "found", "applied", "message" }`. A client that does not expect the capability is refused with a message. That nothing was found is an answer, not a refusal.

The interactive shell expects the capability. It reads Rubberduck on Windows, and answers `None` elsewhere. `CONFIG IMPORT RUBBERDUCK` in the shell, and `rdc config import rubberduck` in command mode, do the same
without the language server: they read, map and set the settings for the user.
