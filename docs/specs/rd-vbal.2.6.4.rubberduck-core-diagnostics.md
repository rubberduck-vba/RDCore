# 2.6.4 Rubberduck Core Diagnostics

**Rubberduck Core diagnostics** are the analyzer findings issued by the `RDCore.Diagnostics` analyzers. They cover implicit declarations, obsolete syntax, misleading constructs, every inspection that the legacy Rubberduck add-in shipped, and further inspections beyond those.

|||
|---|---|
|Code family|`RDC`, defined by [RDCoreDiagnosticId](../api/RDCore.SDK.Model.Diagnostics.RDCoreDiagnosticId.html); the enum value is the code|
|Title|per finding|
|Raised by|the `RDCore.Diagnostics` analyzers|
|Severity|spans `Hint` through `Error`, per finding|

Any other *language core diagnostics* issued from RDCore.Diagnostics are to be documented under an `RDC00000` code; see [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).

## Published codes

An analyzer is a pure function of facts (`IModuleAnalyzer`): the environment host runs the semantic analysis pass and says what
is true of the code, and an analyzer decides what is worth saying
([**RD-VBAL §5.0.3** Semantic Analysis](rd-vbal.5.0.semantics.md)). An analyzer that is not given the fact it reads has nothing to say.

|Code|Title|Severity|Reads the fact|
|---|---|---|---|
|[`RDC00101`](../diagnostics/rdc00101.md)|Implicit declarations enabled|Warning|`ModuleSemanticModel.OptionExplicit`|
|[`RDC00302`](../diagnostics/rdc00302.md)|Obsolete Call statement|Hint|`ValueExpressionSemanticFlags.ExplicitCallKeyword`|

What the host's static pass found wrong with the code is not an opinion: it is reported as the `VBC` compile error it is, by the same
extension, from the same model.

## Language Core Conditions and Analyzer Opinions

The `VBC`, `VBR` and `VBA` diagnostic families describe conditions the language core defines. `RDC` diagnostics are opinions of the analyzer.

Shadowing of `VBA` library definitions should be detected in the semantic layer and reported through semantic flags, so that RDCore.Diagnostics can issue shadowed declaration diagnostics; see [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md) and [**RD-VBAL §1.1.3** Core Semantic Flags](rd-vbal.1.1.3.core-semantic-flags.md).

## Extension Diagnostics

🧩 Diagnostics contributed by other extensions must use their own prefix, distinct from `RDC`, so that codes stay unique and traceable to their source. The `RDX00000` format and third-party prefixes are specified in [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).

---
> ⏮️ [**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md) | ⏭️ [**RD-VBAL §2.6.5** Diagnostics Pipeline](rd-vbal.2.6.5.diagnostics-pipeline.md)
