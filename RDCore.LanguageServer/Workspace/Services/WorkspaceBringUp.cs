using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Progress;
using RDCore.LanguageServer.Symbols;
using RDCore.SDK.Server.Progress;

namespace RDCore.LanguageServer.Workspace.Services;

/// <summary>
/// The workspace brought up as the server starts: every document parsed, then its symbols defined in the environment host - each part of it reported to the
/// client as work of the server's own.
/// </summary>
/// <remarks>
/// <para>
/// A client may ask what is found of a document before the workspace is loaded, and is answered then: what a module names of another module that is not
/// parsed yet is not known. Once the workspace is loaded, the client is asked to refresh what it shows, as it said it is asked to (<see cref="IWorkspaceRefresh"/>),
/// and a pull that names the result it had is answered anew.
/// </para>
/// <para>
/// What the client supplies the host - a program it runs, a document it opened - waits for the workspace's modules to be defined there
/// (<see cref="ISymbolSyncService.ExpectWorkspace"/>), whose scope the server begins as it initializes, before the client can send anything else; the bring-up
/// ends it once the workspace is synced, or once it fails.
/// </para>
/// </remarks>
internal static class WorkspaceBringUp
{
    /// <summary>Parses every document of the workspace, defines its symbols in the environment host, and has what the client shows refreshed.</summary>
    /// <param name="parsing">Parses the documents.</param>
    /// <param name="symbols">Defines the symbols in the environment host.</param>
    /// <param name="refresh">What the client shows of each document, refreshed.</param>
    /// <param name="progress">Where each part of the work is reported.</param>
    /// <param name="expected">The scope of the bring-up the client's modules wait for (<see cref="ISymbolSyncService.ExpectWorkspace"/>): ended once the
    /// workspace is synced, however its sync ends.</param>
    /// <param name="token">A token that cancels the bring-up.</param>
    public static async Task LoadAsync(IParsingClientService parsing, ISymbolSyncService symbols, IWorkspaceRefresh refresh, IWorkProgressReporter progress,
        IDisposable? expected, CancellationToken token)
    {
        try
        {
            using (var parsingWork = progress.Begin(ProgressMessages.ParsingWorkspace))
            {
                await parsing.ParseWorkspaceAsync(token, parsingWork);
            }

            using (var composingWork = progress.Begin(ProgressMessages.ComposingProject))
            {
                await symbols.SyncWorkspaceAsync(token, composingWork);
            }
        }
        finally
        {
            expected?.Dispose();
        }

        // what was found of a document before every module was parsed and defined is not what is found now; a server that is stopping asks nothing.
        if (!token.IsCancellationRequested)
        {
            refresh.Changed();
        }
    }
}
