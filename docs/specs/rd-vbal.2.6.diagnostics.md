# 2.6 Diagnostics
> [!NOTE]
> This specification may be incomplete at this time.

🎯 The **RDCore** platform surfaces every problem it finds in a workspace — syntax errors today,
semantic and analyzer findings as the resolver and inspection passes come online — to the editor as
**LSP diagnostics**, aggregated from one or more _diagnostics-provider extensions_ by the language
server.

---
## 2.6.1 Diagnostic providers

The language server does not itself compute diagnostics. A _diagnostics provider_ is a platform
extension whose manifest advertises the `DiagnoseDocument` capability
(`[assembly: ProvidesCorePlatformClientCapability<DiagnoseDocument>]`, recorded by
`rdc.exe describe-ext` in the extension's [`extension.manifest.json`](rd-vbal.2.3.application-host.html)).
The set of registered capabilities — not a hard-coded list — determines which extensions the language
server asks for diagnostics.

**RDCore.Diagnostics** is the core-bundled inaugural provider. It is always brought up during platform
assembly and today emits the parser's located syntax errors
([`VBSyntaxErrorInfo`](../api/RDCore.SDK.Model.Errors.VBSyntaxErrorInfo.html)) as platform
diagnostics. Its scope grows to every legacy Rubberduck inspection; other extensions
(shadowed-declaration diagnostics from the [semantic layer](rd-vbal.2.3.application-host.html),
dimensional-analysis, and so on) will register as providers alongside it.

When no provider is registered, the workspace simply has no diagnostics.

---
## 2.6.2 Pull model

Diagnostics use the **LSP 3.17 pull model** (`textDocument/diagnostic`). The editor requests
diagnostics for a document; the language server, as the platform orchestrator:

1. resolves the workspace document and its current version;
2. parses it (the authoritative parse, not a cache read);
3. fans the parsed [`ModuleParseResult`](rd-vbal.3.0.syntax-tree.html) out to every registered
   provider over the internal `rdcore/diagnostics/document` request — the language server owns the
   document and parser state and pushes them _down_, so a provider extension needs no parser or
   file-system access of its own;
4. aggregates the `PlatformDiagnostic`s the providers return, collapsing exact duplicates;
5. maps them to LSP `Diagnostic`s and answers the pull.

`rdcore/diagnostics/document` carries the parse result as a
[`PlatformJson`](rd-vbal.2.3.application-host.html) string because the syntax tree is polymorphic; it
is the seam a future `SemanticContext` (resolver output) is added to, so semantic and runtime
analyzers receive the same envelope. The editor edge stays plain LSP throughout — only the
language-server-to-extension hop is an RDCore request.

Proactive push (`textDocument/publishDiagnostics`) and workspace-wide diagnostics
(`workspace/diagnostic`) are forthcoming; the pull pipeline is the seam they hang off. On start-up the
language server pulls diagnostics for every loaded document once, to exercise the provider fan-out
without an editor attached.

---
## 2.6.3 Result identity and staleness

Each report carries a `resultId` derived from the workspace document's in-memory version. When the
editor sends back a `previousResultId` that still matches the document's current version, the language
server answers a `RelatedUnchangedDocumentDiagnosticReport` and computes nothing.

Diagnostics are also **staleness-gated**: the document version is captured before the provider
fan-out and re-checked after it. If the document changed in between, the just-computed diagnostics
describe a version the editor has already moved past, so they are dropped rather than returned, and
the report's `resultId` advances to the current version.

> [!NOTE]
> Document versioning is inert until `textDocument/didChange` is handled — today the version only
> moves on workspace reload or a rename. The gate is in place so that when incremental text
> synchronization lands, an edit that bumps the version automatically invalidates the next pull.

---
## 2.6.4 Severity and detail

A provider emits `PlatformDiagnostic` records — a transport-agnostic shape with no dependency on the
LSP types:

|Field|Description|
|---|---|
|`Code`|The numeric error id — `VBSyntaxErrorInfo.ErrorId` (a [`VBCompileErrorId`](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html)) for a syntax error.|
|`Source`|The emitting provider's name, e.g. `RDCore.Diagnostics`.|
|`Severity`|`Error`, `Warning`, `Information`, or `Hint` — the numeric values match the LSP scale.|
|`Location`|A [`SourceLocation`](../api/RDCore.SDK.Model.Source.SourceLocation.html): a document `Uri` and a zero-based line/character range that maps directly to an LSP `Range`.|
|`Message`|The human-readable message.|
|`Verbose`|Optional detail — a faulted token's semantics, a stack — carried on the LSP `Diagnostic.Data` field so a client can surface it without a second request.|

---
> ⏮️ [**RD-VBAL §2.5** Runtime Values](rd-vbal.2.5.runtime-values.html) | ⏭️ [**RD-VBAL §3.0** Syntax Tree](rd-vbal.3.0.syntax-tree.html)
