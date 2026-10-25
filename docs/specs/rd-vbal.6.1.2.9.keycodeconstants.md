# 6.1.2.9 KeyCodeConstants

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.9** KeyCodeConstants](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/56e717e8-b7dc-4153-afd6-db8418f807f6).

The `KeyCodeConstants` module is represented in the SDK by the interface
[IStdKeyCodeConstantsModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdKeyCodeConstantsModule.html).

The constants are the specification's, with its values, each an `Integer` - not a `Long`, as the colour constants are - as in MS-VBA (recorded in Excel).
A constant is reached by its own name, or qualified by the module or by the library (`KeyCodeConstants.vbKeyReturn`, `VBA.vbKeyReturn`); it is a constant
expression (`Case vbKeyReturn`), and assigning to one is a compile error. The module is no type: `Dim k As KeyCodeConstants` is a compile error.

---
> ⏮️ [**RD-VBAL §6.1.2.8** Interaction](rd-vbal.6.1.2.8.interaction.md) | ⏭️ [**RD-VBAL §6.1.2.10** Math](rd-vbal.6.1.2.10.math.md)
