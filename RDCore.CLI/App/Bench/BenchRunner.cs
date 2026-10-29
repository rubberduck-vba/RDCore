using Microsoft.Extensions.Logging;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.External;
using RDCore.Parsing;
using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using System.Diagnostics;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Bench;

/// <summary>
/// What <c>rdc bench</c> is asked to do with a workspace.
/// </summary>
/// <param name="Runs">How many times each case is run.</param>
/// <param name="Filter">Only the cases whose <c>Module.Procedure</c> name contains this, without regard to case; every case when <see langword="null"/>.</param>
/// <param name="ListOnly">Whether the workspace is loaded and swept, and its cases listed, without running any of them.</param>
/// <param name="Engine">What runs the cases; the engine the environment says when <see langword="null"/>.</param>
internal sealed record class BenchOptions(int Runs = 3, string? Filter = null, bool ListOnly = false, VBExecutionEngine? Engine = null);

/// <summary>
/// A case of a benchmark, and how long each of its runs took - or the error that stopped it.
/// </summary>
/// <param name="Name">The case: <c>Module.Procedure</c>.</param>
/// <param name="Runs">How long each run took.</param>
/// <param name="Error">What stopped the case, when something did; no run that follows it is made.</param>
internal sealed record class BenchCase(string Name, IReadOnlyList<TimeSpan> Runs, string? Error = null)
{
    /// <summary>The fastest run, which is the least disturbed by anything else the machine did.</summary>
    public TimeSpan? Min => Runs.Count == 0 ? null : Runs.Min();

    /// <summary>The run in the middle.</summary>
    public TimeSpan? Median => Runs.Count == 0 ? null : Runs.Order().ElementAt(Runs.Count / 2);
}

/// <summary>
/// What <c>rdc bench</c> measured of a workspace: how long it took to get the code ready to run, then each case.
/// </summary>
/// <param name="Workspace">The workspace.</param>
/// <param name="Parse">Parsing every module.</param>
/// <param name="Symbols">Building the symbols of every module and defining them in the session.</param>
/// <param name="Load">Loading the code of every module: lowered, and analyzed statically.</param>
/// <param name="Sweep">From the first module loaded until the facts of every module are evaluated: the analysis sweep, which runs in the background as modules load.</param>
/// <param name="Compile">Compiling the procedures the cases called to IL, when the engine is <see cref="VBExecutionEngine.Compiled"/>: each the first time it was
/// called, which is in the first run of its case.</param>
/// <param name="Cases">The cases, in the order the modules declare them.</param>
/// <param name="Problems">What kept a module from being parsed or loaded; a case of such a module is not run.</param>
internal sealed record class BenchReport(
    BenchWorkspace Workspace, TimeSpan Parse, TimeSpan Symbols, TimeSpan Load, TimeSpan Sweep, TimeSpan Compile, IReadOnlyList<BenchCase> Cases,
    IReadOnlyList<string> Problems);

/// <summary>
/// Runs the benchmarks of a workspace in-process, the way the environment host runs a program: the session composed from the project, each module's symbols and
/// code defined in it, and each case run as a program of its own.
/// </summary>
/// <remarks>
/// <para>
/// A case is a <c>Public Sub</c> of a standard module whose name begins with <c>Bench</c> and that takes no arguments. It is timed from the moment it is started to
/// the moment it is over, with nothing else running: the analysis sweep that loading starts is waited for first. What it prints is discarded.
/// </para>
/// <para>
/// The workspace reaches none of the outside world (<see cref="ExternalWorld.None"/>): a dialog answers itself, and nothing is called through COM or a
/// <c>Declare</c>. A benchmark measures the interpreter, and not what it waits for.
/// </para>
/// </remarks>
/// <param name="environment">The environment the session is composed for.</param>
/// <param name="fileSystem">The file system the workspace is on.</param>
/// <param name="messages">Builds the verbose half of a run-time error message.</param>
/// <param name="loggers">Makes the loggers of the host's parts.</param>
internal sealed class BenchRunner(IRuntimeEnvironmentProfile environment, IFileSystem fileSystem, IVerboseMessageBuilder messages, ILoggerFactory loggers)
{
    /// <summary>What the name of a case begins with.</summary>
    public const string CasePrefix = "Bench";

    /// <summary>The environment the session is composed for: what runs the cases, and how they are built.</summary>
    public IRuntimeEnvironmentProfile Environment => environment;

