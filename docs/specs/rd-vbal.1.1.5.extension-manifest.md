# 1.1.5 Extension Manifest

RDCore extensions are *discovered* by the *environment host* during the *composition* of the host environment. An extension is discovered from its *extension manifest*, `extension.manifest.json`.

The extension manifest is a small text file in a human-readable JSON serialized format. It describes the extension to the platform.

By default, the environment host may restrict capabilities requested by **unsigned or untrusted extensions**. Users may override these default restrictions through configuration or development modes.

> [!IMPORTANT]
> Extension packages **must** include a **signed manifest** that binds metadata to the distributed artifacts (the *extension server* executable).

## 1.1.5.1 Schema

```json
{
    "Name": "string",
    "Title": "string",
    "Version": "version-string",
    "Publisher": "string",
    "PublisherWebUrl": "url-string",
    "Description": "string",
    "Signature": "string",
    "Capabilities": [ { "Name": "string", "IsSupported": true } ],
    "Settings": [ { "Key": "string", "Kind": 0, "Choices": [], "Minimum": null, "Maximum": null, "Reach": 0, "IsTrustSensitive": false } ],
    "SettingDescriptions": { "en": { "key": "string" }, "fr": { "key": "string" } }
}
```

|Attribute|Description|
|---|---|
|`Name`|The file name of the extension executable (.exe). The executable is located in the same folder as the manifest file.|
|`Title`|The *friendly name*, or *title*, of the extension. It **must** match the name of the folder the manifest is located in.|
|`Version`|A *semantic version* string minimally identifying the **Major.Minor.Build** version of the extension. It **must** match the assembly file version of the extension executable (.exe) file.|
|`Publisher`|The name of the publisher (copyright holder) of the extension.|
|`PublisherWebUrl`|A reasonably short website URL provided by the publisher.|
|`Description`|A short description of the extension.|
|`Signature`|The Base64-encoded `SHA512` file-hash signature of the extension executable (.exe) file. It **must** be identical to the file hash of the discovered extension executable (.exe).|
|`Capabilities`|The platform capabilities the extension advertises, recorded by `rdc.exe describe-ext`. Each entry names a capability (`Name`) and whether it is supported (`IsSupported`, `true` by default). The host ignores an entry whose `IsSupported` is `false`.|
|`Settings`|The settings the extension is configured with ([`SettingDescriptor`](../api/RDCore.SDK.Server.Configuration.SettingDescriptor.html)), recorded by `rdc.exe describe-ext` from the extension's [`ExtensionSettingsAttribute`](../api/RDCore.SDK.Extensibility.ExtensionSettingsAttribute.html). A client offers them to a user without starting the extension ([**RD-VBAL §2.3.3** Configuration](rd-vbal.2.3.3.configuration.md)). Empty for an extension that has none.|
|`SettingDescriptions`|The description of each setting, by its key, in each language of the platform, by its two-letter name. A description in a language the platform has none in is the first language's (`en`).|

An extension declares a capability so that `rdc.exe describe-ext` records it in the extension's manifest:

|Capability|Purpose|See|
|---|---|---|
|[CliCommand](../api/RDCore.SDK.Client.CliCommand.html)|The extension contributes `rdc.exe` command-mode verbs.|[**RD-VBAL §2.0.2** Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md)|
|[DiagnoseDocument](../api/RDCore.SDK.Client.DiagnoseDocument.html)|The extension is a *diagnostics provider*.|[**RD-VBAL §2.6.5** Diagnostics Pipeline](rd-vbal.2.6.5.diagnostics-pipeline.md)|

## 1.1.5.2 Validation

An extension manifest discovered under the platform's `./extensions` folder must be validated by the host before the extension can be authorized to execute. Validation occurs in layers, and helps ensure that only *trusted extensions* are allowed to run.

|Condition|Validation flag|
|---|---|
|The *platform extension configuration* does not explicitly list the extension's `Title` as `Allowed`.|`NotAllowed`|
|The platform extension configuration explicitly lists the extension's `Title` as `Blocked`.|`Blocked`|
|The `Title` in the extension manifest mismatches the folder location the manifest was discovered in.|`LocationMismatch`|
|The extension executable (.exe) named in the manifest is not found in the same folder as the manifest file.|`FileNotFound`|
|The `SHA512` file-hash signature in the manifest mismatches the `SHA512` file-hash signature of the extension executable (.exe).|`SignatureMismatch`|

The validation flags are documented by [ExtensionValidationFlags](../api/RDCore.SDK.Extensibility.ExtensionValidationFlags.html).

The environment host may prompt to allow discovered extensions that do not appear in the platform extension configuration. Whether the host prompts to allow, or automatically blocks, non-configured extensions depends on the host implementation.

If the validation result is `NoFlags`, the host may:

1. configure a *client host* for the extension server;
2. start the extension server's executable process;
3. initiate the LSP connection handshake and capabilities exchange with the extension server (see [**RD-VBAL §2.0.2** Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md)).

> [!TIP]
> To facilitate building *platform extensions*, the `rdc.exe` host may be configured to allow unsigned extension builds using the `--unsafe-dev-mode` command-line flag.

---
> ⏮️ [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md) | ⏭️ [**RD-VBAL §1.1.6** Capabilities Provider](rd-vbal.1.1.6.capabilities-provider.md)
