# 6.1.2.12 SystemColorConstants

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.12** SystemColorConstants](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/52478406-c992-4997-a1ec-c752f82915af).

The `SystemColorConstants` module is represented in the SDK by the interface
[IStdSystemColorConstantsModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdSystemColorConstantsModule.html).

The constants are the specification's, with its values, each a `Long` with its high bit set (`vbScrollBars` is `&H80000000`, which is `-2147483648`), as in
MS-VBA (recorded in Excel). `vb3DFace` and `vb3DShadow` are `vbButtonFace` and `vbButtonShadow` by other names. What colour each stands for is the system's
to say; RD-VBA draws nothing with one. A constant is reached by its own name, or qualified by the module or by the library
(`SystemColorConstants.vbButtonFace`, `VBA.vbButtonFace`), and assigning to one is a compile error. The module is no type.

---
> ⏮️ [**RD-VBAL §6.1.2.11** Strings](rd-vbal.6.1.2.11.strings.md) | ⏭️ [**RD-VBAL §6.1.2.13** Hidden Module](rd-vbal.6.1.2.13.hidden-module.md)
