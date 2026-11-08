using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.SDK.Extensibility.Diagnostics;

/// <summary>
/// Request for <c>rdcore/diagnostics/document</c>: the language server hands a diagnostics-provider
/// extension everything it needs to analyze one document and asks for the diagnostics it finds.
/// </summary>
/// <remarks>
/// The language server is the orchestrator: it owns the workspace and the parser, so it pushes the
/// parsed <see cref="ModuleParseResult"/> down rather than have each provider re-read and re-parse.
/// The parse result's AST is polymorphic and the JSON-RPC transport's serializer cannot round-trip it
/// (see <see cref="PlatformJson"/>), so the payload rides a <see cref="System.Text.Json"/> string in
/// <see cref="Json"/>. The response is a plain <see cref="DiagnoseDocumentResult"/>: an LSP
/// <see cref="Diagnostic"/> is the transport serializer's own model. Only extensions whose manifest
/// advertises the <see cref="DiagnoseDocument"/> capability receive this request.
/// </remarks>
[Method(RDCoreExtensionProtocol.DiagnoseDocument, Direction.ClientToServer)]
public record class DiagnoseDocumentParams : IRequest, IRequest<DiagnoseDocumentResult>
{
    /// <summary>
    /// The <see cref="System.Text.Json"/> representation of a <see cref="DiagnoseDocumentPayload"/>.
    /// </summary>
    public string Json { get; init; } = string.Empty;
}

/// <summary>
/// The <see cref="System.Text.Json"/> payload carried in <see cref="DiagnoseDocumentParams.Json"/>.
/// </summary>
/// <remarks>
/// Deliberately a record the language server grows: a <c>SemanticContext</c> field (resolver output:
/// bound types, semantic flags) joins it once the resolver exists, so semantic and runtime analyzers
/// receive the same envelope. <see cref="SourceVersion"/> is the workspace document version the parse
/// was taken at; a provider echoes it back so the language server can drop a report that raced a
/// later edit.
/// </remarks>
/// <param name="DocumentUri">The document being diagnosed.</param>
/// <param name="SourceVersion">The workspace document version <paramref name="ParseResult"/> was produced from.</param>
/// <param name="ParseResult">The parsed module: AST plus syntax errors.</param>
/// <param name="Semantics">
/// What the environment host's semantic analysis pass found out about the module (<strong>RD-VBAL §5.0.3</strong>): the facts an analyzer decides what to say of.
/// <see langword="null"/> when the host has none, because it is not part of the platform or the module's code has not been loaded.
/// </param>
/// <param name="Phase">
/// Which phase of the analysis the provider is asked for (<see cref="AnalysisPhase"/>): what it answers is the findings of that phase only, and the language server
/// puts the phases together. Every phase, when not said.
/// </param>
public record class DiagnoseDocumentPayload(
    Uri DocumentUri, int SourceVersion, ModuleParseResult ParseResult, ModuleSemanticsDto? Semantics = null, AnalysisPhase Phase = AnalysisPhase.All);

/// <summary>
/// Response to <c>rdcore/diagnostics/document</c>: the diagnostics a provider found, already projected
/// to LSP <see cref="Diagnostic"/>s (code, <c>codeDescription</c> help URL, and structured
/// <c>data</c>), plus the source version echoed back for the language server's staleness gate.
/// </summary>
public record class DiagnoseDocumentResult
{
    /// <summary>
    /// The diagnostics the provider found, in source order.
    /// </summary>
    public Container<Diagnostic> Diagnostics { get; init; } = new();

    /// <summary>
    /// The <see cref="DiagnoseDocumentPayload.SourceVersion"/> echoed back unchanged.
    /// </summary>
    public int SourceVersion { get; init; }
}
