using Microsoft.Extensions.Logging;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.LanguageServer.Debugging;

/// <summary>
/// The findings of the modules the edits a program that waits does not have name, pushed again to a client that is pushed to when the list changes: a module
/// the list names now has an <c>RDC20001</c> it did not have, and one it named has one that is gone. Every module's findings are pushed again when a program
/// begins or ends waiting in break mode, which withdraws or brings back what comes of evaluating the code.
/// </summary>
/// <remarks>
/// A client that pulls is asked to pull again (<see cref="IEditStatusMonitor"/>); one that is pushed to is sent what it would pull. The edited module's own findings
/// are pushed by its refresh, which asks before it finds them out.
/// </remarks>
internal sealed class EditStatusPublication(
    IEditStatusMonitor editStatus,
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    IDocumentDiagnosticsService diagnostics,
    IDiagnosticsPublisher publisher,
    ILogger<EditStatusPublication> logger)
{
    private int _attached;

    /// <summary>Has each change of the list pushed to the client, from now on; once, however often it is asked.</summary>
    public void Attach()
    {
        if (Interlocked.Exchange(ref _attached, 1) == 0)
        {
            editStatus.Changed += Republish;
        }
    }

    private void Republish(HostDebugEditStatusResult previous, HostDebugEditStatusResult current)
    {
        if (!publisher.IsEnabled)
        {
            return;
        }

        // a program that begins or ends waiting in break mode changes the findings of every module: what comes of evaluating the code is withdrawn while it waits,
        // and is found out again once it no longer does.
        var everyModule = previous.Suspended != current.Suspended;
        var named = previous.Edits.Concat(current.Edits).Select(edit => edit.ModuleUri?.Fragment.TrimStart('#')).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents.GetAllDocuments())
        {
            var uri = document.Id.Uri.ToUri();
            if (everyModule)
            {
                _ = RepublishAsync(uri);
                continue;
            }

            var name = parsing.TryGetCached(uri, out var parse) ? parse.SyntaxTree?.GetDeclaredName() ?? document.Name : document.Name;
            if (named.Contains(name))
            {
                _ = RepublishAsync(uri);
            }
        }
    }

    private async Task RepublishAsync(Uri documentUri)
    {
        try
        {
            var result = await diagnostics.GetAsync(documentUri, previousResultId: null, CancellationToken.None);
            publisher.Publish(documentUri, documents.TryGetDocument(documentUri, out var document) ? document.Version : null, result.Diagnostics);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "❌ The findings of {uri} could not be published again.", documentUri);
        }
    }
}
