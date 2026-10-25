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
|`Live`|When it changes, in the components that are running.|`Server.Verbose`, `Server.TraceLevel`, `Diagnostics.*`|
|`NextStart`|When the component that reads it starts, or the next session does.|`Environment.*`, `Workspace.*`, `CLI.*`, `Platform.Extensions.Allowed`|
|`Deployment`|Never through the platform: only the deployed configuration sets it.|`ServerApp.Component`, `Platform.Transport.*`, `Server.ClientProcessId`|

A `Deployment` setting is read before there is a client to ask: which component a process is, and how it connects. A user does not set one, because a bad value
would keep the platform from starting, and with it the client that could put it back.

> [!NOTE]
> **Not implemented.** No setting takes effect while the platform runs: every component reads its settings when it starts, `Live` ones included. Live changes
> come with `workspace/configuration` and `workspace/didChangeConfiguration`.

A setting that decides what a program may do to the machine it runs on is _trust-sensitive_
([`TrustSensitiveSettingAttribute`](../api/RDCore.SDK.Server.Configuration.TrustSensitiveSettingAttribute.html)): `Environment.AllowDllImports`,
`Environment.AllowAutomation`, `Environment.LegacyRegistrySettings`, and `Platform.Extensions.*`. Setting one says so, with a caution callout.

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

## `CONFIG`

`CONFIG` (`CONFIGURE`) is a verb of the interactive shell. `rdc config` does the same in command mode, without starting the platform; it exits with `1` when it
writes an error.

|Written|Does|
|---|---|
|`CONFIG`|Lists what the user set.|
|`CONFIG Server.Verbose`|Shows the setting: what the user set it to, what it does, and what it can be.|
|`CONFIG Server`|Lists the settings of a section, with what the user set them to.|
|`CONFIG Server.Verbose False`|Sets the setting for the user. A value it cannot be, a `Deployment` setting, and a key that names no setting are errors, and nothing is written.|
|`CONFIG RESET Server.Verbose`|Removes what the user set of a setting, or of every setting of a section (`CONFIG RESET Server`).|
|`CONFIG RESET`|Removes every setting the user set, and the file with them, whether or not it can be read.|

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
|[`AppHost.Settings`](../api/RDCore.SDK.Server.AppHost-1.html)|The settings of a component: the platform's, combined with the component's own.|
|[`ExtensionSettingsAttribute`](../api/RDCore.SDK.Extensibility.ExtensionSettingsAttribute.html)|The settings an extension is configured with, written into its manifest.|

---
> ⏮️ [**RD-VBAL §2.3.2** Mode / State](rd-vbal.2.3.2.mode-state.md) | ⏭️ [**RD-VBAL §2.4** Static Types](rd-vbal.2.4.static-types.md)
