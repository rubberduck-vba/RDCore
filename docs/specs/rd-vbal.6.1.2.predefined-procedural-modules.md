# 6.1.2 Predefined Procedural Modules

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2** Predefined Procedural Modules](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e74eaaeb-e2e5-4ec5-9fb0-f3c739c53403).

The SDK defines the twelve **MS-VBAL §6.1.2** predefined procedural modules:

|§|Module|SDK declaration|
|---|---|---|
|6.1.2.1|[ColorConstants Module](rd-vbal.6.1.2.1.colorconstants-module.md)|[IStdColorConstantsModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdColorConstantsModule.html)|
|6.1.2.2|[Constants Module](rd-vbal.6.1.2.2.constants-module.md)|[IStdConstantsModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdConstantsModule.html)|
|6.1.2.3|[Conversion Module](rd-vbal.6.1.2.3.conversion-module.md)|[IStdConversionModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdConversionModule.html)|
|6.1.2.4|[DateTime Module](rd-vbal.6.1.2.4.datetime-module.md)|[IStdDateTimeModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdDateTimeModule.html)|
|6.1.2.5|[FileSystem](rd-vbal.6.1.2.5.filesystem.md)|[IStdFileSystemModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdFileSystemModule.html)|
|6.1.2.6|[Financial](rd-vbal.6.1.2.6.financial.md)|[IStdFinancialModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdFinancialModule.html)|
|6.1.2.7|[Information](rd-vbal.6.1.2.7.information.md)|[IStdInformationModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdInformationModule.html)|
|6.1.2.8|[Interaction](rd-vbal.6.1.2.8.interaction.md)|[IStdInteractionModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdInteractionModule.html)|
|6.1.2.9|[KeyCodeConstants](rd-vbal.6.1.2.9.keycodeconstants.md)|[VBKeyCodeConstants](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBKeyCodeConstants.html)|
|6.1.2.10|[Math](rd-vbal.6.1.2.10.math.md)|[IStdMathModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdMathModule.html)|
|6.1.2.11|[Strings](rd-vbal.6.1.2.11.strings.md)|[IStdStringsModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdStringsModule.html)|
|6.1.2.12|[SystemColorConstants](rd-vbal.6.1.2.12.systemcolorconstants.md)|[VBSystemColorConstants](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBSystemColorConstants.html)|

And one that the specification does not name but the language has: the library's hidden module, which holds `Array`, `Input$` and `Width`
(`_HiddenModule`, **RD-VBAL §6.1.2.13**):

|§|Module|SDK declaration|
|---|---|---|
|6.1.2.13|[Hidden Module](rd-vbal.6.1.2.13.hidden-module.md)|[IStdHiddenModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdHiddenModule.html)|

---
> ⏮️ [**RD-VBAL §6.1.1** Predefined Enums](rd-vbal.6.1.1.predefined-enums.md) | ⏭️ [**RD-VBAL §6.1.2.1** ColorConstants Module](rd-vbal.6.1.2.1.colorconstants-module.md)
