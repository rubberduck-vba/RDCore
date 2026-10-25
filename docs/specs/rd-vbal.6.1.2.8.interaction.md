# 6.1.2.8 Interaction

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.8** Interaction](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9a7e0f79-3164-41dd-beda-bcc02dc79d1f).

The `Interaction` module is represented in the SDK by the interface
[IStdInteractionModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdInteractionModule.html).

The application settings members (`GetAllSettings`, `GetSetting`, `SaveSetting`, `DeleteSetting`, and the RD-VBA variants
`GetAllJsonSettings`, `GetJsonSetting` and `SaveJsonSetting`) are described with the implicit storage they use, in
[**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.md). The legacy members use the workspace's `appsettings.json`,
unless the environment asks for the Windows Registry (`LegacyRegistrySettings`, Windows only).

What the module reaches outside the platform is the environment host's to provide (`ExternalWorld`):

|Concern|Windows|Elsewhere (e.g. a build machine)|Interactive shell (`rdc.exe`)|An analysis|
|---|---|---|---|---|
|`MsgBox`, `InputBox`|The dialogs of Windows.|None: a dialog answers as though it were confirmed at once (default button, default text).|Asked at the shell's console, on any platform.|None.|
|`DoEvents`|Processes the messages of the thread, and the events of the objects of a library that wait for the session.|Does nothing.|As the platform.|Does nothing.|
|`Beep`, `AppActivate`, `SendKeys`, `Shell`|Windows' own.|Do nothing; `Shell` answers `0`.|As the platform.|Do nothing.|
|Settings|`appsettings.json`, or the registry when asked for.|`appsettings.json`.|As the platform.|Nothing is read, and nothing kept.|

<kbd>Esc</kbd> closes a dialog at window level. A `MsgBox` closed that way answers `vbCancel` (`vbOK` when OK is its only button); one that cannot be closed without choosing (`vbYesNo`, `vbAbortRetryIgnore`) ignores <kbd>Esc</kbd>, as on Windows. An `InputBox` closed that way answers `vbNullString`, which is how a caller tells a cancelled dialog (`StrPtr(answer) = 0`) from one confirmed empty (`""`).


## 6.1.2.8.1 Public Functions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.8.1** Public Functions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5253bced-81cd-4a45-a4da-0270224bf0e5).

`GetAllJsonSettings` and `GetJsonSetting` are RD-VBA variants of `GetAllSettings` and `GetSetting`, and take a child
number of the member they vary. `MacScript`, a hidden member of the module, resolves and raises a run-time error: RD-VBA has no AppleScript to run.

