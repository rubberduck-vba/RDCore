using Microsoft.Extensions.Logging;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.External;
using RDCore.External.Interaction;
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
using RDCore.Runtime.Semantics;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Symbols;
using System.Diagnostics;
using System.IO.Abstractions;
using RDCore.SDK.Platform.Components;

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
/// <param name="Problems">What kept a module from being parsed or loaded. While there is any, no case is run: each is refused with it.</param>
/// <param name="Indent">
/// Indenting the shell's program of <see cref="BenchRunner.ProgramLines"/> lines as the shell does (<see cref="RDCore.SDK.Formatting.ProgramIndenter"/>), the
/// fastest of the runs (<see cref="BenchOptions.Runs"/>, <c>--runs</c>): the same program whatever the workspace; none when the cases are only listed.
/// </param>
/// <param name="DirectCalls">How many calls compiled code made of compiled procedures through their typed entries, over every run of every case.</param>
/// <param name="IndirectCalls">How many calls compiled code made through the invoker, over every run of every case.</param>
internal sealed record class BenchReport(
    BenchWorkspace Workspace, TimeSpan Parse, TimeSpan Symbols, TimeSpan Load, TimeSpan Sweep, TimeSpan Compile, IReadOnlyList<BenchCase> Cases,
    IReadOnlyList<string> Problems, TimeSpan? Indent = null, long DirectCalls = 0, long IndirectCalls = 0);

