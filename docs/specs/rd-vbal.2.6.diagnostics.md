# 2.6 Diagnostics

> [!NOTE]
> This specification may be incomplete at this time.

Every problem the **RDCore** platform finds in a workspace surfaces to the editor as an **LSP diagnostic**. Such problems include syntax errors, static or runtime compilation errors, and analyzer findings.

Every RDCore LSP diagnostic carries:

- a stable **code**;
- a **help URL** for that code;
- for an error diagnostic, structured **detail**.

**RDCore.Diagnostics** is the core platform extension responsible for issuing all *language core diagnostics*. Additional first-party or third-party extensions may provide additional or advanced diagnostics to the LSP orchestration layer. See [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).

Diagnostics reach the editor through an LSP pull pipeline that asks diagnostics providers; see [**RD-VBAL §2.6.5** Diagnostics Pipeline](rd-vbal.2.6.5.diagnostics-pipeline.md).

## Code Families

Diagnostic codes are grouped into four families by the layer that raises them. The code prefixes are `VBC`, `VBR`, `VBA` and `RDC`.

|Family|Prefix|Title|Raised by|Section|
|---|---|---|---|---|
|Syntax errors|`VBC`|_Syntax error_|the parser (concrete syntax tree)|[**RD-VBAL §2.6.1** Syntax Errors](rd-vbal.2.6.1.syntax-errors.md)|
|Semantic compilation errors|`VBC`|_Compile error_|the static semantics layer (abstract syntax tree)|[**RD-VBAL §2.6.2** Semantic Compilation Errors](rd-vbal.2.6.2.semantic-compilation-errors.md)|
|Runtime errors|`VBR` / `VBA`|_Run-time error_ (`VBR`) / _Application error_ (`VBA`)|the runtime semantics layer (`VBR`) / a workspace `Err.Raise` (`VBA`)|[**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md)|
|Rubberduck Core diagnostics|`RDC`|_(per finding)_|the `RDCore.Diagnostics` analyzers|[**RD-VBAL §2.6.4** Rubberduck Core Diagnostics](rd-vbal.2.6.4.rubberduck-core-diagnostics.md)|

The code format of each family (`VBC00000`, `VBR00000`, `VBA00000`, `RDC00000`), and the `RDX00000` format for extension diagnostics, is specified in [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).

## Titles

Every diagnostic family has a **title**. A diagnostic's title is the error's *category*: what kind of thing went wrong. Its *description*, as distinct from its title, is what went wrong.

For example, a diagnostic titled "Run-time error" has the description "Division by zero".

|Family|Title|
|---|---|
|Syntax errors|_Syntax error_|
|Semantic compilation errors|_Compile error_|
|Runtime errors (`VBR`)|_Run-time error_|
|Runtime errors (`VBA`)|_Application error_|
|Rubberduck Core diagnostics|per finding|

The title is localized. The description of compilation and run-time errors shall exactly match the corresponding MS-VBA descriptions; see [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).

A diagnostic's title is derived rather than stored, because the two `VBC` categories (syntax errors and semantic compilation errors) share one family and nothing but the numeric portion separates them. `VBCompileErrorId` reserves the range `[9300..]` for semantic compilation errors. Every `VBCompileErrorId` value below 9300 belongs to the parser (syntax errors).

The title derivation ([VBErrorExtensions](../api/RDCore.SDK.Model.Errors.Abstract.VBErrorExtensions.html)) switches on the error's runtime type. An error held through a more general declared type therefore still takes the title of its own family.

## Codes and Help URLs

The numeric portion of a diagnostic code is a five-digit zero-padded code, e.g. `VBC00001`, `VBR00009`, `RDC01001`.

Each diagnostic code is documented on its own page under [Diagnostics](../diagnostics/index.md). A code's help page URL is `https://rubberduck-vba.github.io/RDCore/diagnostics/<code>.html`, with `<code>` in lower case (e.g. `.../diagnostics/vbc00001.html`).

Every emitted diagnostic points to its code's help page through the LSP `codeDescription` field. The client opens that URL when the reader follows a diagnostic's "learn more".

## Publication

A diagnostic code's page is published as soon as the platform can emit that code. The diagnostics documentation grows at the same rate as the diagnostics.

A published diagnostic code is **not renumbered** and **not retired**, so that older builds' diagnostic links keep resolving. The code and its abstract meaning do not change.

The prose of a code's page may evolve as the ideal set of codes is narrowed down. Each page describes the condition in the abstract: the specifics of a particular occurrence (which token, which literal, which type) travel in the diagnostic's verbose detail, not in the code.

## Severity

|Severity|Use|
|---|---|
|Error|Reserved for coded syntax/compilation and runtime/application errors.|
|Warning|Flags potential bugs or logical errors causing unexpected or unintended behavior, or severe performance issues. Warning diagnostics should be used carefully.|
|Hint, suggestion|Can be as opinionated as needed.|

The choice of a warning severity should take into account that a host environment can be configured to "treat warnings as errors". If a diagnostic is not worth breaking a build over, it is not a warning. See [**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md).
