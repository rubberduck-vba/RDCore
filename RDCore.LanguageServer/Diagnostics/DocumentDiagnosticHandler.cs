using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.LanguageServer.Diagnostics;

/// <summary>
/// Serves the <strong>LSP 3.17</strong> <c>textDocument/diagnostic</c> pull request. The client asks;
/// the handler delegates the provider fan-out and the version staleness gate to
/// <see cref="IDocumentDiagnosticsService"/> and returns the aggregated diagnostics — already LSP
/// <see cref="Diagnostic"/>s, projected by the provider — as a related full or unchanged report.
/// </summary>
internal sealed class DocumentDiagnosticHandler(IDocumentDiagnosticsService diagnostics) : DocumentDiagnosticHandlerBase
{
    public override async Task<RelatedDocumentDiagnosticReport> Handle(DocumentDiagnosticParams request, CancellationToken cancellationToken)
    {
        var result = await diagnostics.GetAsync(request.TextDocument.Uri.ToUri(), request.PreviousResultId, cancellationToken);

        if (result.Unchanged)
        {
            return new RelatedUnchangedDocumentDiagnosticReport { ResultId = result.ResultId };
        }

        return new RelatedFullDocumentDiagnosticReport
        {
            ResultId = result.ResultId,
            Items = new Container<Diagnostic>(result.Diagnostics),
        };
    }

    protected override DiagnosticsRegistrationOptions CreateRegistrationOptions(
        DiagnosticClientCapabilities capability, ClientCapabilities clientCapabilities)
        => new()
        {
            Identifier = "rdcore",
            // one module's diagnostics never depend on another's, and workspace diagnostics aren't served yet.
            InterFileDependencies = false,
            WorkspaceDiagnostics = false,
        };
}
