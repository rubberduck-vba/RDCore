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

## 2.0.2.1.2 The language of the user interface

What a server says - the messages of its diagnostics, why a rename is refused, the titles of its code actions, its hovers - is said in the language the
client's user interface is in: the `locale` of the `initialize` request (**LSP 3.17**), such as `fr-CA` or `en`
([`ClientLocale`](../api/RDCore.SDK.Server.ClientLocale.html)). Every server of the platform applies it before it builds anything from the request,
and a language server tells the servers it starts the same `locale`, so that the diagnostics an extension finds are in the client's language too. A culture
reads its own language's messages, or its parent's: `fr-FR` and `fr-CA` read the French ones, and every other culture - `en-GB`, the invariant culture -
the English ones. Only the language of the user interface is the client's: how numbers and dates are written and read does not depend on it. A `locale`
the platform does not know is logged and ignored; a client that sends none is answered in the language the server's process starts in.

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
|[`ProjectReload`](../api/RDCore.SDK.Client.ProjectReload.html)|`rdcore/host/project/reload`|`EnvironmentHost`|Lets the language server ask the environment host to compose its runtime session again from the project file as it is now, once a client said the `.rdproj` changed (see [below](#workspacedidchangewatchedfiles)).|
|[`CliCommand`](../api/RDCore.SDK.Client.CliCommand.html)|_(none: in-process CLI dispatch)_|`ClientApp`, `Extension`|Advertises that the declaring component contributes `rdc.exe` command-mode verbs. The CLI (`rdc.exe`) declares it for its native verbs. An extension declares it so that `rdc.exe describe-ext` records the capability in the extension's manifest (see [**RD-VBAL §1.1.5** Extension Manifest](rd-vbal.1.1.5.extension-manifest.md)).|
|[`DiagnoseDocument`](../api/RDCore.SDK.Client.DiagnoseDocument.html)|`rdcore/diagnostics/document`|`Extension`|Advertised by a _diagnostics provider_: a platform extension whose manifest advertises this capability. A provider declares it with `[assembly: ProvidesCorePlatformClientCapability<DiagnoseDocument>]` (see [below](#rdcorediagnosticsdocument)).|
|[`SessionInteraction`](../api/RDCore.SDK.Client.SessionInteraction.html)|`rdcore/session/interaction/msgBox`, `.../inputBox`|`LanguageServer`|Relays the dialogs a program opens (`MsgBox`, `InputBox`) to a client that expects this capability, which answers them. The interactive shell expects it, and asks at its console (see [below](#rdcoresessioninteraction)).|
|[`SessionCompletion`](../api/RDCore.SDK.Client.SessionCompletion.html)|`rdcore/session/completion`|`LanguageServer`|Lists what can be written at a position of a module the client supplies, with the list `textDocument/completion` answers with. For a line typed at a prompt, which is in no document (see [below](#textdocumentcompletion-and-rdcoresessioncompletion)).|
|[`SessionSignatureHelp`](../api/RDCore.SDK.Client.SessionSignatureHelp.html)|`rdcore/session/signatureHelp`|`LanguageServer`|Says what the call being written at a position of a module the client supplies takes, with the signature help `textDocument/signatureHelp` answers with. For a line typed at a prompt (see [below](#textdocumentsignaturehelp-and-rdcoresessionsignaturehelp)).|

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

### `textDocument/inlayHint`

The language server answers `textDocument/inlayHint` (**LSP 3.17** §Inlay Hint Request) with, before each positional argument of the calls in the range,
the name of the parameter it is given to: `MsgBox Prompt: "Hello", Buttons: vbOKOnly`
([`ParameterHints`](../api/RDCore.SDK.Semantics.Refactoring.ParameterHints.html)). A call is an argument list after a name the static pass binds to a
procedure, a function or a property - of the workspace or of a library it references; the subscripts of an array have no hint. A named argument, and every
argument after it, has none; nor has an argument that is a name written as its parameter's, or an argument of a `ParamArray` after its first. Each hint is
of kind `Parameter`, complete as it is answered (no `inlayHint/resolve`). For a `.rdc` program, positions are in its lines.

### `textDocument/completion` and `rdcore/session/completion`

The language server answers `textDocument/completion` (**LSP 3.17** §Completion Request) for the documents it holds, a `.rdc` program included. Its trigger characters are `.` and `!`. The list is complete (`isIncomplete = false`): a client narrows it by what is typed after it. Items are not resolved (`resolveProvider = false`).

What is typed of the name when the list is asked for narrows it already: an item is in the list when its label has the letters typed, in their order, in any case ([`CompletionCandidates.Matching`](../api/RDCore.SDK.Model.Symbols.Completion.CompletionCandidates.html)) - `gtc` keeps `GetCount`. Whatever a client narrows by as more is typed - the start of a label, a part of it, letters apart - an item it could show is in the list, so the list stays complete. A list asked for where nothing is typed yet (`Ctrl+Space` after a space) has every name. An item has no `insertText` and no `filterText`: both are its label, which is what a client takes without them.

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

A line at a client's prompt can also be one of the client's own commands. A client registers the verbs of its commands with `rdcore/session/commands` ([`SessionCommandsParams`](../api/RDCore.SDK.Platform.Protocol.SessionCommandsParams.html)), each described by a [`VerbDescriptor`](../api/RDCore.SDK.Platform.Commands.VerbDescriptor.html): the forms its arguments take, which are keywords, choices, line numbers, expressions, paths, text, settings and their values.

A verb is registered once. It is registered again only when its descriptor changes (a choice of what the client has more or fewer of), and then it replaces what it was. A completion request names the verb and what is typed after it (`Command`), and nothing more of the verb travels. The language server completes each argument as described ([`VerbCompletion`](../api/RDCore.SDK.Platform.Commands.VerbCompletion.html)):

- a keyword or a choice with its values;
- a line number with the numbered lines of the module, each shown with its statement;
- an expression where it is evaluated: before the line its form names (`EvaluatedAt`), in that line's procedure and blocks, which is where a breakpoint's program waits. An expression that names no line, or a line the module does not have, is completed at module level: the module's names and the project's, and no procedure's locals;
- a setting a part at a time, as a member is: the sections of the settings the argument carries (`Settings`), then what is in the section written before the last `.` - a section of it, or a setting with its description. The client asks again after each `.`;
- the value of a setting with the values of the setting its form names (`ValueOf`), as it is written before it: `True` and `False`, or the names of a choice. Other values are offered nothing;
- a path or text with nothing.

A verb the client did not register is offered nothing. The interactive shell registers `BREAK` (`CLEAR`, `TOGGLE <line>`, `<line> [condition]`, its condition evaluated at its line), `THEME` (`LIST`, `<theme>`) and `CONFIG` (`RESET [setting]`, `<setting> [value]`; [**RD-VBAL §2.3.3** Configuration](rd-vbal.2.3.3.configuration.md)) the first time one is completed. A command it does not describe is offered nothing after its verb.

### `textDocument/signatureHelp` and `rdcore/session/signatureHelp`

The language server answers `textDocument/signatureHelp` (**LSP 3.17** §Signature Help) for the documents it holds. Its trigger characters are `(`, `,` and a space, which starts the arguments of a call statement; `)` triggers it again, for the call around the one it closes.

The call is read off the line before the cursor ([`SignatureSite`](../api/RDCore.SDK.Model.Symbols.Completion.SignatureSite.html)):

|Written|The call|
|---|---|
|An argument list that is open after a name: `Left(s, `, `o.Item(`, `.Add(`|The innermost one. An argument list after no name - the parentheses of an expression - is no call, and the one around it is the call.|
|A name, a space, and arguments, at the start of a statement: `MsgBox "a", `|The call statement's own (**MS-VBAL §5.4.2.1**). An assignment is not one.|
|In a string literal or a comment|None.|

The argument at the cursor is counted by the commas before it in its argument list. A named argument (`Title:=`) is the parameter it names; an argument after the last parameter is the `ParamArray`'s, when the last parameter is one.

What is called is found as a completion list finds it: a name in the scope of the line, or a member of what its qualifier is, an object created by name included. The answer has one signature ([`SignatureDescription`](../api/RDCore.SDK.Model.Symbols.Completion.SignatureDescription.html)): the procedure's signature on one line, each parameter by its range in the line, the active parameter, and what the procedure and each parameter do. An instance member's implicit `Me` is none of its parameters. The answer is `null` where no call is written, or what is called is no procedure.

What a procedure does is its doc string ([`SymbolProperties.DocString`](../api/RDCore.SDK.Model.Symbols.Abstract.SymbolProperties.html)), a sentence:

|Procedure|Doc string|
|---|---|
|Of a module of the workspace|Its `Attribute <member>.VB_Description`.|
|Of a referenced library, or of an unreferenced one an object is created of by name|The `description` of the member in the library's description file.|
|Of the standard library|An entry of the platform's resources, in the language of the user interface, for the member and for each parameter. A `$` function has the doc strings of the one it is the `String` version of.|

> [!NOTE]
> **Not implemented.** The standard library's doc strings cover the `Strings`, `Interaction`, `Conversion`, `Math`, `Information` and `DateTime` modules, and the parameters of the members of `Strings`, `Interaction` and `DateTime` that take several, and of `Round` and `RGB`. The other modules' members have a signature and no doc string; a parameter with none shows none.

`rdcore/session/signatureHelp` ([`SessionSignatureHelpParams`](../api/RDCore.SDK.Platform.Protocol.SessionSignatureHelpParams.html)) answers the same for a module the client supplies with its source. The interactive shell asks it for the line at its prompt, and draws the signature under the line with the active parameter underlined, and what the procedure and that parameter do under it. A line that is one of the shell's commands is no call.

### `textDocument/rename` and `textDocument/prepareRename`

The language server answers `textDocument/prepareRename` and `textDocument/rename` (**LSP 3.17** §Rename Request, §Prepare Rename Request) for the documents it holds, a `.rdc` program included (`renameProvider.prepareProvider = true`). A rename renames a symbol: the names that refer to it, in every document of the workspace, and no other name - a text that is the same in a comment, a string, or another procedure's local is not renamed ([`SymbolRename`](../api/RDCore.SDK.Semantics.Refactoring.SymbolRename.html)).

`textDocument/prepareRename` answers the range of the name at the position and the name the symbol is declared with (`placeholder`). `textDocument/rename` answers a `WorkspaceEdit` of `documentChanges`, one `TextDocumentEdit` per document whose text changes, at the version the server holds. An edit replaces a name only: a type-declaration character after it stays (`s$`).

What a name refers to is what the static semantics resolve it to (**MS-VBAL §5.6.10**, §5.6.12):

|Written|Refers to|
|---|---|
|A name in a procedure, a constant's expression, an array's bounds|What it resolves to in the scope it is written in.|
|A member, after a `.`, a `With` block's included|The member of the declared type of the object.|
|A named argument (`count:=`)|The parameter of the procedure that is called.|
|A member's attribute (`Attribute Value.VB_UserMemId`)|The member.|
|The key of a dictionary access (`c!key`)|Nothing: it is a string.|

The accessors of a property (`Get`, `Let`, `Set`) are one name, and are renamed together. A member accessed late-bound - on an `Object` or a `Variant` - is not known to refer to the symbol, and is not renamed.

A rename that is refused is answered with an error (`RequestFailed`) whose message says why, in the language of the user interface, and where:

|Refused|Because|
|---|---|
|The new name is empty, not an identifier, or a keyword (**MS-VBAL §3.3.5**, §3.3.5.2)|Code cannot declare it ([`VBIdentifier`](../api/RDCore.SDK.Model.VBIdentifier.html)). A letter of another script is a letter: `Données` is a name.|
|The new name is declared where the symbol is|Two declarations would have one name.|
|The new name means something nearer, or as near, where the code refers to the symbol|The code would refer to that instead.|
|The symbol would be nearer, or as near, than what the new name means where the code writes it already|The code would refer to the symbol instead.|
|What a name written as the symbol's refers to is not known|The rename could miss it.|
|The symbol is a module, a class, a `Type` or an `Enum`|A type is not renamed yet.|
|The symbol is an event, a `WithEvents` variable, a form's control, or a member of a class another implements|What is named after it is not renamed yet.|
|The symbol is a procedure of a class module with an underscore in its name|It is named after the event it handles or the member it implements.|
|The symbol is an external procedure (`Declare`), or the workspace does not declare it|It is named by its library.|

A position is said the way the document says it: its file and line, and for a program of the BASIC, the number of its line.

### `textDocument/hover`

The language server answers `textDocument/hover` (**LSP 3.17** §Hover Request) for the documents it holds, over the name at the position, bound the way a
rename binds it ([`SymbolHover`](../api/RDCore.SDK.Semantics.Refactoring.SymbolHover.html)). A symbol of a library the workspace references is hovered as one
of the workspace is.

|Hovered|Declaration|Member of|
|---|---|---|
|A procedure, a function, a property|Its signature: `Public Function GetTotal() As Long`, `Public Property Get Value() As String`|Its module: `Helpers`, `VBA.Interaction`|
|A variable or a constant of a module|`Public Total As Long`, `Private Const Max As Long`|Its module|
|A local, a parameter|`Dim count As Long`, `Static p As Point`, `ByVal amount As Long`|Its procedure: `Program.Main`|
|An `Enum`, a `Type`|`Public Enum Colour`, `Public Type Point`|Its module|
|A member of an `Enum`, a field of a `Type`|`Red`, `X As Long`|Its `Enum` or `Type`: `Helpers.Colour`|
|A class or a module|`Class Class1`, `Module Helpers`|Nothing|

The hover is Markdown - the declaration in a block of VBA, then what declares it and the symbol's doc string - for a client that reads Markdown
(`hover.contentFormat`), and plain text for one that does not. "Member of" is said in the language of the user interface. No name, or a name whose meaning is
not known (a member accessed late-bound), has no hover.

### `textDocument/definition`

The language server answers `textDocument/definition` (**LSP 3.17** §Goto Definition Request) for the documents it holds, a `.rdc` program included. The name at
the position is bound the way a rename binds it (table above), so a navigation leads to what a rename of the name would rename
([`SymbolNavigation`](../api/RDCore.SDK.Semantics.Refactoring.SymbolNavigation.html)).

|Named|Defined by|
|---|---|
|A variable, a constant, a parameter, a procedure, an event, a `Type` or `Enum` and their members|Its declaration.|
|A property|Each of its procedures: its `Property Get`, `Let` and `Set`.|
|A class or a module, after `As` or `New`|Its document, at its start.|
|A name declared in each branch of an `#If`|Each declaration in a branch that is compiled, or each declaration when none is known to be (**MS-VBAL §3.4**).|
|A member of a library, or a member accessed late-bound - on an `Object` or a `Variant`|Nothing: the answer is empty.|

A client that reads links (`definition.linkSupport`) is answered `LocationLink`s: the name the navigation starts from (`originSelectionRange`), the whole
declaration (`targetRange`), and the name it declares (`targetSelectionRange`). Any other client is answered a `Location` per definition, at the name it
declares. Ranges are in the text of the document: for a program of the BASIC, its lines.

### `textDocument/references` and `textDocument/documentHighlight`

The language server answers `textDocument/references` (**LSP 3.17** §Find References Request) with each name, in every document it holds, that refers to the
symbol named at the position ([`SymbolNavigation.ReferencesAt`](../api/RDCore.SDK.Semantics.Refactoring.SymbolNavigation.html)): the names a rename of the
symbol would rename (table above), and, for a class, a module, a `Type` or an `Enum`, the names of the type in `As` clauses and after `New`. The names its
declarations declare are references when the client asks for them (`context.includeDeclaration`). The procedures of a property are one symbol: a reference
to its `Property Get` is a reference to its `Property Let`. A member accessed late-bound - on an `Object` or a `Variant` - is not known to refer to it, and is
not a reference.

`textDocument/documentHighlight` (**LSP 3.17** §Document Highlights Request) answers the references that are in the document, its declaration included,
each of kind `Text`: which reads the symbol and which assigns it is not told apart yet.

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
query ([`SymbolSearch`](../api/RDCore.SDK.Semantics.Refactoring.SymbolSearch.html)), regardless of case. Each is named with what it is declared in
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

The language server answers `textDocument/codeAction` (**LSP 3.17** §Code Action Request) for the documents it holds, with the kinds `quickfix` and `refactor.rename`. Every action is complete as it is answered (`resolveProvider = false`); a client that asks for some kinds only (`context.only`) is answered those and the kinds under them.

An analyzer states the fix of a finding with it ([`DiagnosticFixes`](../api/RDCore.SDK.Model.Diagnostics.DiagnosticFixes.html)), and the fix travels in the diagnostic's `data`, which a client sends back with the request. The platform makes the fix: an extension says what it is without knowing how the workspace is edited.

|Offered|Kind|Does|
|---|---|---|
|A rename that fixes a diagnostic, to each name its analyzer suggests|`quickfix`, preferred|Its `edit` is the rename's `WorkspaceEdit`. Offered only when the rename can be made.|
|A rename that fixes a diagnostic, to a name the user chooses|`quickfix`|Its `command` is `rdcore.rename`.|
|A rename of each name the range declares, or of the name at the position of an empty range|`refactor.rename`|Its `command` is `rdcore.rename`.|

`rdcore.rename` ([`RefactoringCommands.Rename`](../api/RDCore.SDK.Semantics.Refactoring.RefactoringCommands.html)) is a command a client runs itself: its arguments are the URI of the document and the position of the name. The client asks its user for the new name, and sends `textDocument/rename` with it, unless the user cancels. The titles are in the language of the user interface.

|Diagnostic|Fixed by|
|---|---|
|[RDC01001](../diagnostics/rdc01001.md) - a name that does not say what it is|A rename to a name the user chooses.|
|[RDC01002](../diagnostics/rdc01002.md) - Hungarian notation|A rename to the word after the prefix (`strName` to `Name`), or to a name the user chooses.|

The interactive shell asks for them with `DUCKY`, for its program as the document it is: `DUCKY` for the whole program, `DUCKY 20` for line 20. It pulls the diagnostics of the program (`textDocument/diagnostic`), asks for the code actions of the range with those of them that are in it, lists the actions by number, and does the one whose number is typed; <kbd>Esc</kbd> does none. It runs `rdcore.rename` itself: it asks the platform what the name is (`textDocument/prepareRename`), asks for the new name with that one, and renames (`textDocument/rename`) unless <kbd>Esc</kbd> cancels. An edit is made to the program as a whole or not at all: not when it is of a version the program no longer is, and not when it edits a document the shell does not hold. A program that is no document - in a shell attached to a workspace, a program that was neither loaded nor saved - is to be saved first, which `DUCKY` says.

### `textDocument/typeDefinition` and `textDocument/implementation`

The language server answers `textDocument/typeDefinition` (**LSP 3.17** §Goto Type Definition Request) with where the type of the symbol named at the
position is defined: the class, `Type` or `Enum` a variable, a constant, a parameter or a function is declared as - of an array, its items' type. A name that
names a type leads to the type. An intrinsic type, or a type of a library, has no place in the workspace, and the answer is empty.

It answers `textDocument/implementation` (§Goto Implementation Request) with what implements the symbol (**MS-VBAL §5.2.4.2**): for a class, each class of
the workspace that implements it, at its `Implements` directive; for a member of a class, the procedure of each of those classes that implements it -
`IShape_Area` for `Area` of `IShape`, each accessor of a property. A class that no class implements, or a symbol that is neither a class nor a member of one,
has no implementation. Both are answered as `textDocument/definition` is: links, or locations for a client that does not read them.

### `workspace/configuration` and `workspace/didChangeConfiguration`

The settings a user changes while the platform runs travel as **LSP 3.17** has them (§Configuration Request, §DidChangeConfiguration Notification): the client
owns its user's settings, and a server asks for them.

|Message|Direction|Does|
|---|---|---|
|`workspace/configuration`|Server to client|Asks for the section `rdcore`: the live settings, keyed as the platform's configuration is (`rdcore.Server.Verbose`). A server asks when it is initialized, and whenever its client says they changed.|
|`workspace/didChangeConfiguration`|Client to server|Says the live settings changed. Its `settings` are not read: the server asks for the section.|
|`workspace/diagnostic/refresh`|Server to client|Asks a client that pulls its diagnostics to pull them again, once the settings changed. Sent to a client whose `workspace.diagnostics.refreshSupport` is `true`.|

Every client of the platform says it answers `workspace/configuration` (`workspace.configuration`), and answers with what its component hands down
([`LiveSettingsProtocol`](../api/RDCore.SDK.Server.Configuration.LiveSettingsProtocol.html)): the interactive shell, with the live settings of its user's
settings; the language server, as the client of its components, with what its own client said. Every server asks, and sets what it is told on its settings
([**RD-VBAL §2.3.3** Configuration](rd-vbal.2.3.3.configuration.md#live-settings)). The language server says each change to the components it is the client of.

### `textDocument/selectionRange`

The language server answers `textDocument/selectionRange` (**LSP 3.17** §Selection Range Request) with what a selection at each position grows to, step by
step: each node of the syntax tree written around the position, from a name through its expression, its statement, the block of a structured statement and
the procedure, to everything the module declares ([`SyntaxSelection`](../api/RDCore.SDK.Model.AST.Abstract.SyntaxSelection.html)). Each step is in the next.
A position that no node is written around is answered with the empty range at it. For a `.rdc` program, ranges are in its lines.

### `workspace/didChangeWatchedFiles`

The language server reads the project file (`.rdproj`) when it initializes. A client that rewrites it while the platform runs says so as **LSP 3.17** has it
(§DidChangeWatchedFiles Notification): the client owns the file system, and the server watches nothing itself.

|Message|Direction|Does|
|---|---|---|
|`client/registerCapability`|Server to client|Asks a client that registers capabilities dynamically (`workspace.didChangeWatchedFiles.dynamicRegistration`) to watch `**/.rdproj`, created or changed. A client that does not is not asked.|
|`workspace/didChangeWatchedFiles`|Client to server|Says the workspace's `.rdproj` was created or changed. The server serves it whether or not it asked for it: a client that registers nothing sends it after it writes the project file. A change to another folder's `.rdproj`, and a deletion, are left alone.|
|`rdcore/host/project/reload`|Language server to environment host|Asks the host to compose its runtime session again from the project file as it is now ([`ProjectReload`](../api/RDCore.SDK.Client.ProjectReload.html)). A class module's symbol is the project's, so a class the project gained is a type only once the session is composed again. A program that is running or waits keeps the session it runs in.|
|`workspace/diagnostic/refresh`|Server to client|Asks a client that pulls its diagnostics to pull them again, once the workspace is defined again. Sent to a client whose `workspace.diagnostics.refreshSupport` is `true`.|
|`window/showMessage`|Server to client|Says why the workspace is as it was (the project file could not be read), or that a running program keeps its modules.|

On the notification, the language server (`WorkspaceReloadService`):

1. reads the project file again; one that cannot be read leaves the workspace as it was;
2. loads and parses each file it lists that was not loaded, and takes out each one it no longer lists (a document the client has open stays: its text is the client's);
3. asks the environment host to compose its session again;
4. defines the whole workspace again in its three phases - its types declared, its declarations bound, its code loaded - each module replacing what the host held of it. When the host kept its session, a module the project lost is discarded from it (`rdcore/host/discard`) first;
5. asks the client to pull its diagnostics again.

### `window/workDoneProgress`

The language server reports the progress of its work to a client that shows it (`window.workDoneProgress`; **LSP 3.17** §Work Done Progress):

- **Work for a request:** reported to the token the request gave (`workDoneToken`). `rdcore/session/analyze` and `textDocument/diagnostic` report the parse, the semantic model, and each diagnostics provider as it answers.
- **Work the server does of its own:** reported to a token it creates (`window/workDoneProgress/create`). As the workspace comes up, it reports:
  - parsing the workspace, a module at a time;
  - composing the project: each module defined, then its code loaded;
  - the first analysis of every document.

Progress is for work that takes longer than an answer does. Work that is done within the threshold ([`WorkProgressReporter.DefaultThreshold`](../api/RDCore.SDK.Server.Progress.WorkProgressReporter.html), 300 ms) is reported nothing, and no token is created for it. Work that is still going on then begins, with the last step it reported. Once begun, it always ends, whatever comes of it; work that fails ends with its error's message. Its percentage only goes up.

The code that does the work reports to an `IProgress<WorkProgressStep>` ([`IWorkProgress`](../api/RDCore.SDK.Server.Progress.IWorkProgress.html)), and knows nothing of LSP.

A client tracks what it is reported with [`WorkProgressTracker`](../api/RDCore.SDK.Client.Progress.WorkProgressTracker.html). It shows the work for its requests (`TrackWorkDone`) and the work the server begins (`WorkProgressOrigin.Server`). The interactive shell draws each piece of work as a bar, as PowerShell's `Write-Progress` does:

- while a line is edited at the prompt, under it;
- while a command runs, the work for that command, on the line its output starts on, cleared before the output is written.

The work the server begins of its own is not drawn among a command's output.

### Call hierarchy and type hierarchy

The language server answers `textDocument/prepareCallHierarchy`, `callHierarchy/incomingCalls` and `callHierarchy/outgoingCalls` (**LSP 3.17** §Call
Hierarchy) ([`SymbolHierarchy`](../api/RDCore.SDK.Semantics.Refactoring.SymbolHierarchy.html)):

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
2. The language server asks its client (`rdcore/session/interaction/msgBox`, `.../inputBox`) when the client expects [`SessionInteraction`](../api/RDCore.SDK.Client.SessionInteraction.html). Otherwise it answers that nobody was asked (`Handled = false`), and the environment host shows the dialog of the platform it runs on: a Windows dialog on Windows, and none elsewhere (the dialog answers as though it had been confirmed at once).

The request carries the lines the program completed before it opened the dialog (`Output`), so that the client shows them first; they are not repeated in the answer to `rdcore/session/execute`. An `InputBox` that was cancelled answers `Cancelled = true`, which the program is told with `vbNullString`.

The interactive shell draws a dialog as a box with a border, centred on the console, with its title in the top edge and its message wrapped inside. A `MsgBox` ends with a row of buttons, chosen with the arrow keys, <kbd>Tab</kbd>, <kbd>Enter</kbd>, or a button's key. Its icon has a column of its own, and the lines of the message hang under it. An `InputBox` ends with a line to type on, which starts with the default text selected and scrolls inside the box. <kbd>Esc</kbd> closes either at window level.

---
> ⏮️ [**RD-VBAL §2.0.1** Supported Languages](rd-vbal.2.0.1.supported-languages.md) | ⏭️ [**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.md)
