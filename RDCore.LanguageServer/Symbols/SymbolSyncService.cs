using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Progress;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Progress;
using RDCore.SDK.Model.Symbols;
using RDCore.Symbols;

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
    /// <param name="token">A token that cancels the sync.</param>
    /// <param name="progress">Where each module is reported as it is defined, and then as its code is loaded.</param>
    /// <param name="replace">Whether a module the host has already been sent is defined again, which a workspace that was loaded again asks for: a module it
    /// gained can declare the type another one names. The first sync of a workspace defines each module once.</param>
    Task SyncWorkspaceAsync(CancellationToken token, IProgress<WorkProgressStep>? progress = null, bool replace = false);

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

    /// <summary>
    /// Has the environment host load the code of a module whose symbols it has been sent (<see cref="SyncModuleAsync"/>), so that the semantic analysis pass runs
    /// over it and the host has a model to answer for it.
    /// </summary>
    /// <remarks>
    /// Loading is not running: nothing is executed, which is what a client that wants a module analyzed asks for. The errors that stop a module from loading are the
    /// host's to report through its model.
    /// </remarks>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="parseResult">The parsed module.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task LoadModuleCodeAsync(string moduleName, ModuleParseResult parseResult, CancellationToken token);

    /// <summary>
    /// Brings the environment host up to date with a workspace document that changed: its symbols are defined again, in the workspace as it is now, and its code is
    /// loaded again, so that the host's model of the module is the one of the text the document has.
    /// </summary>
    /// <remarks>
    /// Does nothing when the server has no parse of the version of the text the document has now: what is derived from other text is not an improvement.
    /// The modules that refer to what changed are not loaded again: they are as they were until they are touched, or the workspace is synced.
    /// </remarks>
    /// <param name="documentUri">The address of the document.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task SyncDocumentAsync(Uri documentUri, CancellationToken token);

    /// <summary>
    /// Takes a module out of the environment host: what it declared, what it held, and the code and the model that were made of it.
    /// </summary>
    /// <remarks>
    /// For a module that is gone: a document the client closed that the workspace has no claim on, or a program the client cleared. The module is addressed
    /// the way it was when it was defined, which is the point of deriving the address here and nowhere else.
    /// </remarks>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>What the host took out; nothing when the host cannot be told.</returns>
    /// <param name="endProgram">Whether the program that is running or waits is ended too, and the session wiped: what clearing a program asks (<c>NEW</c>, <c>LOAD</c>).</param>
    Task<DiscardSessionResult> DiscardModuleAsync(string moduleName, CancellationToken token, bool endProgram = false);

    /// <summary>
    /// Keeps every other sync from the environment host until the hold is disposed, so that what the caller sends and then asks about is what the host holds
    /// when it answers.
    /// </summary>
    /// <remarks>
    /// The host holds one module by a name. A client's own text of a module (<see cref="SyncModuleAsync"/>, <see cref="LoadModuleCodeAsync"/>) and the workspace's
    /// text of the same module (<see cref="SyncWorkspaceAsync"/>, <see cref="SyncDocumentAsync"/>) are sent apart, step by step; without a hold, a step of one can
    /// land between the steps of the other, and the model the host answers with is of the other text. The syncs that send the workspace's text take the same hold
    /// themselves. A hold is not reentrant: what is done under one calls none of those that take it.
    /// </remarks>
    /// <param name="token">A token that cancels the wait for the hold.</param>
    /// <returns>The hold, which is released when it is disposed.</returns>
    Task<IAsyncDisposable> HoldAsync(CancellationToken token);
}

