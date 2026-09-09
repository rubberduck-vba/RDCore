using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/diagnostics/document</c>: the language server hands a diagnostics-provider
/// extension everything it needs to analyze one document and asks for the diagnostics it finds.
/// </summary>
/// <remarks>
/// The language server is the orchestrator — it owns the workspace and the parser, so it pushes the
/// parsed <see cref="ModuleParseResult"/> down rather than have each provider re-read and re-parse.
/// The parse result's AST is polymorphic and the JSON-RPC transport's serializer cannot round-trip it
/// (see <see cref="PlatformJson"/>), so the payload rides a <see cref="System.Text.Json"/> string in
/// <see cref="Json"/> and the response rides a <see cref="PlatformJsonEnvelope"/>. Only extensions
/// whose manifest advertises the <see cref="DiagnoseDocument"/> capability receive this request.
/// </remarks>
[Method(RDCorePlatformProtocol.DiagnoseDocument, Direction.ClientToServer)]
public record class DiagnoseDocumentRequest : IRequest, IRequest<PlatformJsonEnvelope>
{
    /// <summary>
    /// The <see cref="System.Text.Json"/> representation of a <see cref="DiagnoseDocumentPayload"/>.
    /// </summary>
    public string Json { get; init; } = string.Empty;
}

/// <summary>
/// The <see cref="System.Text.Json"/> payload carried in <see cref="DiagnoseDocumentRequest.Json"/>.
/// </summary>
/// <remarks>
/// Deliberately a record the language server grows: a <c>SemanticContext</c> field (resolver output —
/// bound types, semantic flags) joins it once the resolver exists, so semantic and runtime analyzers
/// receive the same envelope. <see cref="SourceVersion"/> is the workspace document version the parse
/// was taken at; a provider echoes it back so the language server can drop a report that raced a
/// later edit.
/// </remarks>
/// <param name="DocumentUri">The document being diagnosed.</param>
/// <param name="SourceVersion">The workspace document version <paramref name="ParseResult"/> was produced from.</param>
/// <param name="ParseResult">The parsed module — AST plus syntax errors.</param>
public record class DiagnoseDocumentPayload(Uri DocumentUri, int SourceVersion, ModuleParseResult ParseResult);

/// <summary>
/// The <see cref="System.Text.Json"/> payload carried in the response <see cref="PlatformJsonEnvelope"/>.
/// </summary>
/// <param name="Diagnostics">The diagnostics the provider found.</param>
/// <param name="SourceVersion">The <see cref="DiagnoseDocumentPayload.SourceVersion"/> echoed back unchanged.</param>
public record class DiagnoseDocumentResult(PlatformDiagnostic[] Diagnostics, int SourceVersion);
