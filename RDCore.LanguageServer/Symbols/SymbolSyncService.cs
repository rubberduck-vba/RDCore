using Microsoft.Extensions.Logging;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
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

    /// <summary>
    /// Sends the symbols of one parsed module to the environment host.
    /// </summary>
    /// <remarks>
    /// For a module the client supplied rather than the workspace: it has no document, no cached
    /// parse, and no sibling modules to resolve declared type names against beyond the intrinsics.
    /// </remarks>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="parseResult">The parsed module.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>
    /// The module URI the symbols were defined under — derived here, from the workspace's own root, so
    /// that a caller cannot address the same module differently than the workspace sync does.
    /// </returns>
    Task<Uri> SyncModuleAsync(string moduleName, ModuleParseResult parseResult, CancellationToken token);
}

internal sealed class SymbolSyncService(
    IPlatformOrchestrationService orchestration,
    IParsingClientService parsing,
    IWorkspaceDocumentService documents,
    ISymbolResolver resolver,
    ILogger<SymbolSyncService> logger) : ISymbolSyncService
{
    public async Task<Uri> SyncModuleAsync(string moduleName, ModuleParseResult parseResult, CancellationToken token)
    {
        var host = orchestration.RuntimeEnvironment;
        await host.WaitForReadyAsync(token);

        var workspaceRoot = new Uri(documents.WorkspaceRoot);
        var moduleUri = new UriBuilder(workspaceRoot) { Fragment = moduleName }.Uri;
        var workspaceResolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot, [(moduleUri, ModuleType.StdModule, parseResult)], resolver);

        // a module the client keeps editing is defined again every time it is run, so the newest
        // definition has to win rather than being skipped as a duplicate.
        await DefineModuleSymbolsAsync(
            workspaceRoot, moduleUri, moduleName, ModuleType.StdModule, parseResult, workspaceResolver, replace: true, token);
        return moduleUri;
    }

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

            // collect every parsed module first: the resolver is composed over the whole workspace, so
            // one module's `As SomeType` can bind to a sibling module's Type / Enum declaration.
            Uri? workspaceRoot = null;
            var modules = new List<(Uri Uri, string Name, ModuleType Kind, ModuleParseResult Parse)>();
            foreach (var document in documents.GetAllDocuments())
            {
                token.ThrowIfCancellationRequested();

                if (!parsing.TryGetCached(document.Id.Uri.ToUri(), out var parseResult) || parseResult.SyntaxTree is null)
                {
                    continue;
                }

                workspaceRoot ??= new Uri(document.WorkspaceRoot);
                // the module's programmatic name is its Attribute VB_Name; the file name is the fallback.
                var moduleName = parseResult.SyntaxTree?.GetDeclaredName() ?? document.Name;
                // module kind is never a parser input — read it off the raw source, same as the parsing pass.
                var moduleKind = ParsingClientService.ModuleTypeOf(document);
                modules.Add((new UriBuilder(workspaceRoot) { Fragment = moduleName }.Uri, moduleName, moduleKind, parseResult));
            }

            if (workspaceRoot is null)
            {
                LogIfEnabled(LogLevel.Information, "Symbol sync found no parsed workspace modules.");
                return;
            }

            var workspaceResolver = WorkspaceSymbolResolver.Compose(
                workspaceRoot, modules.Select(module => (module.Uri, module.Kind, module.Parse)), resolver);

            var totalDefined = 0;
            foreach (var module in modules)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    totalDefined += await DefineModuleSymbolsAsync(workspaceRoot, module.Uri, module.Name, module.Kind, module.Parse, workspaceResolver, replace: false, token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // one module's failure (a malformed module, a projection bug) must not cost every
                    // other module its symbols for the rest of the session — this sync is one-shot,
                    // not re-run on didOpen/didChange, so an unguarded throw here used to make it
                    // permanent instead of degrading to "this one module didn't get defined."
                    logger.LogError(exception, "❌ Symbol sync failed for module {module}; continuing with the remaining workspace modules.", module.Name);
                }
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

    private async Task<int> DefineModuleSymbolsAsync(
        Uri workspaceRoot, Uri moduleUri, string moduleName, ModuleType moduleType, ModuleParseResult parseResult,
        ISymbolResolver workspaceResolver, bool replace, CancellationToken token)
    {
        var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, moduleType, parseResult, workspaceResolver).ProvideSymbols();
        var descriptors = SymbolDescriptorProjector.Project(symbols, moduleUri);

        var result = await orchestration.RuntimeEnvironment.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = moduleName,
                Symbols = descriptors,
                Replace = replace,
            }, token);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("📤 {module}: {defined} defined, {replaced} replaced, {skipped} skipped, {unresolved} unresolved type(s).",
                moduleName, result.Defined, result.Replaced, result.Skipped.Count, result.UnresolvedTypeNames.Count);
        }

        return result.Defined + result.Replaced;
    }

    private void LogIfEnabled(LogLevel level, string message)
    {
        if (logger.IsEnabled(level))
        {
            logger.Log(level, "{message}", message);
        }
    }
}
