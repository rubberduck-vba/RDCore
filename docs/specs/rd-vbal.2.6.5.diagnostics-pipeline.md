# 2.6.5 Diagnostics Pipeline

The language server does not compute diagnostics itself. Diagnostics are served by an LSP pull pipeline (`textDocument/diagnostic`) that asks *diagnostics providers*. The pipeline gives each result a result identity and gates its results on the document version.

## Providers

A *diagnostics provider* is a platform extension whose manifest advertises the [DiagnoseDocument](../api/RDCore.SDK.Client.DiagnoseDocument.html) capability. The extension declares it with the assembly attribute `[assembly: ProvidesCorePlatformClientCapability<DiagnoseDocument>]` ([`ProvidesCorePlatformClientCapabilityAttribute<T>`](../api/RDCore.SDK.Client.ProvidesCorePlatformClientCapabilityAttribute-1.html)). `rdc.exe describe-ext` records the declaration in the extension's `extension.manifest.json`; see [**RD-VBAL §1.1.5** Extension Manifest](rd-vbal.1.1.5.extension-manifest.md).

The set of registered capabilities, not a hard-coded list, determines which extensions the language server asks for diagnostics.

|Provider|Registration|
|---|---|
|**RDCore.Diagnostics**|The core-bundled diagnostics provider. It is always brought up during platform assembly.|
|Other extensions (dimensional analysis, and so on)|Register as diagnostics providers alongside RDCore.Diagnostics.|

With no diagnostics provider registered, a workspace has no diagnostics.

## Pull Model

Diagnostics use the **LSP 3.17 pull model** (`textDocument/diagnostic`). When the editor asks for the diagnostics of a document, the language server acts as orchestrator:

1. It resolves the workspace document and its current version.
2. It parses the document. This parse is the authoritative parse.
3. It fans the parsed [ModuleParseResult](../api/RDCore.SDK.Model.AST.ModuleParseResult.html) (see [**RD-VBAL §3.0** Abstract Syntax Tree](rd-vbal.3.0.syntax-tree.md)) out to every registered provider, over the internal `rdcore/diagnostics/document` request.
4. It aggregates the LSP `Diagnostic`s the providers return, collapsing exact duplicates (the same range, code, source and message).
5. It answers the pull.

The language server owns the document and parser state and pushes them down to the diagnostics providers. A diagnostics provider therefore needs no parser or file-system access of its own.

Each diagnostics provider projects its own findings to LSP `Diagnostic`s through [ICoreDiagnosticsFactory](../api/RDCore.SDK.Model.Diagnostics.ICoreDiagnosticsFactory.html).

> [!NOTE]
> **Not implemented.** Proactive push of diagnostics (`textDocument/publishDiagnostics`) and workspace-wide diagnostics (`workspace/diagnostic`) are not implemented. Diagnostics are served by the document pull only.

## rdcore/diagnostics/document

|Hop|Protocol|
|---|---|
|Editor → language server|plain LSP (`textDocument/diagnostic`)|
|Language server → diagnostics provider|RDCore request `rdcore/diagnostics/document` ([DiagnoseDocumentRequest](../api/RDCore.SDK.Platform.Protocol.DiagnoseDocumentRequest.html), answered by a [DiagnoseDocumentResponse](../api/RDCore.SDK.Platform.Protocol.DiagnoseDocumentResponse.html))|

The editor edge of the diagnostics pipeline stays plain LSP throughout. Only the language-server-to-provider hop is an RDCore request.

The `rdcore/diagnostics/document` request carries the parse result ([DiagnoseDocumentPayload](../api/RDCore.SDK.Platform.Protocol.DiagnoseDocumentPayload.html)) as a [PlatformJson](../api/RDCore.SDK.Platform.Protocol.PlatformJson.html) string, because the syntax tree is polymorphic.

> [!NOTE]
> **Not implemented.** The `rdcore/diagnostics/document` request does not carry a `SemanticContext` (resolver output). It carries the parse result.

## Result Identity and Staleness

The diagnostic report's `resultId` is of the document's in-memory version and of the workspace it is in (`v3.12`). The findings of a module whose text did not change change with what the other modules declare: a procedure another module calls, removed, makes the call an error. So the workspace has a generation, which moves whenever a change can change what is found out about a module that did not change - a module whose declarations changed, a module that is gone, a reload of the project, a change of the live settings ([**RD-VBAL §2.0.2**](rd-vbal.2.0.2.client-server-capabilities.md#refreshing-what-the-client-shows)). A `previousResultId` that still matches both is answered with a `RelatedUnchangedDocumentDiagnosticReport`, and the language server computes nothing.

Diagnostic results are staleness-gated:

1. The document version, and the workspace's generation, are captured before the diagnostics fan-out.
2. The version is re-checked after the fan-out.
3. A report that raced a later edit is dropped rather than returned, and the pull is answered `ContentModified` (-32801), which the client asks again after. No report is made of text that is not there any more: its `resultId`, sent again, would be answered `unchanged` for findings the client never had.

## Start-up

On start-up the language server pulls diagnostics for every loaded document once. This exercises the fan-out without an editor attached.

---
> ⏮️ [**RD-VBAL §2.6.4** Rubberduck Core Diagnostics](rd-vbal.2.6.4.rubberduck-core-diagnostics.md) | ⏭️ [**RD-VBAL §3.0** Abstract Syntax Tree](rd-vbal.3.0.syntax-tree.md)