|§|Member|Notes|
|---|---|---|
|6.1.2.8.1.1|[CallByName](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b195f168-fb71-428d-b12d-1ab490ba64a4)|Late-bound, on the default interface of the object's class; on a late-bound object (`CreateObject`), by the name. Arguments are passed by value. 🚧 A public variable can be read (`VbGet`), not assigned.|
|6.1.2.8.1.2|[Choose](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2f178971-8a8b-4d07-a21e-d0fa82b9db7c)|An index that is not a whole number is rounded as a `Single` is Let-coerced to a `Long` (banker's rounding).|
|6.1.2.8.1.3|[Command](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/03942db8-2ce2-46f5-b7dc-d32779b89101)|✅ A `String` (in a `Variant` for `Command`): the arguments the host says the program was started with, which are none unless it says otherwise, as in a program an Office application runs.|
|6.1.2.8.1.4|[CreateObject](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a2040e64-6724-4bf6-a496-7ef01ec9af31)|✅ A class the platform implements itself is found first, on any platform (`VBScript.RegExp`, [**RD-VBAL §6.2**](rd-vbal.6.2.vbscript-regexp.md)); then the automation servers of the machine (Windows only; elsewhere error `429`). The programmatic identifier is not case-sensitive. An object of a class that a referenced library declares is an object of that class; any other is late-bound: its members are reached by name when they are called, and `TypeName` is the name of its class (`Dictionary`). Errors as MS-VBA: `429` for a class nothing can create (an empty or dot-less name included), the server's own error number for a machine that is not there (`-2147023174`), `438` for a member the object does not have. 🚧 A late-bound member's arguments are passed by value. An identifier written as a literal or a constant tells tooling the class of the object, from the description of its library, referenced or not; that is never a compile error, and the object stays late-bound. Of a class a referenced library declares, [`RDC00311`](../diagnostics/rdc00311.md) suggests `New`.|
|6.1.2.8.1.5|[DoEvents](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/83c90f91-96e7-421e-8faf-a07b1c0cbb68)||
|6.1.2.8.1.6|[Environ / Environ$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/efa62d49-87ea-4b55-949d-69a7fc7e08bb)|Names are found without regard to case; `%NAME%` names the variable `NAME`. On Windows the `%NAME%` macros of a value are expanded. `Environ(n)` numbers the variables in order of their names.|
|6.1.2.8.1.7|[GetAllSettings](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a2b5707d-9a11-4cca-9920-3a2b7e7ff518)|Backed by the _workspace application settings_, unless the environment asks for the Windows Registry. See [**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.md).|
|6.1.2.8.1.7.1|🧩 `GetAllJsonSettings`|RD-VBA variant of `GetAllSettings` over _workspace application settings_. See [**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.md).|
|6.1.2.8.1.8|[GetAttr](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f4c6a91b-d66d-4eb7-878d-24974d8e80ac)|✅ Through the session's file channels, as the FileSystem module ([**RD-VBAL §6.1.2.5**](rd-vbal.6.1.2.5.filesystem.md)). Error `53` for what does not exist, `52` for a pattern.|
|6.1.2.8.1.9|[GetObject](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/74281353-20cb-4a12-8deb-47839d290132)|✅ Windows only, as `CreateObject`. `GetObject(, "Class")` is the running object of the class, or error `429`; `GetObject("", "Class")` is a new one. A path is a file, opened by the class given or by the one the file names, or a moniker (`GetObject("winmgmts:")`); a file that cannot be opened is error `432`. Given neither a path nor a class, error `13`; an empty path and no class, error `5`.|
|6.1.2.8.1.10|[GetSetting](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7e55c0a6-2fc2-425c-9f4c-be88cff4f629)|Backed by the _workspace application settings_, unless the environment asks for the Windows Registry. See [**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.md).|
|6.1.2.8.1.10.1|🧩 `GetJsonSetting`|RD-VBA variant of `GetSetting` over _workspace application settings_. See [**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.md).|
|6.1.2.8.1.11|[IIf](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3593c073-c770-4060-8416-aaf7545ea5fe)|Both parts are evaluated. A `Null` expression returns the false part.|
|6.1.2.8.1.12|[InputBox](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/64b763ff-9f5f-43cd-b247-c75557e51989)||
|6.1.2.8.1.13|[MsgBox](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ba0375ee-936a-482c-8a9f-e9fa4515d191)||
|6.1.2.8.1.14|[Partition](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/44e5d3f2-6126-4a0e-b047-de2d546995c4)|✅ As MS-VBA: each side of the colon is padded to the width of the wider of `Start - 1` and `Stop + 1`, which the specification's "characters in Stop, plus one" is only for some ranges (`Partition(195, 20, 199, 10)` is `"190:199"`). Each part is rounded to a whole number half to even; one too large for a `Long` is past the range. A `Null` part is a `Null` result, before anything else is checked; a `Start` below 0, a `Stop` not after `Start`, or an `Interval` below 1 is error `5`.|
|6.1.2.8.1.15|[Shell](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/70501cdb-0322-4e1f-915f-819dced09534)||
|6.1.2.8.1.16|[Switch](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7136c7fd-223c-4870-b2ba-9fe2cc9a2baa)|A `Null` expression is not `True`. No `True` expression returns `Null`; unpaired arguments are error 5.|


## 6.1.2.8.2 Public Subroutines

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.8.2** Public Subroutines](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9af0f4b6-9d30-4c6c-a8f1-fb32903f8142).

|§|Member|Notes|
|---|---|---|
|6.1.2.8.2.1|[AppActivate](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6e739dc6-7eea-4f95-ba0a-ef632e2608ba)||
|6.1.2.8.2.2|[Beep](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f154435f-5eb6-4300-a1c7-61b925f0cf0a)||
|6.1.2.8.2.3|[DeleteSetting](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/295c31a2-a9f6-4326-b69e-3d6f5549abec)|Given no section, deletes every section of the application. A setting that does not exist is error 5.|
|6.1.2.8.2.4|[SaveSetting](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/98261176-760c-4257-8476-49669bd36053)||
|6.1.2.8.2.4.1|🧩 `SaveJsonSetting`|RD-VBA variant of `SaveSetting` over _workspace application settings_. See [**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.md).|
|6.1.2.8.2.5|[SendKeys](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/df1a061c-0f22-4459-93f7-a8ada7148bb7)||

---
> ⏮️ [**RD-VBAL §6.1.2.7** Information](rd-vbal.6.1.2.7.information.md) | ⏭️ [**RD-VBAL §6.1.2.9** KeyCodeConstants](rd-vbal.6.1.2.9.keycodeconstants.md)
