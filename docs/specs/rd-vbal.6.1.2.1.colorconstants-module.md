# 6.1.2.1 ColorConstants Module

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.1** ColorConstants Module](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3201c251-c1a1-4465-96fa-78f4c9510978).

The `ColorConstants` module is represented in the SDK by the interface
[IStdColorConstantsModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdColorConstantsModule.html).

The eight constants are the specification's, each a `Long`, as in MS-VBA (recorded in Excel): `vbBlack`, `vbBlue`, `vbCyan`, `vbGreen`, `vbMagenta`,
`vbRed`, `vbWhite`, `vbYellow`. A constant is reached by its own name, or qualified by the module or by the library (`ColorConstants.vbRed`, `VBA.vbRed`,
`VBA.ColorConstants.vbRed`); it is a constant expression (`Const c As Long = vbRed + 1`, `Case vbRed`), and assigning to one is a compile error.
The module is no type: `Dim c As ColorConstants` is a compile error, as it is in MS-VBA ("Automation type not supported").

---
> ⏮️ [**RD-VBAL §6.1.2** Predefined Procedural Modules](rd-vbal.6.1.2.predefined-procedural-modules.md) | ⏭️ [**RD-VBAL §6.1.2.2** Constants Module](rd-vbal.6.1.2.2.constants-module.md)
