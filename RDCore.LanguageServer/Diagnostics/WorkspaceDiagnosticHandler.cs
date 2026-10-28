using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.LanguageServer.Progress;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Progress;
using RDCore.SDK.Server.Serialization;

namespace RDCore.LanguageServer.Diagnostics;

/// <summary>
/// Serves <c>workspace/diagnostic</c> (<strong>LSP 3.17</strong> §Workspace Diagnostics): the diagnostics of every module of the workspace, open or not - a
/// report for each, as the module's own pull answers it (<see cref="DocumentDiagnosticHandler"/>).
/// </summary>
/// <remarks>
/// <para>
/// A module whose result id the client sends (<c>previousResultIds</c>) and that has not changed since is reported <c>unchanged</c>; the result ids are the
/// document pull's, so a client can mix the two. A report's <c>version</c> is the client's for a document it has open, and <c>null</c> for one it has not.
/// </para>
/// <para>
/// A client that gives a <c>partialResultToken</c> is sent each module's report as it is made (<c>$/progress</c>), and an empty answer; one that does not is
/// answered every report at once. The work is reported module by module to the <c>workDoneToken</c>.
/// </para>
/// <para>
/// A change to any document that arrives while the modules are being analyzed ends the pull, answered <c>ContentModified</c> (-32801): the change, and the
/// client's next request, do not wait for the whole workspace (<see cref="IncomingDocumentChanges.WhileWorkspaceCurrentAsync{T}"/>).
/// </para>
/// </remarks>
internal sealed class WorkspaceDiagnosticHandler(
    IWorkspaceDocumentService documents,
    IDocumentStateProvider states,
    IDocumentDiagnosticsService diagnostics,
    IWorkProgressReporter progress,
    IncomingDocumentChanges incoming,
    ILanguageServerFacade server) : WorkspaceDiagnosticHandlerBase
{
    public override async Task<WorkspaceDiagnosticReport> Handle(WorkspaceDiagnosticParams request, CancellationToken cancellationToken)
    {
        // a document is the same document however its URI was written: a client writes the drive and its colon as it likes, as LSP lets it.
        var previous = (request.PreviousResultIds ?? new Container<PreviousResultId>())
            .GroupBy(result => result.Uri)
            .ToDictionary(group => group.Key, group => group.Last().Value);

        using var work = progress.BeginFor(request, ProgressMessages.AnalyzingWorkspace);
        var partial = request.PartialResultToken;

        var reports = await incoming.WhileWorkspaceCurrentAsync(cancellationToken, async token =>
        {
            var modules = documents.GetAllDocuments().OrderBy(document => document.Id.Uri.ToString(), StringComparer.Ordinal).ToList();
            var answered = new List<WorkspaceDocumentDiagnosticReport>();
            for (var index = 0; index < modules.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var module = modules[index];
                work.Report(new WorkProgressStep(string.Format(ProgressMessages.ItemOf, module.Name, index + 1, modules.Count), 100 * index / modules.Count));

                var report = await ReportOfAsync(module, previous.GetValueOrDefault(module.Id.Uri), token);
                if (partial is not null)
                {
                    // written by the repaired serializer, which OmniSharp's progress manager does not write a partial result with (WorkspaceDiagnosticReports).
                    server.SendNotification(new ProgressParams { Token = partial, Value = WorkspaceDiagnosticReports.ToJson(WorkspaceDiagnosticReports.PartialOf([report])) });
                }
                else
                {
                    answered.Add(report);
                }
            }

            return answered;
        });

        return WorkspaceDiagnosticReports.Of(reports);
    }

    // a module's report, as its own pull answers it: unchanged since the result the client has, or every finding there is.
    private async Task<WorkspaceDocumentDiagnosticReport> ReportOfAsync(WorkspaceDocument module, string? previousResultId, CancellationToken token)
    {
        var result = await diagnostics.GetAsync(module.Id.Uri.ToUri(), previousResultId, token);
        var version = states.IsTracked(module.Id) && states.GetCurrentState(module.Id) is OpenedDocumentState ? module.Version : (int?)null;
        return result.Unchanged
            ? new WorkspaceUnchangedDocumentDiagnosticReport { Uri = module.Id.Uri, Version = version, ResultId = result.ResultId }
            : new WorkspaceFullDocumentDiagnosticReport { Uri = module.Id.Uri, Version = version, ResultId = result.ResultId, Items = new Container<Diagnostic>(result.Diagnostics) };
    }

    protected override DiagnosticsRegistrationOptions CreateRegistrationOptions(DiagnosticWorkspaceClientCapabilities capability, ClientCapabilities clientCapabilities)
        => DocumentDiagnosticHandler.PullDiagnostics;
}
