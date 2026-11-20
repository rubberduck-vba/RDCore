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
using System.Globalization;
using System.Runtime;
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
/// <param name="WarmUp">How each case is warmed up before its timed runs; not at all when <see langword="null"/>.</param>
internal sealed record class BenchOptions(
    int Runs = 3, string? Filter = null, bool ListOnly = false, VBExecutionEngine? Engine = null, BenchWarmUp? WarmUp = null);

/// <summary>
/// How a case is warmed up before its timed runs: untimed runs, at least <paramref name="Runs"/> of them, until the JIT has compiled nothing for
/// <paramref name="Quiet"/>. The warm-up ends once <paramref name="Limit"/> has passed, after one run at least, whatever the JIT does.
/// </summary>
/// <remarks>
/// Tiered compilation recompiles a method at its optimized tier, with dynamic PGO, in the background, once it has been called 30 times and the JIT has been
/// idle for a while: a method called once a run is recompiled after 30 runs. Until then a run can take several times as long as the runs after it, for a
/// number of runs that differs from one process to the next. After it, the JIT goes on recompiling a few methods now and then; a short quiet spell ends the
/// warm-up.
/// </remarks>
/// <param name="Runs">The fewest warm-up runs.</param>
/// <param name="Quiet">How long the JIT compiles nothing before the warm-up ends.</param>
/// <param name="Limit">How long the warm-up of a case lasts at the most, whatever the JIT does and however few runs it made, after its first run.</param>
internal sealed record class BenchWarmUp(int Runs, TimeSpan Quiet, TimeSpan Limit)
{
    /// <summary>
    /// The warm-up of <c>rdc bench</c>: at least 31 runs, past the 30 calls that tier a method up, until the JIT has compiled nothing for 250 ms, for 5 seconds
    /// at the most.
    /// </summary>
    public static BenchWarmUp UntilTheJitIsQuiet { get; } = new(31, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5));

    /// <summary>
    /// What the report says of the warm-up, in English whatever the culture: the text is data the bench job and the reference file keep.
    /// </summary>
    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"untimed runs of each case first, at least {Runs}, until the JIT compiles nothing for {Quiet.TotalMilliseconds:0} ms, "
        + $"for {Limit.TotalSeconds:0} s at the most");
}

/// <summary>
/// What a run of a benchmark case allocated, and the garbage collections made while it ran.
/// </summary>
/// <param name="Bytes">The bytes allocated while the run ran, on every thread of the process.</param>
/// <param name="Gen0">The collections of generation 0; a collection of generation 1 or 2 counts here too.</param>
/// <param name="Gen1">The collections of generation 1; a collection of generation 2 counts here too.</param>
/// <param name="Gen2">The collections of generation 2.</param>
internal readonly record struct BenchRunCost(long Bytes, int Gen0, int Gen1, int Gen2);

