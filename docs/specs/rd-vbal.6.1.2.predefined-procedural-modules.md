# 6.1.2 Predefined Procedural Modules

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2** Predefined Procedural Modules](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e74eaaeb-e2e5-4ec5-9fb0-f3c739c53403).

The language core defines the twelve **MS-VBAL §6.1.2** predefined procedural modules:

|§|Module|Declaration|
|---|---|---|
|6.1.2.1|[ColorConstants Module](rd-vbal.6.1.2.1.colorconstants-module.md)|`IStdColorConstantsModule`|
|6.1.2.2|[Constants Module](rd-vbal.6.1.2.2.constants-module.md)|`IStdConstantsModule`|
|6.1.2.3|[Conversion Module](rd-vbal.6.1.2.3.conversion-module.md)|`IStdConversionModule`|
|6.1.2.4|[DateTime Module](rd-vbal.6.1.2.4.datetime-module.md)|`IStdDateTimeModule`|
|6.1.2.5|[FileSystem](rd-vbal.6.1.2.5.filesystem.md)|`IStdFileSystemModule`|
|6.1.2.6|[Financial](rd-vbal.6.1.2.6.financial.md)|`IStdFinancialModule`|
|6.1.2.7|[Information](rd-vbal.6.1.2.7.information.md)|`IStdInformationModule`|
|6.1.2.8|[Interaction](rd-vbal.6.1.2.8.interaction.md)|`IStdInteractionModule`|
|6.1.2.9|[KeyCodeConstants](rd-vbal.6.1.2.9.keycodeconstants.md)|`IStdKeyCodeConstantsModule`|
|6.1.2.10|[Math](rd-vbal.6.1.2.10.math.md)|`IStdMathModule`|
|6.1.2.11|[Strings](rd-vbal.6.1.2.11.strings.md)|`IStdStringsModule`|
|6.1.2.12|[SystemColorConstants](rd-vbal.6.1.2.12.systemcolorconstants.md)|`IStdSystemColorConstantsModule`|

And one that the specification does not name but the language has: the library's hidden module, which holds `Array`, `Input$` and `Width`
(`_HiddenModule`, **RD-VBAL §6.1.2.13**):

|§|Module|Declaration|
|---|---|---|
|6.1.2.13|[Hidden Module](rd-vbal.6.1.2.13.hidden-module.md)|`IStdHiddenModule`|
