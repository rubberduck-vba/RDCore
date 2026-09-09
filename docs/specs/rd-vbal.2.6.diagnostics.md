# 2.6 Diagnostics
> [!NOTE]
> This specification may be incomplete at this time.

🎯 Every problem the **RDCore** platform finds in a workspace — a syntax error, a static or runtime
compilation error, an analyzer finding — surfaces to the editor as an **LSP diagnostic** carrying a
stable **code**, a **help URL** for that code, and, for error diagnostics, structured detail.

Diagnostic codes are grouped into four families by the layer that raises them:

|Family|Prefix|Raised by|Section|
|---|---|---|---|
|Syntax errors|`VBC`|the parser (concrete syntax tree)|[§2.6.1](#261-syntax-errors)|
|Semantic compilation errors|`VBC`|the static semantics layer (abstract syntax tree)|[§2.6.2](#262-semantic-compilation-errors)|
|Runtime errors|`VBR` / `VBA`|the runtime semantics layer / workspace `Err.Raise`|[§2.6.3](#263-runtime-errors)|
|Rubberduck Core diagnostics|`RDC`|the `RDCore.Diagnostics` analyzers|[§2.6.4](#264-rubberduck-core-diagnostics)|

The numeric portion is a five-digit zero-padded code (`VBC00001`, `VBR00009`, `RDC01001`). Each code
is documented on its own page at
`https://rubberduck-vba.github.io/RDCore/diagnostics/<code>.html` on this documentation site, and
every emitted diagnostic points there through the LSP `codeDescription` field — the client opens that
URL when the reader follows a diagnostic's "learn more".

## Pipeline

The language server does not compute diagnostics itself. A _diagnostics provider_ is a platform
extension whose manifest advertises the `DiagnoseDocument` capability
(`[assembly: ProvidesCorePlatformClientCapability<DiagnoseDocument>]`, recorded by
`rdc.exe describe-ext` in the extension's
[`extension.manifest.json`](rd-vbal.2.3.application-host.html)). The set of registered capabilities —
not a hard-coded list — determines which extensions the language server asks. **RDCore.Diagnostics**
is the core-bundled provider, always brought up during platform assembly; other extensions
(dimensional analysis, and so on) register alongside it. With no provider registered, a workspace
simply has no diagnostics.

Diagnostics use the **LSP 3.17 pull model** (`textDocument/diagnostic`). When the editor asks for a
document, the language server, as orchestrator:

1. resolves the workspace document and its current version;
2. parses it (the authoritative parse);
3. fans the parsed [`ModuleParseResult`](rd-vbal.3.0.syntax-tree.html) out to every registered
   provider over the internal `rdcore/diagnostics/document` request — the language server owns the
   document and parser state and pushes them _down_, so a provider needs no parser or file-system
   access of its own;
4. aggregates the LSP `Diagnostic`s the providers return (each provider projects its own findings
   through `ICoreDiagnosticsFactory`), collapsing exact duplicates, and answers the pull.

`rdcore/diagnostics/document` carries the parse result as a
[`PlatformJson`](rd-vbal.2.3.application-host.html) string because the syntax tree is polymorphic; it
is the seam a future `SemanticContext` (resolver output) is added to, so the semantic and runtime
passes receive the same envelope. The editor edge stays plain LSP throughout — only the
language-server-to-provider hop is an RDCore request.

The report's `resultId` tracks the document's in-memory version. A `previousResultId` that still
matches answers a `RelatedUnchangedDocumentDiagnosticReport` and computes nothing. Results are also
**staleness-gated**: the version is captured before the fan-out and re-checked after; a report that
raced a later edit is dropped rather than returned, and the `resultId` advances to the current
version.

> [!NOTE]
> Document versioning is inert until `textDocument/didChange` is handled — today the version only
> moves on workspace reload or rename. Proactive push (`textDocument/publishDiagnostics`) and
> workspace-wide diagnostics (`workspace/diagnostic`) are forthcoming; the pull pipeline is the seam
> they hang off. On start-up the language server pulls diagnostics for every loaded document once, to
> exercise the fan-out without an editor attached.

---
## 2.6.1 Syntax Errors

A **syntax error** is raised while the parser traverses the _concrete syntax tree_ (CST) — a token
the grammar cannot place. It is the inaugural diagnostic the platform emits.

|||
|---|---|
|Code family|`VBC` — `VBC00001`–`VBC00999`|
|Source metadata|[`VBSyntaxErrorInfo`](../api/RDCore.SDK.Model.Errors.VBSyntaxErrorInfo.html) (`ErrorId` is a [`VBCompileErrorId`](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html))|
|Severity|`Error`|
|Detail|the faulted token and its expected role, on `Diagnostic.data`|

MS-VBAL does not distinguish a compile-time error raised in CST semantics from one raised in AST
semantics; RDCore splits them by numeric range only. A `#If` that splits a statement is unparseable
by the grammar and reports located `VBC` diagnostics a client can anchor a squiggle on.

---
## 2.6.2 Semantic Compilation Errors

A **semantic compilation error** is raised by the static semantics layer while walking the _abstract
syntax tree_ (AST) with symbol information — a duplicate declaration, an undefined name, a type
mismatch in a constant expression.

|||
|---|---|
|Code family|`VBC` — `VBC09300`–`VBC09999`|
|Source metadata|[`VBCompileErrorInfo`](../api/RDCore.SDK.Model.Errors.VBCompileErrorInfo.html)|
|Severity|`Error`|
|Detail|the offending symbol / expression, on `Diagnostic.data`|

Emitted once the resolver and static semantic pass are online; the provider projects them through the
same `ICoreDiagnosticsFactory` as syntax errors.

---
## 2.6.3 Runtime Errors

A **runtime error** is raised by the runtime semantics layer and left unhandled by workspace code — a
subscript out of range, a type-mismatch coercion, division by zero.

|||
|---|---|
|Code family|`VBR` — the numeric portion matches the corresponding MS-VBA run-time error code|
|Source metadata|[`VBRuntimeErrorInfo`](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorInfo.html)|
|Severity|`Error`|

An **application error** is a custom run-time error explicitly raised from workspace source code with
`Error` or `Err.Raise`. MS-VBAL does not distinguish it from a semantic run-time error.

|||
|---|---|
|Code family|`VBA` — pseudo-code; the numeric portion matches the application-supplied error code|
|Source metadata|[`VBApplicationErrorInfo`](../api/RDCore.SDK.Model.Errors.VBApplicationErrorInfo.html)|
|Severity|`Error`|

---
## 2.6.4 Rubberduck Core Diagnostics

**Rubberduck Core diagnostics** are the analyzer findings issued by the `RDCore.Diagnostics`
analyzers — implicit declarations, obsolete syntax, misleading constructs, and every inspection the
legacy Rubberduck add-in shipped, and then some.

|||
|---|---|
|Code family|`RDC` — [`RDCoreDiagnosticId`](../api/RDCore.SDK.Model.Diagnostics.RDCoreDiagnosticId.html); the enum value is the code|
|Severity|spans `Hint` through `Error`, per finding|

Unlike the `VBC`/`VBR`/`VBA` families, which describe conditions the language core defines, `RDC`
diagnostics are opinions of the analyzer. Diagnostics contributed by **other extensions** must use
their own prefix, distinct from `RDC`, so codes stay unique and traceable to their source.

---
> ⏮️ [**RD-VBAL §2.5** Runtime Values](rd-vbal.2.5.runtime-values.html) | ⏭️ [**RD-VBAL §3.0** Syntax Tree](rd-vbal.3.0.syntax-tree.html)
