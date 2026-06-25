# 2.1. Implicit Storage

> 💡 MS-VBAL explicitly presumes of an _implementation-dependent_ storage mechanism outside the scope of its own specification. **RD-VBAL explicitly specifies these mechanisms**, decoupling the _language semantics_ from _implementation-dependent storage_.


---
## 2.1.1 Application Settings

MS-VBAL addresses legitimate **application configuration** concerns through a _get-only_ API exposed in the _standard library_:

- MS-VBAL 6.1.2.8.1.7 [GetAllSettings](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdInteractionModule.html#RDCore_SDK_Runtime_Abstract_StdLib_IStdInteractionModule_StdInteraction__GetAllSettings_RDCore_SDK_Model_Values_Intrinsic_VBStringValue_RDCore_SDK_Model_Values_Intrinsic_VBStringValue_)
- MS-VBAL 6.1.2.8.1.10 [GetSetting](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdInteractionModule.html#RDCore_SDK_Runtime_Abstract_StdLib_IStdInteractionModule_StdInteraction__GetSetting_RDCore_SDK_Model_Values_Intrinsic_VBStringValue_RDCore_SDK_Model_Values_Intrinsic_VBStringValue_RDCore_SDK_Model_Values_Intrinsic_VBStringValue_RDCore_SDK_Model_Values_Intrinsic_VBVariantValue_)

RD-VBA keeps backward compatibility by keeping an implementation backed by the _Windows Registry_, but isn't inherently constrained to it - hence these additions managing _workspace application settings_ using a similar API:

- 🧩RD-VBAL 6.1.2.8.1.7.1 [GetJsonSettings](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdInteractionModule.html#RDCore_SDK_Runtime_Abstract_StdLib_IStdInteractionModule_StdInteraction__GetJsonSettings_RDCore_SDK_Model_Values_Intrinsic_VBStringValue_RDCore_SDK_Model_Values_Intrinsic_VBStringValue_)
- 🧩RD-VBAL 6.1.2.8.1.10.1 [GetJsonSetting](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdInteractionModule.html#RDCore_SDK_Runtime_Abstract_StdLib_IStdInteractionModule_StdInteraction__GetJsonSetting_RDCore_SDK_Model_Values_Intrinsic_VBStringValue_RDCore_SDK_Model_Values_Intrinsic_VBStringValue_RDCore_SDK_Model_Values_Intrinsic_VBVariantValue_)

Whether **any** _standard library_ calls implicate actual or simulated _Windows Registry_ reads is entirely **implementation-dependent** and may behave differently on different platforms. This remains entirely compliant with the relevant MS-VBAL sections as specified.

> [!IMPORTANT]
> The _host environment_ **may** expose configuration settings that can set the implicit storage of `GetAllSettings` and `GetSetting` to _workspace application settings_, making these functions work exactly as if they were invoking `GetAllJsonSettings` and `GetJsonSetting`, respectively.


### 2.1.1.1 Workspace Application Settings

The MS-VBAL specified `GetSettings` API would work perfectly fine as-is for this purpose, however distinctly separate functions were introduced in RD-VBAL to maintain backward compatibility without modifying any existing signatures.

As a result, the _legacy_ `GetSettings` API maintains its MS-VBA behavior, and RD-VBA applications can now leverage a new `GetJsonSettings` API that brings application configuration on par with any other managed (.net) configuration scheme.

- A _workspace_ may include one or more `appsettings.json` file(s) at its root, or under any of its subfolders;
- A configuration file may be named differently: "appsettings.json" is just a (configurable) language platform default;

The _application host_ (`rdc.exe`) is responsible for binding the configuration as the application is _composed_, before it starts executing.

> [!NOTE]
> This feature has the full power and flexibility of a .NET managed `IConfigurationBuilder` underneath: future extensions could harmonize configuration settings and environment variables, fully deprecating the corresponding legacy APIs as _obsolete_ (_semantic flags_ can then be issued at usage sites, with _code actions_ to update the workspace source code).


---
> ⏮️ [**RD-VBAL §2.0** Computational Environment](rd-vbal.2.0.computational-environment.html) | ⏭️ [**RD-VBAL §2.2** RDPROJ Structure](rd-vbal.2.2.rdproj-structure.html)

