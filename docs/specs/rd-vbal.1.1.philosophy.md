# 1.1 Design and Extension Philosophy

RD-VBA is not a *reinterpretation* of VBA. It is an effort to fully realize the VBA specification, MS-VBAL.

The RD-VBA *language core* shall remain strictly compatible with the MS-VBAL specifications. It shall not, however, be treated as a *fossilized language*.

## How to read this section

The RD-VBA philosophy is based on one idea:

- The *language core* describes what VBA already is.
- *Extensions* may build on what VBA already is, but must not alter it.

In other words, the language core preserves the semantic identity of VBA, and the platform enables evolution around the language core.

Sections [**RD-VBAL §1.1.1** Platform Extensions](rd-vbal.1.1.1.platform-extensions.md) to [**RD-VBAL §1.1.6** Capabilities Provider](rd-vbal.1.1.6.capabilities-provider.md) formalize this distinction between the language core and the platform and its extensions.

> [!NOTE]
> These design principles guide both implementation and contributions. Changes to the *language core* should preserve semantic compatibility with the language specifications, and should prioritize clarity over novelty.

---
## In this section

|§|Title|
|---|---|
|1.1.1|[Platform Extensions](rd-vbal.1.1.1.platform-extensions.md)|
|1.1.2|[Language Core Extensions](rd-vbal.1.1.2.language-core-extensions.md)|
|1.1.3|[Core Semantic Flags](rd-vbal.1.1.3.core-semantic-flags.md)|
|1.1.4|[Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md)|
|1.1.5|[Extension Manifest](rd-vbal.1.1.5.extension-manifest.md)|
|1.1.6|[Capabilities Provider](rd-vbal.1.1.6.capabilities-provider.md)|