    /// <summary>
    /// Loads <paramref name="workspace"/>, waits for its analysis sweep, and runs its cases.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="options">How many runs, and of which cases.</param>
    /// <param name="progress">Told the name of each case as it is about to be run.</param>
    /// <param name="token">Stops the benchmark between two runs, and the run that is going on.</param>
    public async Task<BenchReport> RunAsync(BenchWorkspace workspace, BenchOptions options, Action<string>? progress = null, CancellationToken token = default)
    {
        // whatever implements the environment: --engine is never ignored.
        var profile = options.Engine is { } engine ? SDK.Runtime.RuntimeEnvironmentProfile.Of(environment) with { ExecutionEngine = engine } : environment;
        var load = await LoadAsync(workspace, profile, token);
        var provider = load.Provider;

        var cases = new List<BenchCase>();
        foreach (var (name, entryPoint) in Cases(provider.Session, load.Loaded).Where(found => Selected(found.Name, options.Filter)))
        {
            if (options.ListOnly)
            {
                cases.Add(new BenchCase(name, []));
                continue;
            }

            progress?.Invoke(name);
            cases.Add(await RunCaseAsync(provider, name, entryPoint, options.Runs, token));
        }

        return new BenchReport(workspace, load.Parse, load.Symbols, load.Load, load.Sweep, provider.Image.CompilationTime, cases, load.Problems);
    }

    /// <summary>
    /// Loads <paramref name="workspace"/>, and compiles each procedure of each module that loaded, as its first call would: whether it runs compiled, and
    /// what keeps it in the interpreter when it does not.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="token">Stops the load.</param>
    /// <returns>Each procedure, by module and in the order the module declares them; and what kept a module from being parsed or loaded.</returns>
    public async Task<(IReadOnlyList<(string Module, ProcedureCompilation Compilation)> Procedures, IReadOnlyList<string> Problems)> CompileAsync(
        BenchWorkspace workspace, CancellationToken token = default)
    {
        var load = await LoadAsync(workspace, environment, token);
        var session = load.Provider.Session;
        var pipeline = RuntimeExecutionPipeline.Create(session, load.Provider.Image, messages, token, outside: load.Provider.Outside);
        var procedures = new List<(string Module, ProcedureCompilation Compilation)>();
        foreach (var (module, _) in load.Loaded)
        {
            if (!session.Symbols.TryGetModule(module.Name, out var symbol))
            {
                continue;
            }

            foreach (var procedure in session.Symbols.MembersOf(symbol.Uri).OfType<VBTypeMemberSymbol>().OrderBy(member => member.Range.Start.Line))
            {
                if (ProcedureCompilation.Of(load.Provider.Image, session, pipeline.Executor, procedure) is { } compilation)
                {
                    procedures.Add((module.Name, compilation));
                }
            }
        }

        return (procedures, load.Problems);
    }

    private sealed record class LoadedWorkspace(
        EnvironmentSessionProvider Provider, IReadOnlyList<(BenchModule Module, Uri Uri)> Loaded, TimeSpan Parse, TimeSpan Symbols, TimeSpan Load, TimeSpan Sweep,
        IReadOnlyList<string> Problems);

