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
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.SDK.Platform.Components;

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
    /// Says that the host's session was composed again - the workspace was loaded into it again - and holds nothing it was sent: what each module's code load
    /// fails with is reported again by the next sync, as it is by the first sync of a session.
    /// </summary>
    void SessionComposedAnew();

    /// <summary>
    /// The modules the last <see cref="SyncWorkspaceAsync"/> did not define or load, by name, and why: none when it defined and loaded each one. A sync that was
    /// cancelled did not load the modules it had not reached.
    /// </summary>
    /// <remarks>
    /// A sync goes on with the other modules when one fails, and the host has the failed one as it was: the code of a program that waits on it is not the code
    /// of its document.
    /// </remarks>
    IReadOnlyDictionary<string, string> Unsynced { get; }

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
    /// <param name="sessionModule">Whether the module is the client's own, of the session and not of the project (<see cref="DefineSymbolsParams.SessionModule"/>).</param>
    /// <returns>
    /// The module URI the symbols were defined under — derived here, from the workspace's own root, so
    /// that a caller cannot address the same module differently than the workspace sync does.
    /// </returns>
    Task<Uri> SyncModuleAsync(string moduleName, ModuleParseResult parseResult, CancellationToken token, bool sessionModule = false);

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
    Task<DiscardResult> DiscardModuleAsync(string moduleName, CancellationToken token, bool endProgram = false);

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

    /// <summary>
    /// Says that the workspace is being brought up: until the scope is disposed, what the client supplies - a module it runs or has analyzed
    /// (<see cref="SyncModuleAsync"/>), a document it opened or edited (<see cref="SyncDocumentAsync"/>), a hold (<see cref="HoldAsync"/>) - waits, and is sent
    /// once the workspace's own modules are defined in the host (<see cref="SyncWorkspaceAsync"/>).
    /// </summary>
    /// <remarks>
    /// A module the host is given before the modules of the workspace are has its names bound without them: a shell's program that calls <c>Module1.Helper</c>
    /// declares <c>Module1</c> a variable of its own, where implicit declarations are allowed, and the program fails with error 424 when it calls through it -
    /// however the workspace is synced afterwards, since the first sync of a workspace does not define again a module the host has.
    /// </remarks>
    /// <returns>The scope of the bring-up: disposed once the workspace is synced, or once the bring-up ends without it, which lets what waits go.</returns>
    IDisposable ExpectWorkspace();

    /// <summary>
    /// Waits for the workspace's modules to be defined in the host: until the scope of the bring-up (<see cref="ExpectWorkspace"/>) is disposed, or at once when
    /// no bring-up is under way.
    /// </summary>
    /// <param name="token">A token that cancels the wait.</param>
    /// <returns>A task that completes once the bring-up has ended, however it ended.</returns>
    Task WorkspaceDefinedAsync(CancellationToken token);
}