/// <summary>
/// What a build made of a workspace (<see cref="BenchRunner.BuildAsync"/>).
/// </summary>
/// <param name="Procedures">Each procedure compiled into the program's assembly, or found not to be, by module and in the order the module declares them.</param>
/// <param name="Problems">What kept a module from being parsed or loaded.</param>
/// <param name="Definitions">What was defined in the session, in order: what the built program defines in its own when it runs.</param>
/// <param name="Modules">The names of the modules that loaded.</param>
/// <param name="Provider">The session the workspace was loaded in, which the caller disposes.</param>
/// <param name="Environment">The environment the program was built for.</param>
internal sealed record class BuiltWorkspace(
    IReadOnlyList<(string Module, ProcedureCompilation Compilation)> Procedures, IReadOnlyList<string> Problems, IReadOnlyList<DefineSymbolsParams> Definitions,
    IReadOnlyList<string> Modules, EnvironmentSessionProvider Provider, IRuntimeEnvironmentProfile Environment);

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
/// A benchmark reaches none of the outside world (<see cref="ExternalWorld.None"/>): a dialog answers itself, and nothing is called through COM or a
/// <c>Declare</c>. A benchmark measures the interpreter, and not what it waits for. A workspace that is compiled or built reaches none of it either. A
/// program loaded to be run (<see cref="LoadProgramAsync"/>) reaches the machine's outside world, as the program <c>rdc build</c> builds of it does.
/// </para>
/// </remarks>
/// <param name="environment">The environment the session is composed for.</param>
/// <param name="fileSystem">The file system the workspace is on.</param>
/// <param name="errorDetails">Records the details of the run-time errors raised.</param>
/// <param name="loggers">Makes the loggers of the host's parts.</param>
/// <param name="libraries">
/// Where the descriptions of the libraries a project references are found: the platform's, as the environment host has them, so that a workspace that
/// references Excel is benched with Excel's types.
/// </param>
internal sealed class BenchRunner(
    IRuntimeEnvironmentProfile environment, IFileSystem fileSystem, IErrorDetailsRecorder errorDetails, ILoggerFactory loggers, ILibrarySource libraries)
{
    /// <summary>What the name of a case begins with.</summary>
    public const string CasePrefix = "Bench";

    /// <summary>The lines of the shell's program whose indentation is timed: as many as a long paste at the prompt.</summary>
    public const int ProgramLines = 5000;

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
        var load = await LoadAsync(workspace, profile, ExternalWorld.None, token);
        using var provider = load.Provider;

        // nothing of the project runs while a module of it does not load (RD-VBAL 2.6.1): each case is refused with what keeps the modules from loading.
        var refused = load.Problems.Count > 0 ? $"{ExecutionOutcome.SyntaxError} {string.Join("; ", load.Problems)}" : null;

        var cases = new List<BenchCase>();
        foreach (var (name, entryPoint) in Cases(provider.Session, load.Loaded).Where(found => Selected(found.Name, options.Filter)))
        {
            if (options.ListOnly)
            {
                cases.Add(new BenchCase(name, []));
                continue;
            }

            if (refused is not null)
            {
                cases.Add(new BenchCase(name, [], refused));
                continue;
            }

            progress?.Invoke(name);
            cases.Add(await RunCaseAsync(provider, name, entryPoint, options.Runs, token));
        }

        var indent = options.ListOnly ? (TimeSpan?)null : IndentProgram(options.Runs);

        // how compiled code made its calls: what says which way a case that calls ran.
        var (direct, indirect) = provider.Image.CompiledCalls;
        return new BenchReport(workspace, load.Parse, load.Symbols, load.Load, load.Sweep, provider.Image.CompilationTime, cases, load.Problems, indent,
            direct, indirect);
    }

    /// <summary>
    /// Indents the shell's program of <see cref="ProgramLines"/> lines - a quarter of them the openers of blocks - as the shell does before it lists the program,
    /// saves it or tells the language server of it: one parse of the program, and the indenter's edits made on its lines.
    /// </summary>
    /// <param name="runs">How many times it is indented, after once to have the parser ready.</param>
    /// <returns>The fastest of the runs.</returns>
    internal static TimeSpan IndentProgram(int runs)
    {
        string[] statements = [.. Enumerable.Range(1, ProgramLines).Select(line => (line % 4) switch
        {
            1 => $"If X > {line} Then",
            2 => $"X = X + {line}",
            3 => "End If",
            _ => $"Y = {line}",
        })];
        var parser = new ModuleParser();
        var uri = new Uri($"untitled:{CasePrefix}{SDK.Workspace.BasicProgramText.Extension}");
        Func<string, ModuleParseResult> parse = text => parser.Parse(uri, text);
        SDK.Formatting.ProgramIndenter.Indent(statements, parse, SDK.Formatting.CodeFormattingOptions.Default);

        var fastest = TimeSpan.MaxValue;
        for (var run = 0; run < Math.Max(1, runs); run++)
        {
            var stopwatch = Stopwatch.StartNew();
            SDK.Formatting.ProgramIndenter.Indent(statements, parse, SDK.Formatting.CodeFormattingOptions.Default);
            fastest = TimeSpan.FromTicks(Math.Min(fastest.Ticks, stopwatch.Elapsed.Ticks));
        }

        return fastest;
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
        var load = await LoadAsync(workspace, environment, ExternalWorld.None, token);
        using var provider = load.Provider;
        var session = provider.Session;
        var pipeline = RuntimeExecutionPipeline.Create(session, provider.Image, errorDetails, token, outside: provider.Outside);
        var procedures = new List<(string Module, ProcedureCompilation Compilation)>();
        foreach (var (module, _) in load.Loaded)
        {
            if (!session.Symbols.TryGetModule(module.Name, out var symbol))
            {
                continue;
            }

            foreach (var procedure in session.Symbols.MembersOf(symbol.Uri).OfType<VBTypeMemberSymbol>().OrderBy(member => member.Range.Start.Line))
            {
                if (ProcedureCompilation.Of(provider.Image, session, pipeline.Executor, procedure) is { } compilation)
                {
                    procedures.Add((module.Name, compilation));
                }
            }
        }

        return (procedures, load.Problems);
    }

    /// <summary>
    /// Loads <paramref name="workspace"/>, and compiles each procedure of each module that loaded into <paramref name="builder"/>, as its first call would:
    /// what <c>rdc build</c> makes of a workspace.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="builder">The program's assembly.</param>
    /// <param name="profile">The environment the program is built for; the runner's when omitted.</param>
    /// <param name="token">Stops the load.</param>
    public async Task<BuiltWorkspace> BuildAsync(BenchWorkspace workspace, ProgramAssemblyBuilder builder, IRuntimeEnvironmentProfile? profile = null,
        CancellationToken token = default)
    {
        var definitions = new List<DefineSymbolsParams>();
        var built = profile ?? environment;
        var load = await LoadAsync(workspace, built, ExternalWorld.None, token, definitions);
        var procedures = new List<(string Module, ProcedureCompilation Compilation)>();
        try
        {
            var session = load.Provider.Session;
            var pipeline = RuntimeExecutionPipeline.Create(session, load.Provider.Image, errorDetails, token, outside: load.Provider.Outside);
            foreach (var (module, _) in load.Loaded)
            {
                if (!session.Symbols.TryGetModule(module.Name, out var symbol))
                {
                    continue;
                }

                foreach (var procedure in session.Symbols.MembersOf(symbol.Uri).OfType<VBTypeMemberSymbol>().OrderBy(member => member.Range.Start.Line))
                {
                    if (builder.Add(load.Provider.Image, session, pipeline.Executor, procedure) is { } compilation)
                    {
                        procedures.Add((module.Name, compilation));
                    }
                }
            }
        }
        catch
        {
            load.Provider.Dispose();
            throw;
        }

        return new BuiltWorkspace(procedures, load.Problems, definitions, [.. load.Loaded.Select(loaded => loaded.Module.Name)], load.Provider, built);
    }

    /// <summary>
    /// Loads <paramref name="workspace"/> to run it, as <c>rdc run</c> does: the session composed for <paramref name="profile"/> and the outside world of the
    /// machine, every module defined and loaded, and the analysis sweep waited for.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="profile">The environment the program runs in; the runner's when omitted.</param>
    /// <param name="dialogs">Who answers the program's dialogs in place of the machine's own; the machine's when omitted.</param>
    /// <param name="token">Stops the load.</param>
    /// <returns>The session, which the caller disposes; the modules that loaded; and what kept a module from being parsed or loaded.</returns>
    public async Task<(EnvironmentSessionProvider Provider, IReadOnlyList<string> Modules, IReadOnlyList<string> Problems)> LoadProgramAsync(
        BenchWorkspace workspace, IRuntimeEnvironmentProfile? profile = null, IUserInteraction? dialogs = null, CancellationToken token = default)
    {
        // a program reaches the outside world of the machine, as the built program does: its automation servers, its native libraries and its dialogs.
        var outside = dialogs is null ? null : MachineExternal.World with { Interaction = dialogs };
        var load = await LoadAsync(workspace, profile ?? environment, outside, token);
        return (load.Provider, [.. load.Loaded.Select(loaded => loaded.Module.Name)], load.Problems);
    }

    private sealed record class LoadedWorkspace(
        EnvironmentSessionProvider Provider, IReadOnlyList<(BenchModule Module, Uri Uri)> Loaded, TimeSpan Parse, TimeSpan Symbols, TimeSpan Load, TimeSpan Sweep,
        IReadOnlyList<string> Problems);

    // the workspace parsed, its symbols defined, its code loaded and swept, in a session composed for profile and reaching the outside world it is given
    // (outside), or the machine's when outside is null; what was defined in it, in order, kept in definitions when it is given. The caller disposes the
    // provider: the session's current directory is the workspace, which the caller may delete.
    private async Task<LoadedWorkspace> LoadAsync(BenchWorkspace workspace, IRuntimeEnvironmentProfile profile, ExternalWorld? outside, CancellationToken token,
        List<DefineSymbolsParams>? definitions = null)
    {
        var root = workspace.RootUri;
        var provider = new EnvironmentSessionProvider(
            profile, fileSystem, loggers.CreateLogger<EnvironmentSessionProvider>(), libraries, outside);
        try
        {
            provider.Compose(workspace.Project, root);
            return await LoadAsync(provider, workspace, root, token, definitions);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    private async Task<LoadedWorkspace> LoadAsync(EnvironmentSessionProvider provider, BenchWorkspace workspace, Uri root, CancellationToken token,
        List<DefineSymbolsParams>? definitions)
    {
        var problems = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        var parsed = new List<(BenchModule Module, Uri Uri, ModuleParseResult Parse)>();
        foreach (var module in workspace.Modules)
        {
            // a module with a syntax error is defined and loaded as the language server does it: its load refuses it, unless the error is in a branch of a #If
            // that is excluded, which only the host knows.
            // the parser reads text alone: a form's designer is set on its parse from its text and its companion file, as the language server sets it.
            var parse = new ModuleParser().Parse(workspace.DocumentOf(module), module.Source) with
            {
                Designer = FormDesigner.Read(module.Source, module.Companion, out var designerProblem),
            };
            if ((module.CompanionProblem ?? designerProblem) is { } problem)
            {
                loggers.CreateLogger<BenchRunner>().LogWarning(problem, "⚠️ The designer of {module} cannot be read.", module.RelativePath);
            }

            if (parse.SyntaxTree is not null)
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
        var referenced = LibrarySymbolProvider.Load(libraries, workspace.Project.References, environment.Language);
        IReadOnlyList<Symbol> librarySymbols = [.. new LibrarySymbolProvider(root, referenced, environment.Is64Bit).ProvideSymbols()];
        var scope = environment.Language.ImplicitDeclarationScope;

        // what host document each document module is the code of, as the language server has it: its code names the members of its host's class, and
        // they are not taken for variables it declares by naming them.
        var hosts = new Dictionary<string, DocClassType>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in workspace.Modules)
        {
            if (workspace.Project.Modules.FirstOrDefault(entry => string.Equals(entry.RelativeUri, module.RelativePath, StringComparison.OrdinalIgnoreCase))?.Super is { } super)
            {
                hosts.TryAdd(module.Name, super);
            }
        }

        var resolver = WorkspaceSymbolResolver.Compose(
            root, parsed.Select(module => (module.Uri, module.Module.Type, module.Parse)), new IntrinsicSymbolResolver(environment.Is64Bit),
            workspace.Project.Name, scope, librarySymbols, hosts);

        // as the language server does, the types of every module are declared before the declarations of any are bound (DefineSymbolsParams.DeclareTypesOnly).
        var define = new DefineSymbolsHandler(provider, errorDetails, loggers.CreateLogger<DefineSymbolsHandler>());
        Task<DefineSymbolsResult> Define(DefineSymbolsParams definition)
        {
            definitions?.Add(definition);
            return define.Handle(definition, token);
        }

        foreach (var declareTypesOnly in new[] { true, false })
        {
            foreach (var (module, uri, parse) in parsed)
            {
                var symbols = new SyntaxTreeSymbolProvider(root, uri, module.Type, parse, resolver, withImplicitDeclarations: true, scope).ProvideSymbols();
                await Define(new DefineSymbolsParams
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
                });
            }
        }

        var symbolsTime = stopwatch.Elapsed;

        stopwatch.Restart();
        var loaded = new List<(BenchModule Module, Uri Uri)>();
        foreach (var (module, uri, parse) in parsed)
        {
            var defined = await Define(new DefineSymbolsParams
            {
                WorkspaceRoot = root,
                ModuleUri = uri,
                ModuleName = module.Name,
                Directives = ModuleKinds.DirectivesOf(module.Type, parse.SyntaxTree!.GetModuleDirectives()),
                ImplementedInterfaceNames = parse.SyntaxTree!.GetImplementedInterfaceNames(),
                ImplementedInterfaceRanges = parse.SyntaxTree!.GetImplementedInterfaceRanges(),
                ParseResultJson = PlatformJson.Serialize(parse),
                CodeOnly = true,
            });

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
            var pipeline = RuntimeExecutionPipeline.Create(provider.Session, provider.Image, errorDetails, token, outside: provider.Outside);
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
