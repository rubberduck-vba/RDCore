using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Progress;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Progress;

namespace RDCore.LanguageServer.Diagnostics;

/// <summary>
/// Serves the <strong>LSP 3.17</strong> <c>textDocument/diagnostic</c> pull request. The client asks;
/// the handler delegates the provider fan-out and the version staleness gate to
/// <see cref="IDocumentDiagnosticsService"/> and returns the aggregated diagnostics — already LSP
/// <see cref="Diagnostic"/>s, projected by the provider — as a related full or unchanged report.
/// </summary>
/// <remarks>
/// What is found of a document is of the text the environment host has: a pull that comes as the document is edited waits for the host to have the edit,
/// which it is told of at once (<see cref="IDocumentLifecycleService.WhenSyncedAsync"/>) - one analysis, of the text that is there.
/// </remarks>
internal sealed class DocumentDiagnosticHandler(
    IDocumentDiagnosticsService diagnostics, IDocumentLifecycleService lifecycle, IWorkProgressReporter progress, IncomingDocumentChanges incoming) : DocumentDiagnosticHandlerBase
{
    public override async Task<RelatedDocumentDiagnosticReport> Handle(DocumentDiagnosticParams request, CancellationToken cancellationToken)
    {
        var documentUri = request.TextDocument.Uri.ToUri();
        using var work = progress.BeginFor(request, string.Format(ProgressMessages.Analyzing, Path.GetFileNameWithoutExtension(documentUri.AbsolutePath)));
        // a change to the document that arrives meanwhile ends the pull, answered ContentModified: the change, and the client's next request, don't wait for it.
        var result = await incoming.WhileCurrentAsync(documentUri, cancellationToken, async token =>
        {
            await lifecycle.WhenSyncedAsync(documentUri, token);
            return await diagnostics.GetAsync(documentUri, request.PreviousResultId, token, work);
        });

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
        => PullDiagnostics;

    /// <summary>
    /// The pull diagnostics the server serves, which it registers for <c>textDocument/diagnostic</c> and <c>workspace/diagnostic</c> alike
    /// (<see cref="WorkspaceDiagnosticHandler"/>).
    /// </summary>
    /// <remarks>
    /// A module's diagnostics depend on what the other modules declare: a call to a procedure of another module is an error once the procedure is gone, and a
    /// name another module declares publicly is no undeclared variable. So <c>interFileDependencies</c> is <see langword="true"/>, which tells a client that a
    /// change to one document may change the diagnostics of others.
    /// </remarks>
    internal static DiagnosticsRegistrationOptions PullDiagnostics => new()
    {
        Identifier = "rdcore",
        InterFileDependencies = true,
        WorkspaceDiagnostics = true,
    };
}
