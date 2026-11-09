# 2.1 Implicit Storage

MS-VBAL explicitly presumes an _implementation-dependent_ storage mechanism outside the scope of its own specification. **RD-VBAL explicitly specifies these mechanisms**. This decouples the _language semantics_ from _implementation-dependent storage_.

The _environment host_ is responsible for configuring the implicit storage of the host VBA environment, and for loading any _application settings_ and additional _workspace resources_ into the runtime environment (see [**RD-VBAL §2.3.1** Composition Root](rd-vbal.2.3.1.composition-root.md)).

## 2.1.1 Application Settings

MS-VBAL addresses legitimate **application configuration** concerns through an API exposed in the _standard library_, whose storage it leaves _implementation-defined_:

|Member|Specification|
|---|---|
|`GetAllSettings`|[**MS-VBAL §6.1.2.8.1.7** GetAllSettings](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a2b5707d-9a11-4cca-9920-3a2b7e7ff518)|
|`GetSetting`|[**MS-VBAL §6.1.2.8.1.10** GetSetting](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7e55c0a6-2fc2-425c-9f4c-be88cff4f629)|
|`DeleteSetting`|[**MS-VBAL §6.1.2.8.2.3** DeleteSetting](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/295c31a2-a9f6-4326-b69e-3d6f5549abec)|
|`SaveSetting`|[**MS-VBAL §6.1.2.8.2.4** SaveSetting](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/98261176-760c-4257-8476-49669bd36053)|

MS-VBA keeps these settings in the _Windows Registry_, under `HKEY_CURRENT_USER\Software\VB and VBA Program Settings`. **RD-VBA keeps them in the _workspace application settings_ by default**: application `appName`'s key `key` of section `section` is the setting `ProgramSettings:appName:section:key` of the workspace's `appsettings.json`.

> [!IMPORTANT]
> The `LegacyRegistrySettings` environment setting (`appsettings.json` of the _environment host_, `Configuration:Environment`) sets the storage of these members to the _Windows Registry_, as MS-VBA has it. **Only Windows has a registry**: elsewhere the setting is ignored, and the workspace application settings are used either way.

RD-VBA also adds members that manage the workspace application settings explicitly, whatever the legacy members use:

|Member|Specification|SDK member|
|---|---|---|
|`GetAllJsonSettings`|[**RD-VBAL §6.1.2.8.1.7.1** GetAllJsonSettings](rd-vbal.6.1.2.8.interaction.md#61281-public-functions)|`IStdInteractionModule.GetAllJsonSettings`|
|`GetJsonSetting`|[**RD-VBAL §6.1.2.8.1.10.1** GetJsonSetting](rd-vbal.6.1.2.8.interaction.md#61281-public-functions)|`IStdInteractionModule.GetJsonSetting`|
|`SaveJsonSetting`|[**RD-VBAL §6.1.2.8.2.4.1** SaveJsonSetting](rd-vbal.6.1.2.8.interaction.md#61282-public-subroutines)|`IStdInteractionModule.SaveJsonSetting`|

There is no `DeleteJsonSetting`: a configuration file is the workspace's to edit, and `DeleteSetting` covers the settings a program keeps for itself.

### 2.1.1.1 Workspace Application Settings

|Rule|Description|
|---|---|
|Location|A _workspace_ may include one or more `.json` configuration file(s), at its root or under any of its subfolders. A file outside the workspace, or that is not a `.json` file, is refused (error 5).|
|File name|`appsettings.json` at the workspace root is the default. The `config` argument of the RD-VBA members names another file, relative to the workspace root.|
|Reading|A setting is read through a .NET managed `IConfiguration` built over the file: a path is its sections separated by `:`, compared without regard to case; an array's elements are its sections `0`, `1`, ...; a number or a Boolean reads as its text. A file that is not there has no settings; a file that is not JSON is error 321.|
|Writing|A setting is written as a JSON string under the sections of its path, each created when it is not there. A file that is written to is written whole, indented, and **without its comments**.|
|Binding|The _environment host_ (`rdc.exe`) binds the workspace's settings as the runtime session is _composed_.|

An _analysis_ reaches no settings: what it saves is not kept, and nothing is read.

The `ErlLineNumbering` and `ErlAfterErrClear` environment settings are bound from `appsettings.json`, with the rest of the runtime profile (see [**RD-VBAL §6.1.2.7.1.14** Erl](rd-vbal.6.1.2.7.information.md#6127114-erl)).
