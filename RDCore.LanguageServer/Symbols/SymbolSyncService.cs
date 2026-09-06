using Microsoft.Extensions.Logging;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Extracts the member symbols of every parsed workspace module and defines them in the environment
/// host over <c>rdcore/host/symbols/define</c>. Runs once after the workspace parse round-trip; a
/// failure is logged, not thrown.
/// </summary>
internal interface ISymbolSyncService
{
    /// <summary>
    /// Sends the symbols of every workspace module with a cached parse result to the environment host.
    /// </summary>
    Task SyncWorkspaceAsync(CancellationToken token);
}

internal sealed class SymbolSyncService(
    IPlatformOrchestrationService orchestration,
    IParsingClientService parsing,
    IWorkspaceDocumentService documents,
    ISymbolResolver resolver,
    ILogger<SymbolSyncService> logger) : ISymbolSyncService
{
    public async Task SyncWorkspaceAsync(CancellationToken token)
    {
        try
        {
            var host = orchestration.RuntimeEnvironment;
            await host.WaitForReadyAsync(token);

            if (host.PlatformInfo?.Provides<DefineSymbols>() != true)
            {
                LogIfEnabled(LogLevel.Warning,
                    "Environment host does not provide the DefineSymbols capability; workspace symbols will not be defined.");
                return;
            }

            var totalDefined = 0;
            foreach (var document in documents.GetAllDocuments())
            {
                token.ThrowIfCancellationRequested();

                var uri = document.Id.Uri.ToUri();
                if (!parsing.TryGetCached(uri, out var parseResult) || parseResult.SyntaxTree is null)
                {
                    continue;
                }

                totalDefined += await DefineModuleSymbolsAsync(document, parseResult, token);
            }

            LogIfEnabled(LogLevel.Information, $"✅ Workspace symbols defined in the environment host ({totalDefined} total)");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            LogIfEnabled(LogLevel.Information, "Symbol sync was cancelled; language server is shutting down.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "❌ Symbol sync failed.");
        }
    }

    private async Task<int> DefineModuleSymbolsAsync(WorkspaceDocument document, ModuleParseResult parseResult, CancellationToken token)
    {
        var workspaceRoot = new Uri(document.WorkspaceRoot);
        var moduleUri = new UriBuilder(workspaceRoot) { Fragment = document.Name }.Uri;

        var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, parseResult, resolver).ProvideSymbols();
        var descriptors = SymbolDescriptorProjector.Project(symbols, moduleUri);

        var result = await orchestration.RuntimeEnvironment.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = document.Name,
                Symbols = descriptors,
            }, token);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("📤 {module}: {defined} defined, {skipped} skipped, {unresolved} unresolved type(s).",
                document.Name, result.Defined, result.Skipped.Count, result.UnresolvedTypeNames.Count);
        }

        return result.Defined;
    }

    private void LogIfEnabled(LogLevel level, string message)
    {
        if (logger.IsEnabled(level))
        {
            logger.Log(level, "{message}", message);
        }
    }
}
