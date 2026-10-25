using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Progress;
using RDCore.SDK.Server.Progress;

namespace RDCore.LanguageServer.Diagnostics;

/// <summary>
/// Serves the <strong>LSP 3.17</strong> <c>textDocument/diagnostic</c> pull request. The client asks;
/// the handler delegates the provider fan-out and the version staleness gate to
/// <see cref="IDocumentDiagnosticsService"/> and returns the aggregated diagnostics — already LSP
/// <see cref="Diagnostic"/>s, projected by the provider — as a related full or unchanged report.
/// </summary>
internal sealed class DocumentDiagnosticHandler(IDocumentDiagnosticsService diagnostics, IWorkProgressReporter progress) : DocumentDiagnosticHandlerBase
{
    public override async Task<RelatedDocumentDiagnosticReport> Handle(DocumentDiagnosticParams request, CancellationToken cancellationToken)
    {
        var documentUri = request.TextDocument.Uri.ToUri();
        using var work = progress.BeginFor(request, string.Format(ProgressMessages.Analyzing, Path.GetFileNameWithoutExtension(documentUri.AbsolutePath)));
        var result = await diagnostics.GetAsync(documentUri, request.PreviousResultId, cancellationToken, work);

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