internal sealed class SymbolSyncService(
    IPlatformOrchestrationService orchestration,
    IParsingClientService parsing,
    IWorkspaceDocumentService documents,
    ISymbolResolver resolver,
    IOptions<SdkAppOptions> options,
    ILogger<SymbolSyncService> logger,
    IReferencedLibraryService referencedLibraries) : ISymbolSyncService
{
    // where the variable an undeclared name declares lives is the environment's to say, and every extraction pass
    // - the resolver's own two, and the one that defines the symbols - has to agree on it.
    private ImplicitDeclarationScope ImplicitScope => options.Value.Workspace.SupportedLanguage.ImplicitDeclarationScope;

    // one at a time: what goes to the host to change what it holds, and what is asked of it in the same breath.
    private readonly SemaphoreSlim _hold = new(1, 1);

    public async Task<IAsyncDisposable> HoldAsync(CancellationToken token)
    {
        await _hold.WaitAsync(token).ConfigureAwait(false);
        return new Hold(_hold);
    }

    private sealed class Hold(SemaphoreSlim held) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                held.Release();
            }

            return ValueTask.CompletedTask;
        }
    }

    public async Task<Uri> SyncModuleAsync(string moduleName, ModuleParseResult parseResult, CancellationToken token)
    {
        var host = orchestration.RuntimeEnvironment;
        await host.WaitForReadyAsync(token);

        var workspaceRoot = new Uri(documents.WorkspaceRoot);
        var moduleUri = SymbolUri.Module(workspaceRoot, moduleName);

        // the module is one of the project's, and its names are bound among the others' as a module of the workspace binds its own: a shell's program calls a
        // Sub of a module of the workspace, qualified or not, and a name of one is no variable the program declares by naming it. What the project has of a
        // module of the same name is the module as it was: this is the one that is synchronized.
        var (_, others) = CollectModules(token);
        var workspaceResolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot,
            [
                .. others.Where(other => !string.Equals(other.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                    .Select(other => (SymbolUri.Module(workspaceRoot, other.Name), other.Kind, other.Parse)),
                (moduleUri, ModuleType.StdModule, parseResult),
            ],
            resolver, projectName: referencedLibraries.ProjectName, implicitScope: ImplicitScope, libraries: referencedLibraries.SymbolsUnder(workspaceRoot));

        // a module the client keeps editing is defined again every time it is run, so the newest
        // definition has to win rather than being skipped as a duplicate. Its parse result travels with the request to
        // run it, not with this one.
        await DefineModuleSymbolsAsync(
            workspaceRoot, moduleUri, moduleName, ModuleType.StdModule, parseResult, workspaceResolver, replace: true,
            withCode: false, token);
        return moduleUri;
    }

    public async Task SyncWorkspaceAsync(CancellationToken token, IProgress<WorkProgressStep>? progress = null, bool replace = false)
    {
        try
        {
            var host = orchestration.RuntimeEnvironment;
            await host.WaitForReadyAsync(token);

            // every module is defined before the code of any is loaded: nothing else is sent to the host in between, or what it would send is overwritten.
            await using var hold = await HoldAsync(token);

            if (host.PlatformInfo?.Provides<DefineSymbols>() != true)
            {
                LogIfEnabled(LogLevel.Warning,
                    "Environment host does not provide the DefineSymbols capability; workspace symbols will not be defined.");
                return;
            }

            // collect every parsed module first: the resolver is composed over the whole workspace, so
            // one module's `As SomeType` can bind to a sibling module's Type / Enum declaration.
            var (workspaceRoot, modules) = CollectModules(token);

            if (workspaceRoot is null)
            {
                LogIfEnabled(LogLevel.Information, "Symbol sync found no parsed workspace modules.");
                return;
            }

            var workspaceResolver = WorkspaceSymbolResolver.Compose(
                workspaceRoot, modules.Select(module => (module.Uri, module.Kind, module.Parse)), resolver, projectName: referencedLibraries.ProjectName,
                implicitScope: ImplicitScope, libraries: referencedLibraries.SymbolsUnder(workspaceRoot));

            // three phases, each for every module before the next (DefineSymbolsParams.DeclareTypesOnly): its types are declared, its declarations are bound,
            // and its code is loaded - a third of the work each.
            var totalDefined = 0;
            var step = 0;
            void Step(string message, string module) => progress?.Report(new WorkProgressStep(
                string.Format(ProgressMessages.ItemOf, string.Format(message, module), step % modules.Count + 1, modules.Count), 100 * step++ / (3 * modules.Count)));

            // every type exists before any declaration is bound to it, whichever module declares it and in whichever order the project lists them.
            foreach (var module in modules)
            {
                token.ThrowIfCancellationRequested();
                Step(ProgressMessages.Declaring, module.Name);
                try
                {
                    totalDefined += await DefineModuleSymbolsAsync(
                        workspaceRoot, module.Uri, module.Name, module.Kind, module.Parse, workspaceResolver, replace, withCode: false, token, declareTypesOnly: true);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "❌ Declaring the types of module {module} failed; continuing with the remaining workspace modules.", module.Name);
                }
            }

            foreach (var module in modules)
            {
                token.ThrowIfCancellationRequested();
                Step(ProgressMessages.Defining, module.Name);
                try
                {
                    totalDefined += await DefineModuleSymbolsAsync(workspaceRoot, module.Uri, module.Name, module.Kind, module.Parse, workspaceResolver, replace, withCode: false, token);
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

            // the code of a module is checked against everything the workspace declares, which a module that names one defined after it cannot be until
            // every module is: so the code is sent once all of them are defined.
            foreach (var module in modules)
            {
                token.ThrowIfCancellationRequested();
                Step(ProgressMessages.Loading, module.Name);
                try
                {
                    await SendModuleCodeAsync(workspaceRoot, module.Uri, module.Name, module.Parse, token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "❌ Loading the code of module {module} failed; continuing with the remaining workspace modules.", module.Name);
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
        ISymbolResolver workspaceResolver, bool replace, bool withCode, CancellationToken token, bool declareTypesOnly = false)
    {
        var symbols = new SyntaxTreeSymbolProvider(
            workspaceRoot, moduleUri, moduleType, parseResult, workspaceResolver,
            withImplicitDeclarations: true, ImplicitScope).ProvideSymbols();
        var descriptors = SymbolDescriptorProjector.Project(symbols, moduleUri);

        // a module that declares no type has nothing to declare in the first phase.
        if (declareTypesOnly && !descriptors.Any(descriptor => descriptor.Kind is SymbolDescriptorKind.UserDefinedType or SymbolDescriptorKind.Enum))
        {
            return 0;
        }

        var result = await orchestration.RuntimeEnvironment.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = moduleName,
                Symbols = descriptors,
                Directives = parseResult.SyntaxTree.GetModuleDirectives(),
                ImplementedInterfaceNames = parseResult.SyntaxTree?.GetImplementedInterfaceNames() ?? [],
                ImplementedInterfaceRanges = parseResult.SyntaxTree?.GetImplementedInterfaceRanges() ?? [],
                ParseResultJson = withCode ? PlatformJson.Serialize(parseResult) : string.Empty,
                PrecompilerTriviaJson = DefineSymbolsParams.PrecompilerTriviaJsonOf(parseResult),
                Replace = replace,
                DeclareTypesOnly = declareTypesOnly,
            }, token);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("📤 {module}: {defined} defined, {replaced} replaced, {skipped} skipped, {unresolved} unresolved type(s).",
                moduleName, result.Defined, result.Replaced, result.Skipped.Count, result.UnresolvedTypeNames.Count);
        }

        return result.Defined + result.Replaced;
    }

    // every module of the primary project that has a parse: the resolver is composed over the whole project, so one module's `As SomeType` can bind to a sibling
    // module's Type / Enum declaration. The environment host runs that project and no other: a module of another project is analyzed by the language server,
    // and a `Module1` of it is never defined under the primary project's.
    private (Uri? WorkspaceRoot, List<(Uri Uri, string Name, ModuleType Kind, ModuleParseResult Parse)> Modules) CollectModules(CancellationToken token)
    {
        Uri? workspaceRoot = null;
        var modules = new List<(Uri Uri, string Name, ModuleType Kind, ModuleParseResult Parse)>();
        foreach (var document in documents.GetProjectDocuments(documents.WorkspaceRoot))
        {
            token.ThrowIfCancellationRequested();

            // a document of a server with no workspace is a project of its own, of which the host has no session.
            if (document.WorkspaceRoot.Length == 0 || !parsing.TryGetCached(document.Id.Uri.ToUri(), out var parseResult) || parseResult.SyntaxTree is null)
            {
                continue;
            }

            workspaceRoot ??= SymbolUri.Root(document.WorkspaceRoot);
            // the module's programmatic name is its Attribute VB_Name; the file name is the fallback.
            var moduleName = parseResult.SyntaxTree?.GetDeclaredName() ?? document.Name;
            // module kind is never a parser input — read it off the raw source, same as the parsing pass.
            var moduleKind = ParsingClientService.ModuleTypeOf(document);
            modules.Add((SymbolUri.Module(workspaceRoot, moduleName), moduleName, moduleKind, parseResult));
        }

        return (workspaceRoot, modules);
    }

    public async Task SyncDocumentAsync(Uri documentUri, CancellationToken token)
    {
        var host = orchestration.RuntimeEnvironment;
        await host.WaitForReadyAsync(token);
        await using var hold = await HoldAsync(token);

        if (host.PlatformInfo?.Provides<DefineSymbols>() != true
            || !documents.TryGetDocument(documentUri, out var document)
            || !parsing.TryGetCached(documentUri, document.Version, out var parseResult) || parseResult.SyntaxTree is null)
        {
            // the host cannot be told, or what the server has of the document is not a parse of the text it has now: nothing is better than what is stale.
            return;
        }

        var (workspaceRoot, modules) = CollectModules(token);
        var moduleName = parseResult.SyntaxTree.GetDeclaredName() ?? document.Name;
        if (workspaceRoot is null || modules.FirstOrDefault(module => module.Name == moduleName) is not { Parse: not null } changed)
        {
            return;
        }

        // the module is defined again with the newest definition winning, in the workspace as it is now - what the other modules declare is what its names are
        // bound against - and then its code is loaded, which is checked against the same.
        var workspaceResolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot, modules.Select(module => (module.Uri, module.Kind, module.Parse)), resolver, projectName: referencedLibraries.ProjectName,
            implicitScope: ImplicitScope, libraries: referencedLibraries.SymbolsUnder(workspaceRoot));
        await DefineModuleSymbolsAsync(workspaceRoot, changed.Uri, changed.Name, changed.Kind, changed.Parse, workspaceResolver, replace: true, withCode: false, token);
        await SendModuleCodeAsync(workspaceRoot, changed.Uri, changed.Name, changed.Parse, token);
    }

    public async Task<DiscardSessionResult> DiscardModuleAsync(string moduleName, CancellationToken token, bool endProgram = false)
    {
        var host = orchestration.RuntimeEnvironment;
        if (host is null)
        {
            return new DiscardSessionResult();
        }

        await host.WaitForReadyAsync(token);
        await using var hold = await HoldAsync(token);
        if (host.PlatformInfo?.Provides<SessionDiscard>() != true)
        {
            // the host cannot be told: what it holds stays, as it did before there was a way to say otherwise.
            return new DiscardSessionResult();
        }

        var moduleUri = SymbolUri.Module(documents.WorkspaceRoot, moduleName);
        var result = await host.SendRequestAsync<HostDiscardParams, DiscardSessionResult>(
            new HostDiscardParams { ModuleUri = moduleUri, ModuleName = moduleName, EndProgram = endProgram }, token);

        LogIfEnabled(LogLevel.Information, $"🗑️ {moduleName}: {result.Discarded} symbol(s) discarded.");
        return result;
    }

    public Task LoadModuleCodeAsync(string moduleName, ModuleParseResult parseResult, CancellationToken token)
    {
        var workspaceRoot = new Uri(documents.WorkspaceRoot);
        return SendModuleCodeAsync(workspaceRoot, SymbolUri.Module(workspaceRoot, moduleName), moduleName, parseResult, token);
    }

    // the module's symbols are defined: the host composes it and loads its code, and says what is wrong with the code if it is not loaded.
    private async Task SendModuleCodeAsync(Uri workspaceRoot, Uri moduleUri, string moduleName, ModuleParseResult parseResult, CancellationToken token)
    {
        var result = await orchestration.RuntimeEnvironment.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = moduleName,
                Directives = parseResult.SyntaxTree.GetModuleDirectives(),
                ImplementedInterfaceNames = parseResult.SyntaxTree?.GetImplementedInterfaceNames() ?? [],
                ImplementedInterfaceRanges = parseResult.SyntaxTree?.GetImplementedInterfaceRanges() ?? [],
                ParseResultJson = PlatformJson.Serialize(parseResult),
                CodeOnly = true,
            }, token);

        foreach (var error in result.CodeErrors)
        {
            logger.LogWarning("📤 {module} was not loaded: {error}", moduleName, error);
        }
    }

    private void LogIfEnabled(LogLevel level, string message)
    {
        if (logger.IsEnabled(level))
        {
            logger.Log(level, "{message}", message);
        }
    }
}