internal sealed class SymbolSyncService(
    IPlatformOrchestrationService orchestration,
    IParsingClientService parsing,
    IWorkspaceDocumentService documents,
    ISymbolResolver resolver,
    IOptions<SdkAppOptions> options,
    ILogger<SymbolSyncService> logger,
    IReferencedLibraryService referencedLibraries,
    IWorkspaceProjects projects,
    IReferencedProjectService? referencedProjects = null,
    ModuleLiveBranches? liveBranches = null) : ISymbolSyncService
{
    // what the primary project's names are bound among: the libraries it references, and the projects of the workspace it references.
    private IReadOnlyList<Symbol> PrimaryLibraries(Uri workspaceRoot, CancellationToken token)
        => referencedProjects?.LibrariesOf(workspaceRoot, documents.WorkspaceRoot, token) ?? referencedLibraries.SymbolsUnder(workspaceRoot);

    // the other projects the host analyzes, by their root, as each was when its analysis session was composed: one that changed is composed again. Of the host
    // they were sent to: a host started again has none.
    private readonly Dictionary<Uri, ProjectFile> _analyzed = new(SymbolUri.RootComparer);
    private IRDCoreClientApp? _analysisHost;

    // where the variable an undeclared name declares lives is the environment's to say, and every extraction pass
    // - the resolver's own two, and the one that defines the symbols - has to agree on it.
    private ImplicitDeclarationScope ImplicitScope => options.Value.Workspace.SupportedLanguage.ImplicitDeclarationScope;

    // one at a time: what goes to the host to change what it holds, and what is asked of it in the same breath.
    private readonly SemaphoreSlim _hold = new(1, 1);

    private IReadOnlyDictionary<string, string> _unsynced = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Unsynced => Volatile.Read(ref _unsynced);

    // the workspace as it is brought up: what the client supplies waits for its modules to be defined, before it takes the hold - the sync of the workspace
    // takes the hold too, and a caller that held it while it waited would keep the workspace from being synced.
    private TaskCompletionSource _workspaceDefined = Defined();

    private static TaskCompletionSource Defined()
    {
        var defined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        defined.SetResult();
        return defined;
    }

    public IDisposable ExpectWorkspace()
    {
        var expected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _workspaceDefined, expected);
        return new Expectation(expected);
    }

    private sealed class Expectation(TaskCompletionSource expected) : IDisposable
    {
        public void Dispose() => expected.TrySetResult();
    }

    public Task WorkspaceDefinedAsync(CancellationToken token) => Volatile.Read(ref _workspaceDefined).Task.WaitAsync(token);

    public async Task<IAsyncDisposable> HoldAsync(CancellationToken token)
    {
        await WorkspaceDefinedAsync(token).ConfigureAwait(false);
        return await TakeHoldAsync(token).ConfigureAwait(false);
    }

    // the hold, whatever is being brought up: the sync that brings the workspace up takes it so.
    private async Task<IAsyncDisposable> TakeHoldAsync(CancellationToken token)
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

    public async Task<Uri> SyncModuleAsync(string moduleName, ModuleParseResult parseResult, CancellationToken token, bool sessionModule = false)
    {
        // what the client supplies is bound among the workspace's modules, which the host is given first. A caller that holds the hold waited for them already.
        await WorkspaceDefinedAsync(token);
        var host = orchestration.RuntimeEnvironment;
        await host.WaitForReadyAsync(token);

        var workspaceRoot = SymbolUri.Root(documents.WorkspaceRoot);
        var moduleUri = SymbolUri.Module(workspaceRoot, moduleName);

        // the module is one of the project's, and its names are bound among the others' as a module of the workspace binds its own: a shell's program calls a
        // Sub of a module of the workspace, qualified or not, and a name of one is no variable the program declares by naming it. What the project has of a
        // module of the same name is the module as it was: this is the one that is synchronized.
        var (_, others, hosts) = CollectModules(token);
        var workspaceResolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot,
            [
                .. others.Where(other => !string.Equals(other.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                    .Select(other => (SymbolUri.Module(workspaceRoot, other.Name), other.Kind, other.Parse)),
                (moduleUri, ModuleType.StdModule, parseResult),
            ],
            resolver, projectName: referencedLibraries.ProjectName, implicitScope: ImplicitScope, libraries: PrimaryLibraries(workspaceRoot, token),
            documentHosts: hosts);

        // a module the client keeps editing is defined again every time it is run, so the newest
        // definition has to win rather than being skipped as a duplicate. Its parse result travels with the request to
        // run it, not with this one.
        var module = new ParsedModule(moduleUri, moduleName, ModuleType.StdModule, parseResult);
        var definition = new SessionDefinition(SessionDefinitionPhase.Declare, workspaceRoot, module, workspaceResolver, ImplicitScope, Replace: true);
        await DefineModuleSymbolsAsync(definition, token, sessionModule);
        return moduleUri;
    }

    public async Task SyncWorkspaceAsync(CancellationToken token, IProgress<WorkProgressStep>? progress = null, bool replace = false)
    {
        // what this sync did not define or load, and the modules whose code it loaded: those it did not reach, when it is cancelled, are not loaded either.
        var failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> collected = [];
        try
        {
            var host = orchestration.RuntimeEnvironment;
            await host.WaitForReadyAsync(token);

            // every module is defined before the code of any is loaded: nothing else is sent to the host in between, or what it would send is overwritten. The
            // workspace's own sync is what the client's modules wait for: it does not wait for itself.
            await using var hold = await TakeHoldAsync(token);

            // the first sync of a session - the workspace's load into it, or a host started again - reports again what each module's load fails with. A sync an
            // edit asks for sends the same modules to the same session once more, and a failure it was told of already is not news.
            if (!replace || !ReferenceEquals(host, _loadFailuresHost))
            {
                _loadFailures = new ModuleLoadFailures();
                _loadFailuresHost = host;
            }

            if (host.PlatformInfo?.Provides<DefineSymbols>() != true)
            {
                LogIfEnabled(LogLevel.Warning,
                    "Environment host does not provide the DefineSymbols capability; workspace symbols will not be defined.");
                return;
            }

            // collect every parsed module first: the resolver is composed over the whole workspace, so
            // one module's `As SomeType` can bind to a sibling module's Type / Enum declaration.
            var (workspaceRoot, modules, hosts) = CollectModules(token);
            collected = [.. modules.Select(module => module.Name)];

            // the modules of a name that another module of the project has are none of the program's (MS-VBAL §4.2): a run of it is refused, and says why.
            foreach (var (name, error) in ProjectModules.NameConflicts(documents, parsing, documents.WorkspaceRoot, token).Values)
            {
                failed.TryAdd(name, $"{error.Description}: {error.Verbose}");
            }

            if (workspaceRoot is null)
            {
                LogIfEnabled(LogLevel.Information, "Symbol sync found no parsed workspace modules.");
                await SyncOtherProjectsAsync(host, token);
                return;
            }

            var workspaceResolver = WorkspaceSymbolResolver.Compose(
                workspaceRoot, modules.Select(module => (module.Uri, module.Kind, module.Parse)), resolver, projectName: referencedLibraries.ProjectName,
                implicitScope: ImplicitScope, libraries: PrimaryLibraries(workspaceRoot, token), documentHosts: hosts);

            // a module the workspace no longer has - a block taken out of its file, a module renamed, while the file changed outside the editor - is taken out
            // of the host before the others are bound again.
            await DiscardGoneAsync(host, modules, document: null, token);
            Defined(modules);

            // three phases, each for every module before the next (DefineSymbolsParams.DeclareTypesOnly): its types are declared, its declarations are bound,
            // and its code is loaded - a third of the work each.
            var totalDefined = 0;
            var step = 0;
            void Step(string message, string module) => progress?.Report(new WorkProgressStep(
                string.Format(ProgressMessages.ItemOf, string.Format(message, module), step % modules.Count + 1, modules.Count), 100 * step++ / (3 * modules.Count)));

            // the session is sent its definitions in the order every session is (SessionDefinitions): the projects the project references, and its own.
            foreach (var definition in SessionDefinitions.Of(workspaceRoot, modules, workspaceResolver, ImplicitScope, replace, ReferencedProjects(token)))
            {
                token.ThrowIfCancellationRequested();
                var module = definition.Module;
                if (definition.Referenced is null)
                {
                    Step(definition.Phase switch
                    {
                        SessionDefinitionPhase.DeclareTypes => ProgressMessages.Declaring,
                        SessionDefinitionPhase.Declare => ProgressMessages.Defining,
                        _ => ProgressMessages.Loading,
                    }, module.Name);
                }

                try
                {
                    if (definition.Phase is SessionDefinitionPhase.Code)
                    {
                        await SendModuleCodeAsync(definition, token);
                        if (definition.Referenced is null)
                        {
                            loaded.Add(module.Name);
                        }
                    }
                    else
                    {
                        totalDefined += await DefineModuleSymbolsAsync(definition, token);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // one module's failure (a malformed module, a projection bug) costs no other module its symbols or its code. What fails of a referenced
                    // project is said, and is not of Unsynced, which lists the modules of the primary project.
                    if (definition.Referenced is { } project)
                    {
                        logger.LogError(exception, "❌ {phase} of module {module} of the referenced project {project} failed; continuing.",
                            definition.Phase, module.Name, project.Name);
                    }
                    else
                    {
                        logger.LogError(exception, "❌ {phase} of module {module} failed; continuing with the remaining workspace modules.",
                            definition.Phase, module.Name);
                        failed.TryAdd(module.Name, exception.Message);
                    }
                }
            }

            LogIfEnabled(LogLevel.Information, $"✅ Workspace symbols defined in the environment host ({totalDefined} total)");
            await SyncOtherProjectsAsync(host, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            LogIfEnabled(LogLevel.Information, "Symbol sync was cancelled.");
            Unfinished(failed, loaded, collected, WorkspaceMessages.SyncCancelled);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "❌ Symbol sync failed.");
            Unfinished(failed, loaded, collected, exception.Message);
        }
        finally
        {
            Volatile.Write(ref _unsynced, failed);
        }
    }

    // the other projects of the workspace are analyzed by the host, each in a session of its own that is never run (rdcore/host/project/analyze): a project is
    // its own namespace, and its modules are bound among its own and the libraries it references, as the host binds them in that session. A project that left
    // the workspace is let go of. Nothing of them is a module the program runs, so what fails is said and is not of Unsynced.
    private async Task SyncOtherProjectsAsync(IRDCoreClientApp host, CancellationToken token)
    {
        if (host.PlatformInfo?.Provides<ProjectAnalysis>() != true)
        {
            return;
        }

        if (!ReferenceEquals(host, _analysisHost))
        {
            _analyzed.Clear();
            _analysisHost = host;
        }

        var others = OtherProjects();
        foreach (var gone in _analyzed.Keys.Where(root => !others.Any(other => SymbolUri.SameRoot(SymbolUri.Root(other.Root), root))).ToList())
        {
            _analyzed.Remove(gone);
            await host.SendRequestAsync<HostProjectReleaseParams, HostProjectReleaseResult>(new HostProjectReleaseParams { WorkspaceRoot = gone }, token);
        }

        foreach (var other in others)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await SyncOtherProjectAsync(host, other, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "❌ The project at {root} could not be analyzed; continuing with the other projects.", other.Root);
            }
        }
    }

    // the projects of the workspace other than the primary one.
    private List<WorkspaceProject> OtherProjects()
        => [.. projects.Projects.Where(project => !WorkspacePaths.Same(project.Root, documents.WorkspaceRoot))];

    // the project's session is composed again when the project is not what it was, and holds none of what it held; otherwise its modules are defined again in
    // it, the newest definition winning, in the three phases a workspace is.
    private async Task SyncOtherProjectAsync(IRDCoreClientApp host, WorkspaceProject other, CancellationToken token)
    {
        var root = SymbolUri.Root(other.Root);
        var composed = !_analyzed.TryGetValue(root, out var analyzed) || !ReferenceEquals(analyzed, other.Project);
        if (composed)
        {
            _analyzed.Remove(root);
            var result = await host.SendRequestAsync<HostProjectAnalyzeParams, HostProjectAnalyzeResult>(new HostProjectAnalyzeParams { WorkspaceRoot = root }, token);
            if (!result.Composed)
            {
                return;
            }

            _analyzed[root] = other.Project;
        }

        var (_, modules, hosts) = CollectModules(other.Root, root, token);
        foreach (var definition in SessionDefinitions.Of(root, modules, ResolverOf(root, other, modules, hosts), ImplicitScope, replace: !composed))
        {
            await (definition.Phase is SessionDefinitionPhase.Code ? SendModuleCodeAsync(definition, token) : DefineModuleSymbolsAsync(definition, token));
        }

        LogIfEnabled(LogLevel.Information, $"🔍 {other.Project.ProjectInfo.Name}: {modules.Count} module(s) defined in its analysis session.");
    }

    // what the names of another project's modules are bound against: its own modules, the libraries it references, and its own name.
    private ISymbolResolver ResolverOf(Uri root, WorkspaceProject other, List<ParsedModule> modules, Dictionary<string, DocClassType> hosts)
        => WorkspaceSymbolResolver.Compose(
            root, modules.Select(module => (module.Uri, module.Kind, module.Parse)), resolver,
            projectName: other.Project.ProjectInfo.Name is { Length: > 0 } name ? name : null, implicitScope: ImplicitScope,
            libraries: referencedProjects?.LibrariesOf(root, other.Root) ?? referencedLibraries.SymbolsOf(root, other.Project.ProjectInfo),
            documentHosts: hosts);

    // the projects the primary project references, each with its modules and what their names are bound against, as its own analysis binds them: they are
    // defined in its session as names of those projects (DefineSymbolsParams.Library), beside its own modules.
    private List<ReferencedProjectModules> ReferencedProjects(CancellationToken token)
    {
        var referenced = new List<ReferencedProjectModules>();
        foreach (var project in referencedProjects?.ProjectsReferencedBy(documents.WorkspaceRoot) ?? [])
        {
            var root = SymbolUri.Root(project.Project.Root);
            var (_, modules, hosts) = CollectModules(project.Project.Root, root, token);
            referenced.Add(new ReferencedProjectModules(project.Name, project.Priority, root, modules, ResolverOf(root, project.Project, modules, hosts)));
        }

        return referenced;
    }

    // a sync that did not finish loaded only some of what it collected, and may not have reached the collecting at all: what the last sync did not load is not
    // loaded either, unless this one loaded it.
    private void Unfinished(Dictionary<string, string> failed, HashSet<string> loaded, IReadOnlyList<string> collected, string reason)
    {
        foreach (var name in collected.Where(name => !loaded.Contains(name)))
        {
            failed.TryAdd(name, reason);
        }

        foreach (var (name, why) in Unsynced.Where(module => !loaded.Contains(module.Key)))
        {
            failed.TryAdd(name, why);
        }
    }

    // the module's symbols are defined, as the definition says them; a module that declares no type has nothing to declare in the first phase.
    private async Task<int> DefineModuleSymbolsAsync(SessionDefinition definition, CancellationToken token, bool sessionModule = false)
    {
        if (definition.ToRequest() is not { } request)
        {
            return 0;
        }

        var result = await orchestration.RuntimeEnvironment.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            request with { SessionModule = sessionModule }, token);

        // the branches the host evaluated as not live, of a module whose directives it was sent: what a refactoring checks an edit of the module against.
        if (request.PrecompilerTriviaJson.Length > 0)
        {
            liveBranches?.Record(definition.Module.Parse, result.DeadBranches);
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("📤 {module}: {defined} defined, {replaced} replaced, {skipped} skipped, {unresolved} unresolved type(s).",
                definition.Module.Name, result.Defined, result.Replaced, result.Skipped.Count, result.UnresolvedTypeNames.Count);
        }

        return result.Defined + result.Replaced;
    }

    // every module of the primary project that has a parse: the resolver is composed over the whole project, so one module's `As SomeType` can bind to a sibling
    // module's Type / Enum declaration. The environment host runs that project and no other: a module of another project is defined in that project's analysis
    // session, and a `Module1` of it is never defined under the primary project's.
    private (Uri? WorkspaceRoot, List<ParsedModule> Modules, Dictionary<string, DocClassType> Hosts) CollectModules(CancellationToken token)
        => CollectModules(documents.WorkspaceRoot, null, token);

    private (Uri? WorkspaceRoot, List<ParsedModule> Modules, Dictionary<string, DocClassType> Hosts)
        CollectModules(string projectRoot, Uri? workspaceRoot, CancellationToken token)
        => ProjectModules.Collect(documents, parsing, projectRoot, workspaceRoot, token);

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

        var (workspaceRoot, modules, hosts) = CollectModules(token);
        var file = document.Id.Uri.ToUri();
        var changed = modules.Where(module => module.Document == file).ToList();
        if (workspaceRoot is not null && changed.Count > 0)
        {
            // what the document no longer declares - a block the edit took out, a module it renamed - is taken out of the host, unless another document
            // declares it now.
            await DiscardGoneAsync(host, modules, file, token);
            Defined(changed);
            Moved(modules);

            // each of the document's modules is defined again with the newest definition winning, in the workspace as it is now (what the other modules
            // declare is what its names are bound against), a block the edit added among them; then the code of each is loaded, checked against the same.
            var workspaceResolver = WorkspaceSymbolResolver.Compose(
                workspaceRoot, modules.Select(module => (module.Uri, module.Kind, module.Parse)), resolver, projectName: referencedLibraries.ProjectName,
                implicitScope: ImplicitScope, libraries: PrimaryLibraries(workspaceRoot, token), documentHosts: hosts);
            SessionDefinition Changed(ParsedModule module, SessionDefinitionPhase phase)
                => new(phase, workspaceRoot, module, workspaceResolver, ImplicitScope, Replace: true);
            foreach (var module in changed)
            {
                await DefineModuleSymbolsAsync(Changed(module, SessionDefinitionPhase.Declare), token);
            }

            foreach (var module in changed)
            {
                await SendModuleCodeAsync(Changed(module, SessionDefinitionPhase.Code), token);
            }
        }

        // and so it is in the analysis session of every other project that lists it: a file two projects list is a module of each.
        if (!ReferenceEquals(host, _analysisHost))
        {
            return;
        }

        foreach (var other in OtherProjects().Where(other => document.Projects.Any(root => WorkspacePaths.Same(root, other.Root))
            && _analyzed.ContainsKey(SymbolUri.Root(other.Root))))
        {
            var root = SymbolUri.Root(other.Root);
            var (_, theirs, theirHosts) = CollectModules(other.Root, root, token);
            var theirChanged = theirs.Where(module => module.Document == file).ToList();
            if (theirChanged.Count == 0)
            {
                continue;
            }

            var otherResolver = ResolverOf(root, other, theirs, theirHosts);
            SessionDefinition Theirs(ParsedModule module, SessionDefinitionPhase phase)
                => new(phase, root, module, otherResolver, ImplicitScope, Replace: true);
            foreach (var module in theirChanged)
            {
                await DefineModuleSymbolsAsync(Theirs(module, SessionDefinitionPhase.Declare), token);
            }

            foreach (var module in theirChanged)
            {
                await SendModuleCodeAsync(Theirs(module, SessionDefinitionPhase.Code), token);
            }
        }
    }

    // the modules of the primary project this service defined in the host, by name, and the document each is written in: what a sync finds no document
    // declares any more is what the host holds and the workspace does not.
    private readonly Dictionary<string, Uri> _definedFrom = new(StringComparer.OrdinalIgnoreCase);

    private void Defined(IEnumerable<ParsedModule> modules)
    {
        lock (_definedFrom)
        {
            foreach (var module in modules.Where(module => module.Document is not null))
            {
                _definedFrom[module.Name] = module.Document!;
            }
        }
    }

    // a module the host has that another document declares now - a block moved to another file - is that document's: its sync is what takes it out.
    private void Moved(IEnumerable<ParsedModule> modules)
    {
        lock (_definedFrom)
        {
            foreach (var module in modules.Where(module => module.Document is not null && _definedFrom.ContainsKey(module.Name)))
            {
                _definedFrom[module.Name] = module.Document!;
            }
        }
    }

    // takes out of the host each module it was given from a document - from the document, when one is said - whose parse now declares it no more, and that
    // no module of the workspace is now. A document the server has no parse of at the moment (one being parsed again) says nothing of what it declares, and a
    // document that left the workspace is taken out by whoever removed it.
    private async Task DiscardGoneAsync(IRDCoreClientApp host, IReadOnlyList<ParsedModule> modules, Uri? document, CancellationToken token)
    {
        if (host.PlatformInfo?.Provides<SessionDiscard>() != true)
        {
            return;
        }

        var parsed = modules.Select(module => module.Document).OfType<Uri>().ToHashSet();
        string[] gone;
        lock (_definedFrom)
        {
            gone = [.. _definedFrom
                .Where(defined => (document is null || defined.Value == document) && parsed.Contains(defined.Value)
                    && !modules.Any(module => string.Equals(module.Name, defined.Key, StringComparison.OrdinalIgnoreCase)))
                .Select(defined => defined.Key)];
        }

        foreach (var name in gone)
        {
            await SendDiscardAsync(host, name, endProgram: false, token);
        }
    }

    public async Task<DiscardResult> DiscardModuleAsync(string moduleName, CancellationToken token, bool endProgram = false)
    {
        var host = orchestration.RuntimeEnvironment;
        if (host is null)
        {
            return new DiscardResult();
        }

        await host.WaitForReadyAsync(token);
        await using var hold = await HoldAsync(token);
        if (host.PlatformInfo?.Provides<SessionDiscard>() != true)
        {
            // the host cannot be told: what it holds stays, as it did before there was a way to say otherwise.
            return new DiscardResult();
        }

        return await SendDiscardAsync(host, moduleName, endProgram, token);
    }

    // under the hold: the module is addressed the way it was defined, and is no longer one this service defined.
    private async Task<DiscardResult> SendDiscardAsync(IRDCoreClientApp host, string moduleName, bool endProgram, CancellationToken token)
    {
        var moduleUri = SymbolUri.Module(documents.WorkspaceRoot, moduleName);
        var result = await host.SendRequestAsync<HostDiscardParams, DiscardResult>(
            new HostDiscardParams { ModuleUri = moduleUri, ModuleName = moduleName, EndProgram = endProgram }, token);
        lock (_definedFrom)
        {
            _definedFrom.Remove(moduleName);
        }

        LogIfEnabled(LogLevel.Information, $"🗑️ {moduleName}: {result.Discarded} symbol(s) discarded.");
        return result;
    }

    public Task LoadModuleCodeAsync(string moduleName, ModuleParseResult parseResult, CancellationToken token)
    {
        var workspaceRoot = SymbolUri.Root(documents.WorkspaceRoot);
        var module = new ParsedModule(SymbolUri.Module(workspaceRoot, moduleName), moduleName, ModuleType.StdModule, parseResult);
        return SendModuleCodeAsync(new SessionDefinition(SessionDefinitionPhase.Code, workspaceRoot, module, resolver, ImplicitScope, Replace: false), token,
            fromShell: true);
    }

    // the module's symbols are defined: the host composes it and loads its code, and says what is wrong with the code if it is not loaded - once for as long as
    // it fails the same way, for the module is sent again at every edit of it (ModuleLoadFailures). A module is known by its address; a shell's program, which
    // can be named as a module of the workspace is, by its own. A module of a referenced project is sent as its symbols are (SessionDefinition.Referenced).
    private async Task SendModuleCodeAsync(SessionDefinition definition, CancellationToken token, bool fromShell = false)
    {
        var moduleName = definition.Module.Name;
        var result = await orchestration.RuntimeEnvironment.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            definition.ToRequest(ParserJson.Of)!, token);

        switch (_loadFailures.Record((fromShell ? "shell " : string.Empty) + definition.Module.Uri.AbsoluteUri, result.CodeErrors))
        {
            case ModuleLoadChange.Failed:
                foreach (var error in result.CodeErrors)
                {
                    logger.LogWarning("📤 {module} was not loaded: {error}", moduleName, error);
                }
                break;

            case ModuleLoadChange.Recovered:
                LogIfEnabled(LogLevel.Information, $"📤 {moduleName} loads again.");
                break;
        }
    }

    // what the loads of the workspace as it was last composed failed with.
    private volatile ModuleLoadFailures _loadFailures = new();

    // the host the failures were reported to: another one has been told of none of them.
    private IRDCoreClientApp? _loadFailuresHost;

    public void SessionComposedAnew()
    {
        _loadFailures = new ModuleLoadFailures();

        // a session composed anew holds none of the modules it was given, and has none to take out.
        lock (_definedFrom)
        {
            _definedFrom.Clear();
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