/// <summary>
/// A case of a benchmark, and how long each of its runs took - or the error that stopped it.
/// </summary>
/// <param name="Name">The case: <c>Module.Procedure</c>.</param>
/// <param name="Runs">How long each run took.</param>
/// <param name="Error">What stopped the case, when something did; no run that follows it is made.</param>
/// <param name="Costs">What each run allocated and the collections made while it ran, in the order of <paramref name="Runs"/>.</param>
/// <param name="WarmUpRuns">The untimed runs made before <paramref name="Runs"/> (<see cref="BenchOptions.WarmUp"/>).</param>
internal sealed record class BenchCase(
    string Name, IReadOnlyList<TimeSpan> Runs, string? Error = null, IReadOnlyList<BenchRunCost>? Costs = null, int WarmUpRuns = 0)
{
    /// <summary>The fastest run, which is the least disturbed by anything else the machine did.</summary>
    public TimeSpan? Min => Runs.Count == 0 ? null : Runs.Min();

    /// <summary>The run in the middle.</summary>
    public TimeSpan? Median => Runs.Count == 0 ? null : Runs.Order().ElementAt(Runs.Count / 2);

    /// <summary>
    /// What the fastest run allocated, and the collections made while it ran; none without a cost for each run.
    /// </summary>
    public BenchRunCost? FastestCost
    {
        get
        {
            if (Runs.Count == 0 || Costs is not { } costs || costs.Count != Runs.Count)
            {
                return null;
            }

            var fastest = 0;
            for (var run = 1; run < Runs.Count; run++)
            {
                if (Runs[run] < Runs[fastest])
                {
                    fastest = run;
                }
            }

            return costs[fastest];
        }
    }
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
/// called, which is in the first run of its case, a warm-up run when there is one.</param>
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
/// <param name="Libraries">The descriptions of the libraries the project references, and of those they depend on: what the built program's session reads.</param>
internal sealed record class BuiltWorkspace(
    IReadOnlyList<(string Module, ProcedureCompilation Compilation)> Procedures, IReadOnlyList<string> Problems, IReadOnlyList<DefineSymbolsParams> Definitions,
    IReadOnlyList<string> Modules, EnvironmentSessionProvider Provider, IRuntimeEnvironmentProfile Environment, IReadOnlyList<LibraryDescription> Libraries);

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
            cases.Add(await RunCaseAsync(provider, name, entryPoint, options.Runs, options.WarmUp, token));
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
        // the program is built in the language it is loaded in, and runs in it (BuiltEnvironment).
        var built = InTheLanguageOf(workspace, profile ?? environment);
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

        return new BuiltWorkspace(procedures, load.Problems, definitions, [.. load.Loaded.Select(loaded => loaded.Module.Name)], load.Provider, built, load.Libraries);
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

    // the language is the one the project file says, as the language server takes it, over the setting's: a project imported from a .vbp is Visual Basic 6.0
    // (SupportedLanguages.OfProject). No client names one here.
    private static IRuntimeEnvironmentProfile InTheLanguageOf(BenchWorkspace workspace, IRuntimeEnvironmentProfile profile)
        => SupportedLanguages.OfProject(workspace.Project) is { } language && language != profile.Language
            ? SDK.Runtime.RuntimeEnvironmentProfile.Of(profile) with { SourceLanguage = language }
            : profile;

    private sealed record class LoadedWorkspace(
        EnvironmentSessionProvider Provider, IReadOnlyList<(BenchModule Module, Uri Uri)> Loaded, TimeSpan Parse, TimeSpan Symbols, TimeSpan Load, TimeSpan Sweep,
        IReadOnlyList<string> Problems, IReadOnlyList<LibraryDescription> Libraries);

    // the workspace parsed, its symbols defined, its code loaded and swept, in a session composed for profile and reaching the outside world it is given
    // (outside), or the machine's when outside is null; what was defined in it, in order, kept in definitions when it is given. The caller disposes the
    // provider: the session's current directory is the workspace, which the caller may delete.
    private async Task<LoadedWorkspace> LoadAsync(BenchWorkspace workspace, IRuntimeEnvironmentProfile profile, ExternalWorld? outside, CancellationToken token,
        List<DefineSymbolsParams>? definitions = null)
    {
        var root = workspace.RootUri;
        profile = InTheLanguageOf(workspace, profile);
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
        var benchModules = new Dictionary<ParsedModule, BenchModule>(ReferenceEqualityComparer.Instance);
        var own = Parsed(workspace, root, problems, string.Empty, benchModules);
        var parseTime = stopwatch.Elapsed;

        // as the language server does: the names of every module bound among all of them and the libraries the project references, then each module's symbols
        // defined in the host, then the code of each.
        // they are bound in the environment the session was composed for: the profile's language and pointer width, which need not be the runner's.
        stopwatch.Restart();
        var composed = provider.Session.Environment;
        var referenced = LibrarySymbolProvider.Load(libraries, workspace.Project.References, composed.Language);
        var scope = composed.Language.ImplicitDeclarationScope;

        // each project the project references is bound as its own analysis binds it: its modules, the libraries it references, and its own name. It gives the
        // project its names at the precedence of the reference, after the libraries'.
        var projects = workspace.ReferencedProjects.Select(each =>
        {
            var projectRoot = each.Workspace.RootUri;
            var modules = Parsed(each.Workspace, projectRoot, problems, $"{each.Name}.", parsed: null);
            var ownLibraries = LibrarySymbolProvider.Load(libraries, each.Workspace.Project.References, composed.Language);
            var projectResolver = WorkspaceSymbolResolver.Compose(
                projectRoot, modules.Select(module => (module.Uri, module.Kind, module.Parse)), new IntrinsicSymbolResolver(composed.Is64Bit), each.Name,
                scope, [.. new LibrarySymbolProvider(projectRoot, ownLibraries, composed.Is64Bit).ProvideSymbols()], HostsOf(each.Workspace));
            return new ReferencedProjectModules(each.Name, referenced.Priorities.GetValueOrDefault(each.Name), projectRoot, modules, projectResolver);
        }).ToArray();

        IReadOnlyList<Symbol> librarySymbols =
        [
            .. new LibrarySymbolProvider(root, referenced, composed.Is64Bit).ProvideSymbols(),
            .. projects.SelectMany(project => project.NamesFor(root, scope)),
        ];

        var resolver = WorkspaceSymbolResolver.Compose(
            root, own.Select(module => (module.Uri, module.Kind, module.Parse)), new IntrinsicSymbolResolver(composed.Is64Bit),
            workspace.Project.Name, scope, librarySymbols, HostsOf(workspace));

        var define = new DefineSymbolsHandler(
            provider, new ProjectAnalysisSessions(composed, fileSystem, loggers.CreateLogger<ProjectAnalysisSessions>(), libraries), errorDetails,
            loggers.CreateLogger<DefineSymbolsHandler>());
        var symbolsTime = TimeSpan.Zero;
        var loaded = new List<(BenchModule Module, Uri Uri)>();
        foreach (var definition in SessionDefinitions.Of(root, own, resolver, scope, replace: true, projects))
        {
            if (definition.ToRequest() is not { } request)
            {
                continue;
            }

            if (definition.Phase is SessionDefinitionPhase.Code && symbolsTime == TimeSpan.Zero)
            {
                symbolsTime = stopwatch.Elapsed;
                stopwatch.Restart();
            }

            definitions?.Add(request);
            var defined = await define.Handle(request, token);
            if (definition.Phase is not SessionDefinitionPhase.Code)
            {
                continue;
            }

            // what keeps a module of a referenced project from loading is named as of its project: it refuses the run as one of the project's own does.
            var name = definition.Referenced is { } project ? $"{project.Name}.{definition.Module.Name}" : definition.Module.Name;
            if (defined.CodeErrors.Count > 0)
            {
                problems.Add($"{name}: {string.Join("; ", defined.CodeErrors)}");
            }
            else if (definition.Referenced is null)
            {
                loaded.Add((benchModules[definition.Module], definition.Module.Uri));
            }
        }

        var loadTime = stopwatch.Elapsed;

        // the sweep of each module started when it was loaded, and runs in the background: it is over before anything is timed that it could slow down.
        await provider.Image.Semantics.WhenAllEvaluatedAsync(token);
        var sweepTime = stopwatch.Elapsed;

        return new LoadedWorkspace(provider, loaded, parseTime, symbolsTime, loadTime, sweepTime, problems, referenced.Libraries);
    }

    // the modules of a workspace that parse, addressed under root; what keeps one from parsing is a problem, named with the prefix (its project's, for a
    // referenced one). A module with a syntax error is defined and loaded as the language server does it: its load refuses it, unless the error is in a
    // branch of a #If that is excluded, which only the host knows. The parser reads text alone: a form's designer is set on its parse from its text and its
    // companion file, as the language server sets it.
    private List<ParsedModule> Parsed(
        BenchWorkspace workspace, Uri root, List<string> problems, string prefix, Dictionary<ParsedModule, BenchModule>? parsed)
    {
        var modules = new List<ParsedModule>();
        foreach (var module in workspace.Modules)
        {
            var parse = new ModuleParser().Parse(workspace.DocumentOf(module), module.Source) with
            {
                Designer = FormDesigner.Read(module.Source, module.Companion, out var designerProblem),
            };
            if ((module.CompanionProblem ?? designerProblem) is { } problem)
            {
                loggers.CreateLogger<BenchRunner>().LogWarning(problem, "⚠️ The designer of {module} cannot be read.", module.RelativePath);
            }

            if (parse.SyntaxTree is null)
            {
                problems.Add($"{prefix}{module.Name}: {string.Join("; ", parse.SyntaxErrors.Select(error => error.Description))}");
                continue;
            }

            var each = new ParsedModule(SymbolUri.Module(root, module.Name), module.Name, module.Type, parse);
            modules.Add(each);
            parsed?.Add(each, module);
        }

        return modules;
    }

    // what host document each document module is the code of, as the language server has it: its code names the members of its host's class, and they are
    // not taken for variables it declares by naming them.
    private static Dictionary<string, DocClassType> HostsOf(BenchWorkspace workspace)
    {
        var hosts = new Dictionary<string, DocClassType>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in workspace.Modules)
        {
            var listed = workspace.Project.Modules
                .FirstOrDefault(entry => string.Equals(entry.RelativeUri, module.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (listed?.Super is { } super)
            {
                hosts.TryAdd(module.Name, super);
            }
        }

        return hosts;
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

    private async Task<BenchCase> RunCaseAsync(
        EnvironmentSessionProvider provider, string name, VBTypeMemberSymbol entryPoint, int runs, BenchWarmUp? warmUp, CancellationToken token)
    {
        // a pipeline per run, as the host composes one per request: its cancellation is the run's.
        Task<ExecuteResult> RunOnceAsync() => provider.Execution.RunAsync(
            RuntimeExecutionPipeline.Create(provider.Session, provider.Image, errorDetails, token, outside: provider.Outside), entryPoint, debug: false, token);

        var warmUpRuns = 0;
        if (warmUp is not null)
        {
            // untimed, and what they allocate and collect is not the case's.
            var elapsed = Stopwatch.StartNew();
            var quiet = Stopwatch.StartNew();
            var compiled = JitInfo.GetCompiledMethodCount(currentThread: false);
            // a case slow enough to reach the limit first has run its loops long enough for the JIT to have replaced them.
            while (warmUpRuns == 0 || (elapsed.Elapsed < warmUp.Limit && (warmUpRuns < warmUp.Runs || quiet.Elapsed < warmUp.Quiet)))
            {
                token.ThrowIfCancellationRequested();
                var result = await RunOnceAsync();
                warmUpRuns++;
                if (result.Outcome is not ExecutionOutcome.Completed)
                {
                    return new BenchCase(name, [], Failure(result), [], warmUpRuns);
                }

                // the JIT's background thread counts: a method recompiled at a higher tier is compiled again.
                if (JitInfo.GetCompiledMethodCount(currentThread: false) is var now && now != compiled)
                {
                    compiled = now;
                    quiet.Restart();
                }
            }
        }

        var times = new List<TimeSpan>(runs);
        var costs = new List<BenchRunCost>(runs);
        for (var run = 0; run < runs; run++)
        {
            token.ThrowIfCancellationRequested();

            // every run starts from a collected heap, so a collection inside it depends on what the run allocates and keeps, not on the runs and
            // cases before it.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var bytes = GC.GetTotalAllocatedBytes(precise: true);
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            var stopwatch = Stopwatch.StartNew();
            var result = await RunOnceAsync();
            stopwatch.Stop();
            var cost = new BenchRunCost(GC.GetTotalAllocatedBytes(precise: true) - bytes,
                GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2);

            if (result.Outcome is not ExecutionOutcome.Completed)
            {
                return new BenchCase(name, times, Failure(result), costs, warmUpRuns);
            }

            times.Add(stopwatch.Elapsed);
            costs.Add(cost);
        }

        return new BenchCase(name, times, Costs: costs, WarmUpRuns: warmUpRuns);
    }

    private static string Failure(ExecuteResult result)
        => result.Outcome is ExecutionOutcome.RuntimeError ? $"{result.ErrorCode} {result.ErrorMessage}" : $"{result.Outcome} {result.ErrorMessage}".Trim();
}
