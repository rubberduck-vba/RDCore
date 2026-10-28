# 2.3.3 Configuration

Every component of the platform is configured from the `Configuration` section of its `appsettings.json`, deployed with it. A user changes a setting for
themselves with the shell's `CONFIG` verb, or with `rdc config` in command mode. What a user sets is kept apart from what the platform was deployed with, and a
deployed `appsettings.json` is never written to.

## Settings

The settings of the platform are read off its settings types ([`SettingsSchema`](../api/RDCore.SDK.Server.Configuration.SettingsSchema.html)): the platform's
own ([`SdkAppOptions`](../api/RDCore.SDK.Server.Configuration.SdkAppOptions.html)), the console's (`CLI`), and those of each extension, which its manifest carries
([**RD-VBAL §1.1.5** Extension Manifest](rd-vbal.1.1.5.extension-manifest.md)). A setting is named by its key: its sections and its name, separated by dots
(`Server.Verbose`), compared without regard to case.

|Value|Written as|
|---|---|
|Boolean|`True` or `False`.|
|Whole number|Digits, between the least and the greatest value of the setting.|
|Choice|One of the names of the setting's values (`Server.TraceLevel Warning`).|
|Text|Any text, to the end of the line.|
|List of texts|Texts separated by commas (`Platform.Extensions.Allowed RDCore.Diagnostics, Other`).|

When a change of a setting takes effect is part of the setting ([`SettingAttribute`](../api/RDCore.SDK.Server.Configuration.SettingAttribute.html)):

|Reach|Takes effect|Examples|
|---|---|---|
|`Live`|When it changes, in the components that are running.|`Server.Verbose`, `Server.TraceLevel`, `Formatting.*`, `Diagnostics.*`|
|`NextStart`|When the component that reads it starts, or the next session does.|`Environment.*`, `Workspace.*`, `CLI.*`, `Platform.Extensions.Allowed`|
|`Deployment`|Never through the platform: only the deployed configuration sets it.|`ServerApp.Component`, `Platform.Transport.*`, `Server.ClientProcessId`|

A `Deployment` setting is read before there is a client to ask: which component a process is, and how it connects. A user does not set one, because a bad value
would keep the platform from starting, and with it the client that could put it back.

A setting that decides what a program may do to the machine it runs on is _trust-sensitive_
([`TrustSensitiveSettingAttribute`](../api/RDCore.SDK.Server.Configuration.TrustSensitiveSettingAttribute.html)): `Environment.AllowDllImports`,
`Environment.AllowAutomation`, `Environment.LegacyRegistrySettings`, and `Platform.Extensions.*`. Setting one says so, with a caution callout.

### Build Configuration

Two settings decide how a program is run, whatever its code says:

|Setting|Values|
|---|---|
|`Environment.Configuration`|`Debug` (the default) or `Release`. A release build has no debugger: a `Stop` statement ends the program as `End` does, as in a compiled VB6 executable ([**RD-VBAL §5.4.2.11** Stop Statement](rd-vbal.5.4.2.11.stop-statement.md)). It keeps every `Debug.Print` and `Debug.Assert` call unless `Environment.ReleaseDropsDebugStatements` says otherwise. It is the default of the built-in `DEBUG` conditional-compilation constant (`True` in a debug build, `False` in a release one), and what a release build does follows `DEBUG`: a project's `#Const DEBUG`, or a command-line define, still decides.|
|`Environment.ReleaseDropsDebugStatements`|`false` (the default) or `true`. When `true`, a release build leaves out every `Debug.Print` and `Debug.Assert` call, its arguments unevaluated: no instruction is left behind. When `false`, a release build runs them as a debug build does. A debug build keeps them either way, and a release build's `Stop` ends the program whatever this says.|
|`Environment.ExecutionEngine`|`Interpreter` (the default) or `Compiled`: each procedure compiled to IL on its first call when every instruction in it can be, and interpreted otherwise ([**RD-VBAL §3.5.6** Compiled Execution](rd-vbal.3.5.6.compiled-execution.md)).|