    // the workspace parsed, its symbols defined, its code loaded and swept, in a session composed for profile.
    private async Task<LoadedWorkspace> LoadAsync(BenchWorkspace workspace, IRuntimeEnvironmentProfile profile, CancellationToken token)
    {
        var problems = new List<string>();
        var root = new Uri(workspace.Root);
        var provider = new EnvironmentSessionProvider(
            profile, fileSystem, loggers.CreateLogger<EnvironmentSessionProvider>(), new DirectoryLibrarySource(fileSystem, string.Empty), ExternalWorld.None);
        provider.Compose(workspace.Project, root);

        var stopwatch = Stopwatch.StartNew();
        var parsed = new List<(BenchModule Module, Uri Uri, ModuleParseResult Parse)>();
        foreach (var module in workspace.Modules)
        {
            var parse = new ModuleParser().Parse(new Uri(fileSystem.Path.Combine(workspace.Root, module.RelativePath)), module.Source);
            if (parse.IsSuccess && parse.SyntaxTree is not null)
            {
                parsed.Add((module, SymbolUri.Module(root, module.Name), parse));
            }
            else
            {
                problems.Add($"{module.Name}: {string.Join("; ", parse.SyntaxErrors.Select(error => error.Description))}");
            }
        }

        var parseTime = stopwatch.Elapsed;

        // as the language server does: the names of every module bound among all of them and the libraries the project references, then each module's symbols
        // defined in the host, then the code of each.
        stopwatch.Restart();
        var libraries = LibrarySymbolProvider.Load(new DirectoryLibrarySource(fileSystem, string.Empty), workspace.Project.References);
        IReadOnlyList<Symbol> librarySymbols = [.. new LibrarySymbolProvider(root, libraries, environment.Is64Bit).ProvideSymbols()];
        var scope = environment.Language.ImplicitDeclarationScope;
        var resolver = WorkspaceSymbolResolver.Compose(
            root, parsed.Select(module => (module.Uri, module.Module.Type, module.Parse)), new IntrinsicSymbolResolver(environment.Is64Bit),
            workspace.Project.Name, scope, librarySymbols);

        // as the language server does, the types of every module are declared before the declarations of any are bound (DefineSymbolsParams.DeclareTypesOnly).
        var define = new DefineSymbolsHandler(provider, messages, loggers.CreateLogger<DefineSymbolsHandler>());
        foreach (var declareTypesOnly in new[] { true, false })
        {
            foreach (var (module, uri, parse) in parsed)
            {
                var symbols = new SyntaxTreeSymbolProvider(root, uri, module.Type, parse, resolver, withImplicitDeclarations: true, scope).ProvideSymbols();
                await define.Handle(new DefineSymbolsParams
                {
                    WorkspaceRoot = root,
                    ModuleUri = uri,
                    ModuleName = module.Name,
                    Symbols = SymbolDescriptorProjector.Project(symbols, uri),
                    Directives = ModuleKinds.DirectivesOf(module.Type, parse.SyntaxTree!.GetModuleDirectives()),
                    ImplementedInterfaceNames = parse.SyntaxTree!.GetImplementedInterfaceNames(),
                    ImplementedInterfaceRanges = parse.SyntaxTree!.GetImplementedInterfaceRanges(),
                    PrecompilerTriviaJson = DefineSymbolsParams.PrecompilerTriviaJsonOf(parse),
                    Replace = true,
                    DeclareTypesOnly = declareTypesOnly,
                }, token);
            }
        }

        var symbolsTime = stopwatch.Elapsed;

        stopwatch.Restart();
        var loaded = new List<(BenchModule Module, Uri Uri)>();
        foreach (var (module, uri, parse) in parsed)
        {
            var defined = await define.Handle(new DefineSymbolsParams
            {
                WorkspaceRoot = root,
                ModuleUri = uri,
                ModuleName = module.Name,
                Directives = ModuleKinds.DirectivesOf(module.Type, parse.SyntaxTree!.GetModuleDirectives()),
                ImplementedInterfaceNames = parse.SyntaxTree!.GetImplementedInterfaceNames(),
                ImplementedInterfaceRanges = parse.SyntaxTree!.GetImplementedInterfaceRanges(),
                ParseResultJson = PlatformJson.Serialize(parse),
                CodeOnly = true,
            }, token);

            if (defined.CodeErrors.Count > 0)
            {
                problems.Add($"{module.Name}: {string.Join("; ", defined.CodeErrors)}");
            }
            else
            {
                loaded.Add((module, uri));
            }
        }

        var loadTime = stopwatch.Elapsed;

        // the sweep of each module started when it was loaded, and runs in the background: it is over before anything is timed that it could slow down.
        await provider.Image.Semantics.WhenAllEvaluatedAsync(token);
        var sweepTime = stopwatch.Elapsed;

        return new LoadedWorkspace(provider, loaded, parseTime, symbolsTime, loadTime, sweepTime, problems);
    }

    private static bool Selected(string name, string? filter) => filter is not { Length: > 0 } || name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    // every Public Sub of a standard module that is named for a case and takes nothing, in the order the module declares them.
    private static IEnumerable<(string Name, VBTypeMemberSymbol EntryPoint)> Cases(IRuntimeSession session, IReadOnlyList<(BenchModule Module, Uri Uri)> modules)
    {
        foreach (var (module, _) in modules.Where(loaded => loaded.Module.Type is ModuleType.StdModule))
        {
            if (!session.Symbols.TryResolveValue(module.Name, GlobalSymbols.UnresolvedSymbol, out var symbol) || symbol is null)
            {
                continue;
            }

            var procedures = session.Symbols.MembersOf(symbol.Uri)
                .OfType<VBProcedureMemberSymbol>()
                .Where(procedure => procedure is not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol)
                    && procedure.Name.StartsWith(CasePrefix, StringComparison.OrdinalIgnoreCase)
                    && procedure.AccessModifier is SDK.Model.AccessModifier.Public or SDK.Model.AccessModifier.Implicit
                    && procedure.Parameters.IsEmpty)
                .OrderBy(procedure => procedure.Range.Start.Line);

            foreach (var procedure in procedures)
            {
                yield return ($"{module.Name}.{procedure.Name}", procedure);
            }
        }
    }

    private async Task<BenchCase> RunCaseAsync(EnvironmentSessionProvider provider, string name, VBTypeMemberSymbol entryPoint, int runs, CancellationToken token)
    {
        var times = new List<TimeSpan>(runs);
        for (var run = 0; run < runs; run++)
        {
            token.ThrowIfCancellationRequested();

            // a pipeline per run, as the host composes one per request: its cancellation is the run's.
            var pipeline = RuntimeExecutionPipeline.Create(provider.Session, provider.Image, messages, token, outside: provider.Outside);
            var stopwatch = Stopwatch.StartNew();
            var result = await provider.Execution.RunAsync(pipeline, entryPoint, debug: false, token);
            stopwatch.Stop();

            if (result.Outcome is not ExecutionOutcome.Completed)
            {
                var error = result.Outcome is ExecutionOutcome.RuntimeError ? $"{result.ErrorCode} {result.ErrorMessage}" : $"{result.Outcome} {result.ErrorMessage}".Trim();
                return new BenchCase(name, times, error);
            }

            times.Add(stopwatch.Elapsed);
        }

        return new BenchCase(name, times);
    }
}
