# 6.3 Visual Basic 6.0 App Object

> [!NOTE]
> This section is RD-VBAL's own. MS-VBAL has no section for the libraries of Visual Basic 6.0.

A Visual Basic 6.0 project names `App`, of the `VB` library's hidden `Global` module, without a reference to it. RD-VBA implements the object itself:
`App` is one object of the session, which says where the program runs and keeps what the program assigns to it until the session is composed again.
An RD-VBA project has no `App`.

## Where the program runs

|Member|Under `rdc run`|In a program `rdc build` built|
|---|---|---|
|`Path`|The folder of the workspace project's `.rdproj`.|The folder of the program's executable.|
|`EXEName`|The project's name.|The executable's name, without its extension.|
|`PrevInstance`|`False`.|`True` when another instance of the same executable runs, on every operating system.|
|`UnattendedApp`|`True` for a program run with `--unattended`.|The same.|

`Path` ends with no separator, unless it is the root of a drive or of the file system.

## Values of a new project

A `.rdproj` holds no version information, so `App` gives what a new Visual Basic 6.0 project has:

|Member|Value|
|---|---|
|`Title`|The project's name, until the program assigns another.|
|`Major`, `Minor`, `Revision`|`1`, `0`, `0`.|
|`Comments`, `CompanyName`, `FileDescription`, `LegalCopyright`, `LegalTrademarks`, `ProductName`|`""`.|
|`StartMode`|`vbSModeStandalone` (`0`). RD-VBA builds no ActiveX component.|
|`NonModalAllowed`|`True`.|
|`RetainedProject`|`False`.|

## Properties the program assigns

These keep what the program assigns, and change nothing the platform does: no automation call waits for a busy server or shows its dialog, and no
taskbar button is hidden.

|Member|Initial value|
|---|---|
|`TaskVisible`|`True`.|
|`OleServerBusyTimeout`, `OleRequestPendingTimeout`|`10000` and `5000` (milliseconds).|
|`OleServerBusyRaiseError`|`False`.|
|`OleServerBusyMsgTitle`, `OleRequestPendingMsgTitle`|`App.Title`, until one is assigned.|
|`OleServerBusyMsgText`, `OleRequestPendingMsgText`|`""`.|

## The process

|Member|Value|
|---|---|
|`ThreadID`|The operating system's identifier of the thread the program runs on, on every operating system. Error `6` when it does not fit a `Long`.|
|`hInstance`|Error `5` on an operating system other than Windows. On Windows, error `6` in a 64-bit process, whose module handle does not fit a `Long`. The handle is never cut short.|

## Versions of the platform

RD-VBA addition. These read-only `String` properties say which platform runs the program, such as for an About box.

|Member|Value|
|---|---|
|`PlatformVersion`|RDCore's version, such as `0.1.6-alpha+c90bd73…`.|
|`ComponentVersion(Name)`|The version of the component `Name` names, in any case: `RDCore`, `RDCore.SDK` or `RDCore.Forms`. `""` for another name, or for a component that is not installed, with no error.|

A version is the one its component's build stamped into its assembly, read from [`PlatformComponents`](../api/RDCore.SDK.Platform.PlatformComponents.html).

> [!NOTE]
> **Not implemented.** `HelpFile`, `LogMode`, `LogPath`, `StartLogging` and `LogEvent` raise error `438`.

## Implementation

|Type or member|Role|
|---|---|
|`AppCallProvider`|Answers `App` and the members of its object.|
|`SessionApplication`|What `App` says of the session's program, and what the program assigns to it.|