The engine is independent of the other two: a release build lowers the same statements the same way in either engine.

## User Settings

What a user sets is kept in `user.settings.json`, in the user's folder of the platform: `RDCore` in the user's roaming application data (`%APPDATA%` on Windows,
`~/.config` elsewhere; `RDCORE_USER_DATA` names another folder). The file has the shape of an `appsettings.json`, and holds only what the user set
([`UserSettingsStore`](../api/RDCore.SDK.Server.Configuration.UserSettingsStore.html)).

Each component reads it after its deployed `appsettings.json`, and before the environment variables and the command line it is started with
([`UserSettingsConfiguration`](../api/RDCore.SDK.Server.Configuration.UserSettingsConfiguration.html)):

|Source|Precedence|
|---|---|
|`appsettings.json`, deployed with the component|Lowest.|
|`user.settings.json`|Takes the place of the deployed value.|
|Environment variables (`Configuration__Server__Verbose`)|Take the place of the user's value.|
|The command line|Highest.|

Of the file, only values the settings can be are read: a value a setting cannot be, a value of a `Deployment` setting, and a file that is not JSON are not read.
A file written by hand never keeps the platform from starting. A setting the component does not know of is read as it is: it is another component's.

A component reads the file when it starts, and not again: that is when a `NextStart` setting takes effect.

## Live Settings

