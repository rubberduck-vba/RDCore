# 6.1 VBA Project

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1** VBA Project](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f96f7c1e-4482-4603-8833-ed3cd2b4ac09).

The `VBA` project is a _host project_. It is present in every _VBA environment_.

The `VBA` project consists of a set of classes, functions, `Enum` types and constants that together form VBA's
_standard library_.

Because the `VBA` project is present in every VBA environment, the **RDCore** platform must implement the VBA
standard library.

The _environment host_ shall inject the VBA standard library's symbols into all `VBA` projects. The injected
symbols shall carry the appropriate _return type_ metadata.

The SDK defines the interfaces for the _internal representation_ of each standard-library module, and the environment
host exposes the symbols provided by the standard library to the _workspace_. See
[**RD-VBAL §6.0.1** Symbol Injection](rd-vbal.6.0.standard-library.md#601-symbol-injection).

---
## In this section

|§|Title|MS-VBAL|
|---|---|---|
|6.1.1|[Predefined Enums](rd-vbal.6.1.1.predefined-enums.md)|[§6.1.1](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/cc0c4b7c-bd09-448b-9eeb-19a9d4c19504)|
|6.1.2|[Predefined Procedural Modules](rd-vbal.6.1.2.predefined-procedural-modules.md)|[§6.1.2](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e74eaaeb-e2e5-4ec5-9fb0-f3c739c53403)|
|6.1.3|[Predefined Class Modules](rd-vbal.6.1.3.predefined-class-modules.md)|[§6.1.3](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6b7b6a27-3355-455e-a6ce-93588373c783)|

---
> ⏮️ [**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.md) | ⏭️ [**RD-VBAL §6.1.1** Predefined Enums](rd-vbal.6.1.1.predefined-enums.md)