A `Live` setting takes effect while the platform runs. The client says it, as **LSP 3.17** has a client own its user's settings
([**RD-VBAL §2.0.2** Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md#workspaceconfiguration-and-workspacedidchangeconfiguration)):

1. The shell writes the setting to the user's settings, and sends `workspace/didChangeConfiguration`.
2. The language server asks the shell for the section `rdcore` (`workspace/configuration`). The shell answers with the live settings of the user's settings.
3. The language server sets the live settings it has on its own settings, and sends `workspace/didChangeConfiguration` to each component it is the client of:
   the parsing server, the environment host, and each extension.
4. Each component asks the language server, which answers with what the shell said, and sets the live settings it has. A setting it does not have is another
   component's, and is passed on as it is.

A server asks for the section when it is initialized, too. A live setting the client says nothing of takes the value the component started with back. A value
a setting cannot be is not set ([`LiveSettings`](../api/RDCore.SDK.Server.Configuration.LiveSettings.html)).

A live setting is set on the object the component reads its settings off, in place. What reads a setting each time it uses it reads the new value:

|Setting|Takes effect|
|---|---|
|`Server.TraceLevel`|For the next message each component's logs write ([`LiveLogLevel`](../api/RDCore.SDK.Server.Logging.LiveLogLevel.html)).|
|`Server.Verbose`|For the next message the console writes.|
|`Server.WireErrorDetail`|For the next record forwarded to the client.|
|`Diagnostics.*`|For the next module the diagnostics extension analyzes. A client that pulls its diagnostics is asked to pull them again (`workspace/diagnostic/refresh`).|
|`Formatting.*`|For the next line or range the formatter formats; they are read as the formatter arrives ([Formatting](#formatting)).|

`rdc config` writes the user's settings without a running platform to tell: what it sets takes effect when the platform next starts, a live setting included.

## Formatting

The `Formatting` section is what formats and completes code ([`SdkFormattingOptions`](../api/RDCore.SDK.Server.Configuration.SdkFormattingOptions.html)). A
formatting engine is told the options these settings say ([`CodeFormattingOptions`](../api/RDCore.SDK.Formatting.CodeFormattingOptions.html)); the numbers
are kept to the limits of their settings. The indentation is made of spaces, as VBA code is.

The settings take effect with the formatter, which reads them each time it formats: the platform keeps them and hands them to it as it arrives, and until then they
change nothing in a document. What the platform does with them today is keep them, and take the editor's tab size as `IndentSpaces` when the user never set one
([below](#indentation-and-the-editors-tab-size)).

The first twenty-four are Rubberduck's indenter settings, with the names, types, defaults and limits they have there, most of them the Smart Indenter's. A
setting means here what it means there.

|Setting|Value|Default|
|---|---|---|
|`IndentEntireProcedureBody`|Boolean|`True`|
|`IndentEnumTypeAsProcedure`|Boolean|`False`|
|`IndentFirstCommentBlock`|Boolean|`True`|
|`IndentFirstDeclarationBlock`|Boolean|`True`|
|`IgnoreEmptyLinesInFirstBlocks`|Boolean|`False`|
|`AlignCommentsWithCode`|Boolean|`True`|
|`AlignContinuations`|Boolean|`True`|
|`IgnoreOperatorsInContinuations`|Boolean|`True`|
|`IndentCase`|Boolean|`False`|
|`ForceDebugStatementsInColumn1`|Boolean; says it of `ForceDebugPrintInColumn1`, `ForceDebugAssertInColumn1` and `ForceStopInColumn1` all|`False`|
|`ForceDebugPrintInColumn1`, `ForceDebugAssertInColumn1`, `ForceStopInColumn1`|Boolean|`False`|
|`ForceCompilerDirectivesInColumn1`|Boolean|`False`|
|`IndentCompilerDirectives`|Boolean|`True`|
|`AlignDims`|Boolean|`False`|
|`AlignDimColumn`|0 to 100|`15`|
|`EndOfLineCommentStyle`|`Absolute`, `SameGap`, `StandardGap`, `AlignInColumn`|`AlignInColumn`|
|`EndOfLineCommentColumnSpaceAlignment`|0 to 100|`50`|
|`EmptyLineHandlingMethod`|`Ignore`, `Remove`, `Indent`|`Ignore`|
|`IndentSpaces`|0 to 32|`4`|
|`VerticallySpaceProcedures`|Boolean|`True`|
|`LinesBetweenProcedures`|0 to 2|`1`|
|`GroupRelatedProperties`|Boolean|`False`|

RDCore's own:

|Setting|Value|Default|Does|
|---|---|---|---|
|`FormatOnType`|Boolean|`True`|Formats the line just finished, and indents the new one, on Enter.|
|`CompleteBlocks`|Boolean|`True`|On Enter after a block opener with no closer, inserts the closer and leaves the caret in the indented body.|
|`Snippets`|Boolean|`True`|Completion offers snippets of blocks, with placeholders.|
|`SnippetDimPlacement`|`AboveBlock`, `ProcedureTop`|`AboveBlock`|Where the `Dim` a snippet declares goes: right above the block, where it follows the variable's name as it is typed; or at the top of the procedure, where it does not.|
|`PrettyList`|Boolean|`True`|A finished line is listed back from what it compiled to, as MS-VBA lists it, rather than formatted by rules of spacing and casing.|

### Indentation and the editor's tab size

A client owns its configuration (**LSP 3.17** §Configuration Request), so the platform sets nothing of it and tells the editor nothing: the flow is one way, from the
client to the server. A change of the editor's tab size is a change of configuration like any other: the client says so (`workspace/didChangeConfiguration`), and the
server asks (`workspace/configuration`) for the section `rdcore`, which holds `Formatting.IndentSpaces` when the user set it, and for `editor.tabSize`
([`FormattingTabSize`](../api/RDCore.SDK.Formatting.FormattingTabSize.html)).

|The user set `IndentSpaces`|The editor has a tab size|The server has `Formatting.IndentSpaces` =|
|---|---|---|
|No|Yes|The editor's tab size, for as long as the server runs. Nothing is written.|
|No|No|`4`.|
|Yes|Either|`IndentSpaces`.|

A change that is still being followed when a newer one arrives is dropped, and so is one that is being followed when the server shuts down. The formatter indents
with `Formatting.IndentSpaces`, and not with the `tabSize` of the options of a request to format ([**RD-VBAL §2.0.2**](rd-vbal.2.0.2.client-server-capabilities.md#textdocumentformatting-and-textdocumentrangeformatting)).

A client that wants its editor to agree with the setting sets one from the other in its own configuration: the platform's shell has no editor, and a client with an
editor (HexIDE, a VS Code extension) maps `editor.tabSize` for the VBA language to `Formatting.IndentSpaces`, or the other way.

## `CONFIG`

`CONFIG` (`CONFIGURE`) is a verb of the interactive shell. `rdc config` does the same in command mode, without starting the platform; it exits with `1` when it
writes an error.

|Written|Does|
|---|---|
|`CONFIG`|Lists what the user set.|
|`CONFIG Server.Verbose`|Shows the setting: what the user set it to, what it does, and what it can be.|
|`CONFIG Server`|Lists the settings of a section, with what the user set them to.|
|`CONFIG Server.Verbose False`|Sets the setting for the user; a live one takes effect at once ([Live Settings](#live-settings)). A value it cannot be, a `Deployment` setting, and a key that names no setting are errors, and nothing is written.|
|`CONFIG RESET Server.Verbose`|Removes what the user set of a setting, or of every setting of a section (`CONFIG RESET Server`).|
|`CONFIG RESET`|Removes every setting the user set, and the file with them, whether or not it can be read.|
|`CONFIG IMPORT RUBBERDUCK`|Sets the `Formatting` settings to the indenter settings the user had in Rubberduck: its configuration, else the registry key of the Smart Indenter. Windows only; elsewhere there is nothing to import. The language server also offers it once on the first start ([**RD-VBAL §2.0.2** `rdcore/rubberduck-indenter-settings`](rd-vbal.2.0.2.client-server-capabilities.md#rdcorerubberduck-indenter-settings)).|

`CONFIG CLI.Theme light` sets the theme the console starts with, and does not change the current one: `THEME light` changes the theme of the session only.

The shell completes the arguments of `CONFIG` a part at a time: the sections, then a setting of the one written before the last `.`, then a value of that setting
([**RD-VBAL §2.0.2** Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md#textdocumentcompletion-and-rdcoresessioncompletion)).

## Implementation

|Type or member|Role|
|---|---|
|[`SettingsSchema`](../api/RDCore.SDK.Server.Configuration.SettingsSchema.html)|The settings, read off the settings types; parses a value as a setting has it.|
|[`SettingAttribute`](../api/RDCore.SDK.Server.Configuration.SettingAttribute.html), [`TrustSensitiveSettingAttribute`](../api/RDCore.SDK.Server.Configuration.TrustSensitiveSettingAttribute.html)|When a change of a setting takes effect; whether it is trust-sensitive.|
|[`UserSettingsStore`](../api/RDCore.SDK.Server.Configuration.UserSettingsStore.html)|Reads, sets and resets what the user set.|
|[`UserSettingsConfiguration.AddUserSettings`](../api/RDCore.SDK.Server.Configuration.UserSettingsConfiguration.html)|Adds the user's settings to a component's configuration, checked against its settings.|
|[`LiveSettings`](../api/RDCore.SDK.Server.Configuration.LiveSettings.html), [`LiveSettingsTarget`](../api/RDCore.SDK.Server.Configuration.LiveSettingsTarget.html)|Sets the live settings a client said on a component's settings objects; what it hands down.|
|[`LiveSettingsProtocol`](../api/RDCore.SDK.Server.Configuration.LiveSettingsProtocol.html)|The section `rdcore`: what a client answers `workspace/configuration` with, and what a server reads of it.|
|[`AppHost.Settings`](../api/RDCore.SDK.Server.AppHost-1.html)|The settings of a component: the platform's, combined with the component's own.|
|[`ExtensionSettingsAttribute`](../api/RDCore.SDK.Extensibility.ExtensionSettingsAttribute.html)|The settings an extension is configured with, written into its manifest.|

---
> ⏮️ [**RD-VBAL §2.3.2** Mode / State](rd-vbal.2.3.2.mode-state.md) | ⏭️ [**RD-VBAL §2.4** Static Types](rd-vbal.2.4.static-types.md)
