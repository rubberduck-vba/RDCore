using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using RDCore.Symbols;
using RDCore.Tests.Platform;
using RDCore.SDK.Platform.Components;

namespace RDCore.Tests.Cli;

/// <summary>
/// Runs a workspace of several modules the way the platform does - the language server's composition over every module, the host's session, each module's symbols and code
/// defined in it, then the entry point run - and gives what it printed.
/// </summary>
internal static class ModuleWorkspace
{
    internal static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-module-workspace");

    /// <summary>A class module's source: the header every one has, then <paramref name="body"/>.</summary>
    public static string ClassModule(string name, params string[] body)
        => $"VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"{name}\"\r\n{string.Join("\r\n", body)}\r\n";

    /// <summary>A standard module's source: the name every one has, then <paramref name="body"/>.</summary>
    public static string StandardModule(string name, params string[] body)
        => $"Attribute VB_Name = \"{name}\"\r\n{string.Join("\r\n", body)}\r\n";

    /// <summary>
    /// Runs <c>Program.Main</c> of a workspace of <paramref name="classes"/> and the <c>Program</c> standard module whose source is <paramref name="program"/>.
    /// </summary>
    /// <returns>What it printed, a line each, trimmed.</returns>
    public static Task<string[]> RunAsync(IReadOnlyList<(string Name, string Source)> classes, string program)
        => RunCoreAsync(classes, program, errorsOnly: false);

    /// <summary>
    /// Runs <c>Program.Main</c> like <see cref="RunAsync(IReadOnlyList{ValueTuple{string, string}}, string)"/>, in a workspace that has other standard
    /// modules besides <c>Program</c>.
    /// </summary>
    /// <param name="classes">The class modules of the workspace.</param>
    /// <param name="modules">The standard modules of the workspace other than <c>Program</c> (<see cref="StandardModule"/>).</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    public static Task<string[]> RunAsync(IReadOnlyList<(string Name, string Source)> classes, IReadOnlyList<(string Name, string Source)> modules, string program)
        => RunCoreAsync(classes, program, errorsOnly: false, modules: modules);

    /// <summary>
    /// Loads the workspace like <see cref="LoadErrorsAsync(IReadOnlyList{ValueTuple{string, string}}, string)"/>, when it has other standard modules
    /// besides <c>Program</c>.
    /// </summary>
    /// <param name="classes">The class modules of the workspace.</param>
    /// <param name="modules">The standard modules of the workspace other than <c>Program</c> (<see cref="StandardModule"/>).</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    public static Task<string[]> LoadErrorsAsync(IReadOnlyList<(string Name, string Source)> classes, IReadOnlyList<(string Name, string Source)> modules, string program)
        => RunCoreAsync(classes, program, errorsOnly: true, modules: modules);

    /// <summary>
    /// Runs <c>Program.Main</c> like <see cref="RunAsync(IReadOnlyList{ValueTuple{string, string}}, IReadOnlyList{ValueTuple{string, string}}, string)"/>, with
    /// <c>Program</c> listed first in the project, and so defined and loaded before the modules it uses, as a <c>.rdproj</c> may list them.
    /// </summary>
    /// <param name="classes">The class modules of the workspace.</param>
    /// <param name="modules">The standard modules of the workspace other than <c>Program</c> (<see cref="StandardModule"/>).</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    public static Task<string[]> RunProgramFirstAsync(IReadOnlyList<(string Name, string Source)> classes, IReadOnlyList<(string Name, string Source)> modules, string program)
        => RunCoreAsync(classes, program, errorsOnly: false, modules: modules, programFirst: true);

    /// <summary>
    /// Loads the workspace of <paramref name="classes"/> and the <c>Program</c> standard module the way <see cref="RunAsync"/> does, and gives the
    /// errors that stopped a module from loading, instead of running it.
    /// </summary>
    /// <returns>The errors of every module, one each; empty when every module loaded.</returns>
    public static Task<string[]> LoadErrorsAsync(IReadOnlyList<(string Name, string Source)> classes, string program)
        => RunCoreAsync(classes, program, errorsOnly: true);

    /// <summary>
    /// The errors that stopped the <c>Program</c> standard module from loading, as <see cref="LoadErrorsAsync(IReadOnlyList{ValueTuple{string, string}}, string)"/>
    /// gives them, of a module that may have syntax errors, which the language server sends the host as it sends any other module.
    /// </summary>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    public static Task<string[]> LoadErrorsWithSyntaxErrorsAsync(string program)
        => RunCoreAsync([], program, errorsOnly: true, syntaxErrors: true);

    /// <summary>
    /// The libraries a workspace's project references, and the source they are found in. A library is known by its name, as the project's reference says.
    /// </summary>
    /// <param name="References">The names of the libraries the project references, in order, after the standard library.</param>
    /// <param name="Source">Where the descriptions of the libraries are.</param>
    /// <param name="Automation">What the objects of the libraries are held by: the servers of the machine unless a test brings its own.</param>
    /// <param name="AllowAutomation">Whether the environment lets a program use the objects of a library at all.</param>
    /// <param name="Outside">
    /// Everything a program reaches outside the platform, for a test that brings an external host of its own - one that it takes down, which the machine's is not
    /// to be for the tests that run alongside it.
    /// </param>
    public sealed record WorkspaceLibraries(
        IReadOnlyList<string> References, ILibrarySource Source, RDCore.External.Automation.IAutomationServer? Automation = null, bool AllowAutomation = true,
        RDCore.External.ExternalWorld? Outside = null);

    /// <summary>
    /// Loads the workspace like <see cref="LoadErrorsAsync(IReadOnlyList{ValueTuple{string, string}}, string)"/>, for a project that references libraries.
    /// </summary>
    public static Task<string[]> LoadErrorsAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program, WorkspaceLibraries libraries, SupportedLanguage? language = null)
        => RunCoreAsync(classes, program, errorsOnly: true, libraries: libraries, language: language);

    /// <summary>
    /// Loads the workspace like <see cref="LoadErrorsAsync(IReadOnlyList{ValueTuple{string, string}}, string)"/>, with document modules: <c>.doccls</c> files the
    /// project says are the code of a host's document (<c>Super</c>), loaded as the platform loads them; for a project that references libraries, or only the
    /// standard library when <paramref name="libraries"/> is <see langword="null"/>.
    /// </summary>
    public static Task<string[]> LoadDocumentsAsync(
        IReadOnlyList<(string Name, string Source, DocClassType? Super)> documents, string program, WorkspaceLibraries? libraries)
        => RunCoreAsync([], program, errorsOnly: true, libraries: libraries, documents: documents);

    /// <summary>
    /// Runs <c>Program.Main</c> like <see cref="RunAsync"/>, in a workspace that has document modules
    /// (<see cref="LoadDocumentsAsync"/>).
    /// </summary>
    public static Task<string[]> RunWithDocumentsAsync(
        IReadOnlyList<(string Name, string Source, DocClassType? Super)> documents, string program, WorkspaceLibraries? libraries)
        => RunCoreAsync([], program, errorsOnly: false, libraries: libraries, documents: documents);

    /// <summary>
    /// Runs <c>Program.Main</c> like <see cref="RunAsync"/>, for a project that references libraries.
    /// </summary>
    public static Task<string[]> RunAsync(IReadOnlyList<(string Name, string Source)> classes, string program, WorkspaceLibraries libraries)
        => RunCoreAsync(classes, program, errorsOnly: false, libraries: libraries);

    /// <summary>
    /// Runs <c>Program.Main</c> like <see cref="RunAsync(IReadOnlyList{ValueTuple{string, string}}, string, WorkspaceLibraries)"/>, in a workspace that has forms,
    /// whose parse has what their designer declares, as the language server's does; in RD-VBA unless <paramref name="language"/> says otherwise.
    /// </summary>
    public static Task<string[]> RunAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program, WorkspaceLibraries libraries, IReadOnlyList<(string Name, string Source, FormDescriptor Designer)> forms,
        SupportedLanguage? language = null)
        => RunCoreAsync(classes, program, errorsOnly: false, libraries: libraries, forms: forms, language: language);

    /// <summary>
    /// Runs <c>Program.Main</c> like <see cref="RunAsync"/>, in a workspace that has other standard modules besides <c>Program</c>, for a project that
    /// references libraries, or only the standard library when <paramref name="libraries"/> is <see langword="null"/>.
    /// </summary>
    public static Task<string[]> RunAsync(
        IReadOnlyList<(string Name, string Source)> classes, IReadOnlyList<(string Name, string Source)> modules, string program, WorkspaceLibraries? libraries)
        => RunCoreAsync(classes, program, errorsOnly: false, libraries: libraries, modules: modules);

    /// <summary>
    /// Runs <c>Program.Main</c> unattended, as the language server runs it for a client that expects <c>Unattended</c>: nobody is there to answer a dialog.
    /// What came of the run, whatever it was.
    /// </summary>
    /// <param name="classes">The class modules of the workspace.</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    /// <param name="libraries">The libraries the project references; only the standard library when omitted.</param>
    /// <param name="forms">The forms of the workspace, whose parse has what their designer declares.</param>
    public static async Task<ExecuteResult> RunUnattendedAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program, WorkspaceLibraries? libraries = null,
        IReadOnlyList<(string Name, string Source, FormDescriptor Designer)>? forms = null)
    {
        ExecuteResult? ran = null;
        await RunCoreAsync(classes, program, errorsOnly: false, inspect: (_, result, _) =>
        {
            ran = result;
            return Task.CompletedTask;
        }, libraries: libraries, forms: forms, unattended: true);
        return ran!;
    }

    /// <summary>
    /// Runs <c>Program.Main</c> of a module that may have syntax errors, which the language server sends the host as it sends any other module, and the host
    /// loads what the parser made of it. What came of the run, whatever it was.
    /// </summary>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    /// <param name="modules">The standard modules of the workspace other than <c>Program</c>, which may have syntax errors too.</param>
    public static async Task<ExecuteResult> RunWithSyntaxErrorsAsync(string program, IReadOnlyList<(string Name, string Source)>? modules = null)
    {
        ExecuteResult? ran = null;
        await RunCoreAsync([], program, errorsOnly: false, inspect: (_, result, _) =>
        {
            ran = result;
            return Task.CompletedTask;
        }, modules: modules, syntaxErrors: true);
        return ran!;
    }

    /// <summary>
    /// Loads the workspace like <see cref="LoadErrorsAsync"/>, and asks the host what the semantic analysis pass found out about it, as the language server does.
    /// </summary>
    /// <param name="classes">The class modules of the workspace.</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    /// <param name="moduleName">The module whose model is asked for, or empty for the models of every module.</param>
    /// <param name="language">The language the code is written in; RD-VBA unless said otherwise.</param>
    /// <param name="libraries">The libraries the project references, and the source they and others are found in; only the standard library when omitted.</param>
    /// <param name="modules">The standard modules of the workspace other than <c>Program</c> (<see cref="StandardModule"/>).</param>
    /// <param name="documents">The document modules of the workspace (<see cref="LoadDocumentsAsync"/>).</param>
    public static async Task<SemanticsPayload> SemanticsAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program, string moduleName = "", SupportedLanguage? language = null,
        WorkspaceLibraries? libraries = null, IReadOnlyList<(string Name, string Source, FormDescriptor Designer)>? forms = null,
        IReadOnlyList<(string Name, string Source)>? modules = null, IReadOnlyList<(string Name, string Source, DocClassType? Super)>? documents = null)
    {
        SemanticsPayload? payload = null;
        await RunCoreAsync(classes, program, errorsOnly: true, afterLoading: async sessionProvider =>
        {
            var result = await new HostSemanticsHandler(sessionProvider).Handle(new HostSemanticsParams { ModuleName = moduleName }, CancellationToken.None);
            payload = PlatformJson.Deserialize<SemanticsPayload>(result.Json);
        }, language: language, libraries: libraries, forms: forms, modules: modules, documents: documents);

        return payload!;
    }

    /// <summary>
    /// Loads the workspace like <see cref="LoadErrorsAsync"/>, and asks the host which of its modules declare a procedure the program can start with, as a
    /// debug adapter asks of a launch that names no module.
    /// </summary>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    /// <param name="classes">The class modules of the workspace.</param>
    /// <param name="modules">The standard modules of the workspace other than <c>Program</c> (<see cref="StandardModule"/>).</param>
    /// <param name="entryPoint">The name of the procedure; <c>Main</c> when omitted.</param>
    /// <param name="libraries">The libraries the project references; only the standard library when omitted.</param>
    /// <param name="forms">The forms of the workspace, whose parse has what their designer declares.</param>
    /// <param name="documents">The document modules of the workspace (<see cref="LoadDocumentsAsync"/>).</param>
    /// <returns>The modules the host answers with, every module of the workspace having been asked about.</returns>
    public static async Task<string[]> EntryPointsAsync(
        string program, IReadOnlyList<(string Name, string Source)>? classes = null, IReadOnlyList<(string Name, string Source)>? modules = null,
        string entryPoint = "Main", WorkspaceLibraries? libraries = null, IReadOnlyList<(string Name, string Source, FormDescriptor Designer)>? forms = null,
        IReadOnlyList<(string Name, string Source, DocClassType? Super)>? documents = null)
    {
        string[] asked =
        [
            .. (classes ?? []).Select(module => module.Name), .. (modules ?? []).Select(module => module.Name), .. (forms ?? []).Select(module => module.Name),
            .. (documents ?? []).Select(module => module.Name), "Program",
        ];

        string[] declaring = [];
        await RunCoreAsync(classes ?? [], program, errorsOnly: true, afterLoading: async sessionProvider =>
        {
            var result = await new HostEntryPointsHandler(sessionProvider).Handle(
                new HostEntryPointsParams { EntryPoint = entryPoint, Modules = asked }, CancellationToken.None);
            declaring = result.Modules;
        }, libraries: libraries, modules: modules, forms: forms, documents: documents);

        return declaring;
    }

    /// <summary>
    /// Loads the workspace like <see cref="LoadErrorsAsync"/>, and hands the host's session provider to <paramref name="inspect"/>.
    /// </summary>
    public static Task WithHostAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program, Func<EnvironmentSessionProvider, Task> inspect)
        => RunCoreAsync(classes, program, errorsOnly: true, afterLoading: inspect);

    /// <summary>
    /// Loads the workspace like <see cref="LoadErrorsAsync"/>, then runs a procedure of a module of the client's own, which the project does not list
    /// (<see cref="DefineSymbolsParams.SessionModule"/>), as the language server runs a line a shell attached to the workspace sends: its symbols are bound
    /// among the project's modules and defined in the session, and the host runs it from its parse.
    /// </summary>
    /// <param name="modules">The standard modules of the workspace other than <c>Program</c> (<see cref="StandardModule"/>).</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    /// <param name="moduleName">The name of the client's module.</param>
    /// <param name="source">The source of the client's module.</param>
    /// <param name="entryPoint">The procedure of the client's module that is run.</param>
    /// <param name="debug">Whether it runs under the debugger, as the shell runs a line: a <c>Stop</c> then leaves the program waiting.</param>
    /// <param name="afterRun">Given what came of the run and the host's session provider, while the session is up: a program that waits is looked at here.</param>
    /// <param name="errorBreak">Which run-time errors the program waits at, said to the session before it runs, as the shell says it.</param>
    /// <returns>What came of the run, whatever it was.</returns>
    public static async Task<ExecuteResult> RunSessionModuleAsync(
        IReadOnlyList<(string Name, string Source)> modules, string program, string moduleName, string source, string entryPoint, bool debug = false,
        Func<ExecuteResult, EnvironmentSessionProvider, Task>? afterRun = null, ErrorBreakMode errorBreak = ErrorBreakMode.None)
    {
        ExecuteResult? ran = null;
        await RunCoreAsync([], program, errorsOnly: true, modules: modules, afterLoading: async sessionProvider =>
        {
            _ = await new HostDebugErrorBreakHandler(sessionProvider).Handle(new HostDebugErrorBreakParams { Mode = errorBreak }, CancellationToken.None);

            var workspaceRoot = Wire.FileUri(Root);
            var moduleUri = new UriBuilder(workspaceRoot) { Fragment = moduleName }.Uri;
            var parse = new ModuleParser().Parse(moduleUri, source);
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

            // as SymbolSyncService.SyncModuleAsync binds it: among the project's modules, the client's own added to them.
            (Uri Uri, ModuleType Type, ModuleParseResult Parse)[] workspace =
            [
                .. modules.Append(("Program", program)).Select(module =>
                {
                    var uri = Wire.FileUri(Path.Combine(Root, $"{module.Item1}.bas"));
                    return (new UriBuilder(workspaceRoot) { Fragment = module.Item1 }.Uri, ModuleType.StdModule, new ModuleParser().Parse(uri, module.Item2));
                }),
                (moduleUri, ModuleType.StdModule, parse),
            ];
            var resolver = WorkspaceSymbolResolver.Compose(workspaceRoot, workspace, new IntrinsicSymbolResolver(), "Project1");
            var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, ModuleType.StdModule, parse, resolver, withImplicitDeclarations: true).ProvideSymbols();
            foreach (var declareTypesOnly in new[] { true, false })
            {
                await new DefineSymbolsHandler(sessionProvider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance)
                    .Handle(Wire.RoundTrip(new DefineSymbolsParams
                    {
                        WorkspaceRoot = workspaceRoot,
                        ModuleUri = moduleUri,
                        ModuleName = moduleName,
                        Symbols = SymbolDescriptorProjector.Project(symbols, moduleUri),
                        Directives = ModuleKinds.DirectivesOf(ModuleType.StdModule, parse.SyntaxTree!.GetModuleDirectives()),
                        Replace = true,
                        DeclareTypesOnly = declareTypesOnly,
                        SessionModule = true,
                    }), CancellationToken.None);
            }

            ran = await new HostExecuteHandler(sessionProvider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<HostExecuteHandler>.Instance)
                .Handle(new HostExecuteParams
                {
                    Json = PlatformJson.Serialize(new HostExecutePayload(moduleUri, parse)),
                    ModuleName = moduleName,
                    EntryPoint = entryPoint,
                    Debug = debug,
                }, CancellationToken.None);

            if (afterRun is not null)
            {
                await afterRun(ran, sessionProvider);
            }
        });

        return ran!;
    }

    /// <summary>
    /// Loads the workspace like <see cref="LoadErrorsAsync"/>, and gives the model of a module as the host keeps it, before it travels.
    /// </summary>
    /// <param name="classes">The class modules of the workspace.</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    /// <param name="moduleName">The module whose model is given.</param>
    /// <param name="language">The language the code is written in; RD-VBA unless said otherwise.</param>
    /// <param name="syntaxErrors">Whether a module may have syntax errors, which the language server sends the host as it sends any other module.</param>
    public static async Task<SDK.Semantics.ModuleSemanticModel> ModelAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program, string moduleName = "Program", SupportedLanguage? language = null,
        WorkspaceLibraries? libraries = null, bool syntaxErrors = false)
    {
        SDK.Semantics.ModuleSemanticModel? model = null;
        await RunCoreAsync(classes, program, errorsOnly: true, afterLoading: async sessionProvider =>
        {
            // the facts of the code are evaluated in the background, after the model is stored.
            await sessionProvider.Image.Semantics.WhenAllEvaluatedAsync();
            model = sessionProvider.Image.Semantics.All.Single(candidate => SDK.Model.Symbols.SymbolUri.ModuleNameOf(candidate.Module) == moduleName);
        }, language: language, libraries: libraries, syntaxErrors: syntaxErrors);

        return model!;
    }

    /// <summary>
    /// Runs <c>Program.Main</c> like <see cref="RunAsync"/>, and hands what came of it to <paramref name="inspect"/> instead of requiring that it completed: the
    /// outcome, whatever it was, the session it ran in, and a way to run the entry point again in that same session.
    /// </summary>
    /// <param name="classes">The class modules of the workspace.</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    /// <param name="inspect">Given the session provider, the result of the first run, and a function that runs the entry point again.</param>
    /// <param name="debug">Whether the program runs under a debugger: a <c>Stop</c> suspends it, and the first run answers that it waits.</param>
    /// <param name="cancelWhen">When the first run's request is cancelled: given the session before the program starts, a task that completes when the program
    /// is where the test wants it, as an event of the session says (<see cref="WhenRunning"/>, a dialog the program opens). Never, when omitted. A program that
    /// ends before it completes fails the test then, saying so.</param>
    /// <param name="interaction">Who answers the dialogs the program opens, as a client of the language server does; the platform's own when omitted.</param>
    /// <param name="language">The language the workspace is written in; RD-VBA when omitted.</param>
    /// <param name="configuration">Whether the program is built to be debugged or to be run; a debug build when omitted.</param>
    /// <param name="engine">What runs the program, whatever engine the suite runs with (<c>RDCORE_ENGINE</c>); the suite's when omitted.</param>
    /// <param name="releaseDropsDebugStatements">Whether a release build leaves out the <c>Debug</c> statements; it keeps them when omitted.</param>
    /// <param name="modules">The name and the source of each standard module beside <c>Program</c>; none when omitted.</param>
    /// <param name="environment">What else the environment says, given the profile the other arguments make; that profile when omitted.</param>
    /// <param name="forms">The name, the source and the designer of each form module of the workspace; none when omitted.</param>
    public static Task InspectAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program,
        Func<EnvironmentSessionProvider, ExecuteResult, Func<Task<ExecuteResult>>, Task> inspect, Func<EnvironmentSessionProvider, Task>? cancelWhen = null,
        bool debug = false,
        WorkspaceLibraries? libraries = null, SupportedLanguage? language = null, VBBuildConfiguration configuration = VBBuildConfiguration.Debug,
        VBExecutionEngine? engine = null, bool releaseDropsDebugStatements = false, IReadOnlyList<(string Name, string Source)>? modules = null,
        Func<RuntimeEnvironmentProfile, RuntimeEnvironmentProfile>? environment = null, IHostInteractionChannel? interaction = null,
        IReadOnlyList<(string Name, string Source, FormDescriptor Designer)>? forms = null)
        => RunCoreAsync(classes, program, errorsOnly: false, language: language, inspect: inspect, cancelWhen: cancelWhen, debug: debug, libraries: libraries,
            configuration: configuration, engine: engine, releaseDropsDebugStatements: releaseDropsDebugStatements, modules: modules, environment: environment,
            interaction: interaction, forms: forms);

    /// <summary>
    /// A task that completes once the program runs, as the session's execution says when its state changes: for <c>cancelWhen</c>, a break of a program that runs.
    /// </summary>
    /// <param name="provider">The session the program runs in, before it starts.</param>
    public static Task WhenRunning(EnvironmentSessionProvider provider)
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Execution.StateChanged += state =>
        {
            if (state is ProgramState.Running)
            {
                running.TrySetResult();
            }
        };

        // a program that already runs has said so before anything listened.
        if (provider.Execution.State is ProgramState.Running)
        {
            running.TrySetResult();
        }

        return running.Task;
    }

    // how long a program that was cancelled has to answer the cancel, which it does at its next instruction: the bound past which a program that never
    // answers fails its test, so that it does not leave the test waiting. A program that answers takes a fraction of it.
    private static readonly TimeSpan AnswerGuard = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Runs <c>Program.Main</c> like <see cref="InspectAsync"/>, in a workspace that has standard modules beside <c>Program</c>.
    /// </summary>
    /// <param name="modules">The name and the source of each standard module beside <c>Program</c>.</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    /// <param name="inspect">Given the session provider, the result of the first run, and a function that runs the entry point again.</param>
    /// <param name="debug">Whether the program runs under a debugger.</param>
    /// <param name="engine">What runs the program, whatever engine the suite runs with (<c>RDCORE_ENGINE</c>); the suite's when omitted.</param>
    public static Task InspectWithModulesAsync(
        IReadOnlyList<(string Name, string Source)> modules, string program,
        Func<EnvironmentSessionProvider, ExecuteResult, Func<Task<ExecuteResult>>, Task> inspect, bool debug = false,
        VBExecutionEngine? engine = null)
        => RunCoreAsync([], program, errorsOnly: false, inspect: inspect, debug: debug, modules: modules, engine: engine);

    /// <summary>
    /// Runs <c>Program.Main</c> like <see cref="RunAsync(IReadOnlyList{ValueTuple{string, string}}, string)"/>, with <paramref name="engine"/>, whatever engine
    /// the suite runs with.
    /// </summary>
    public static Task<string[]> RunAsync(IReadOnlyList<(string Name, string Source)> classes, string program, VBExecutionEngine engine)
        => RunCoreAsync(classes, program, errorsOnly: false, engine: engine);

    private static async Task<string[]> RunCoreAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program, bool errorsOnly, Func<EnvironmentSessionProvider, Task>? afterLoading = null,
        SupportedLanguage? language = null,
        Func<EnvironmentSessionProvider, ExecuteResult, Func<Task<ExecuteResult>>, Task>? inspect = null, Func<EnvironmentSessionProvider, Task>? cancelWhen = null,
        bool debug = false,
        WorkspaceLibraries? libraries = null, IReadOnlyList<(string Name, string Source)>? modules = null,
        IReadOnlyList<(string Name, string Source, FormDescriptor Designer)>? forms = null, bool programFirst = false,
        VBBuildConfiguration configuration = VBBuildConfiguration.Debug, VBExecutionEngine? engine = null,
        bool releaseDropsDebugStatements = false, IReadOnlyList<(string Name, string Source, DocClassType? Super)>? documents = null, bool unattended = false,
        bool syntaxErrors = false, Func<RuntimeEnvironmentProfile, RuntimeEnvironmentProfile>? environment = null,
        IHostInteractionChannel? interaction = null)
    {
        // the engine a test asks for is the one that runs its program, whichever the suite runs every other with: the program's runs, and what the test does
        // with the session while it waits, are in this flow.
        using var pinned = engine is { } asked ? RDCore.Runtime.Compiled.CompiledExecution.Running(asked) : null;
        var loadErrors = new List<string>();

        // the kind of a document module is what the project says, as the platform reads it: one it gives no Super is what its text says.
        var supers = (documents ?? []).Where(document => document.Super is not null)
            .ToDictionary(document => document.Name, document => document.Super!.Value, StringComparer.OrdinalIgnoreCase);
        (string Name, string Extension, ModuleType Type, string Source)[] others =
        [
            .. classes.Select(module => (module.Name, "cls", ModuleType.ClassModule, module.Source)),
            .. (documents ?? []).Select(module => (module.Name, "doccls", ModuleKinds.Of(module.Super, module.Source), module.Source)),
            .. (forms ?? []).Select(module => (module.Name, "frm", ModuleType.ClassModule, module.Source)),
            .. (modules ?? []).Select(module => (module.Name, "bas", ModuleType.StdModule, module.Source)),
        ];
        (string Name, string Extension, ModuleType Type, string Source) programModule = ("Program", "bas", ModuleType.StdModule, program);
        (string Name, string Extension, ModuleType Type, string Source)[] allModules = programFirst ? [programModule, .. others] : [.. others, programModule];

        var project = new ProjectFile(Root, new RDCoreProject
        {
            Name = "Project1",
            Modules = [.. allModules.Select(module => new RDCoreModule
            {
                RelativeUri = $"{module.Name}.{module.Extension}",
                Super = supers.TryGetValue(module.Name, out var super) ? super : null,
            })],
            References = [.. (libraries?.References ?? []).Select(name => new RDCoreReference { Name = name })],
        });
        var files = new Dictionary<string, MockFileData> { [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)) };
        foreach (var module in allModules)
        {
            files[Path.Combine(Root, $"{module.Name}.{module.Extension}")] = new(module.Source);
        }

        var profile = new RuntimeEnvironmentProfile(
            Is64Bit: true, 0, 1252, false, SourceLanguage: language, AllowAutomation: libraries?.AllowAutomation ?? true, Configuration: configuration,
            ExecutionEngine: engine ?? RDCore.SDK.Server.Configuration.SdkEnvironmentOptions.DefaultExecutionEngine,
            ReleaseDropsDebugStatements: releaseDropsDebugStatements);
        var sessionProvider = new EnvironmentSessionProvider(
            environment?.Invoke(profile) ?? profile,
            new MockFileSystem(files), NullLogger<EnvironmentSessionProvider>.Instance, libraries?.Source,
            // a test that brings its own servers brings no libraries of its own: those are the machine's still.
            libraries?.Outside ?? (libraries?.Automation is { } automation ? MachineExternal.World with { Automation = automation } : null));
        var workspaceRoot = Wire.FileUri(Root);
        sessionProvider.Compose(project.ProjectInfo, workspaceRoot);

        // the language server binds the workspace's names among the libraries the project references, as the host's session has them.
        var librarySymbols = libraries is null
            ? null
            : (IReadOnlyList<RDCore.SDK.Model.Symbols.Abstract.Symbol>)[.. new LibrarySymbolProvider(
                workspaceRoot, LibrarySymbolProvider.Load(libraries.Source, project.ProjectInfo.References, language)).ProvideSymbols()];
        var parsed = await DefineAsync(sessionProvider, project.ProjectInfo.Name,
            [.. allModules.Select(module => (module.Name, module.Extension, module.Type, module.Source))], forms, librarySymbols, documentHosts: supers,
            syntaxErrors: syntaxErrors);

        foreach (var module in parsed)
        {
            if (errorsOnly)
            {
                loadErrors.AddRange(module.CodeErrors);
                continue;
            }

            // a module with syntax errors may not load, and its run is what says so.
            Assert.IsTrue(syntaxErrors || module.CodeErrors.Count == 0, $"{module.Name}: {string.Join("; ", module.CodeErrors)}");
        }

        if (errorsOnly)
        {
            if (afterLoading is not null)
            {
                await afterLoading(sessionProvider);
            }

            return [.. loadErrors];
        }

        var entry = parsed.Single(module => module.Name == "Program");
        Task<ExecuteResult> Execute(CancellationToken cancellation)
            => new HostExecuteHandler(sessionProvider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<HostExecuteHandler>.Instance)
                .Handle(new HostExecuteParams
                {
                    Json = PlatformJson.Serialize(new HostExecutePayload(entry.Uri, entry.Parse)),
                    ModuleName = "Program",
                    EntryPoint = "Main",
                    Debug = debug,
                    Unattended = unattended,
                }, cancellation);

        // the first run's request is cancelled once the session says the program is where the test wants it, and not after a time, which a slower machine would
        // spend before the program even started. The test listens before the program starts, so that nothing it says is missed. The run is on a thread of its own:
        // without a debugger, a program runs to its end inside the request.
        sessionProvider.Interaction = interaction;
        using var cancellation = new CancellationTokenSource();
        var reached = cancelWhen?.Invoke(sessionProvider);
        var running = Task.Run(() => Execute(cancellation.Token));
        if (reached is not null)
        {
            if (await Task.WhenAny(reached, running) != reached)
            {
                var ended = await running;
                Assert.Fail($"the program ended ({ended.Outcome}) before it was where the test cancels it. {ended.ErrorMessage}");
            }

            await reached;
            await cancellation.CancelAsync();
            try
            {
                await running.WaitAsync(AnswerGuard);
            }
            catch (TimeoutException)
            {
                Assert.Fail("the program never answered the cancel: it was where the test cancels it, and it did not stop.");
            }
        }

        var result = await running;
        if (inspect is not null)
        {
            await inspect(sessionProvider, result, () => Execute(CancellationToken.None));
            return [.. result.Output.Select(line => line.Trim())];
        }

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, $"{result.ErrorMessage} {string.Join("; ", result.Diagnostics ?? [])}");
        return [.. result.Output.Select(line => line.Trim())];
    }

    /// <summary>
    /// Edits modules of a session as the analysis pass does with an edit of their documents: each is parsed again, its symbols are defined again and its
    /// code is loaded again, while a program may wait. What a program that waits is resumed on, or refused.
    /// </summary>
    /// <param name="provider">The session's provider, as a test that inspects a run is given it.</param>
    /// <param name="modules">Every module of the workspace, as it now reads: the names of each are bound among all of them.</param>
    /// <returns>What each module's code load said is wrong with it; none when it compiles.</returns>
    public static async Task<IReadOnlyList<string>> ReloadAsync(EnvironmentSessionProvider provider, params (string Name, ModuleType Type, string Source)[] modules)
    {
        var defined = await DefineAsync(provider, "Project1",
            [.. modules.Select(module => (module.Name, module.Type == ModuleType.ClassModule ? "cls" : "bas", module.Type, module.Source))], forms: null, librarySymbols: null);
        return [.. defined.SelectMany(module => module.CodeErrors)];
    }

    /// <summary>
    /// Edits modules of a session as the language server does with an edit of one document: the names of every module are bound among all of them, as the
    /// workspace now reads, and only the edited ones are defined again and have their code loaded again. The others are loaded later by the language server,
    /// after the edited module's diagnostics; until then the host has them as they were.
    /// </summary>
    /// <param name="provider">The session's provider, as a test that inspects a run is given it.</param>
    /// <param name="only">The names of the modules that are defined and loaded again.</param>
    /// <param name="modules">Every module of the workspace, as it now reads.</param>
    /// <returns>What each reloaded module's code load said is wrong with it; none when it compiles.</returns>
    public static Task<IReadOnlyList<string>> ReloadOnlyAsync(EnvironmentSessionProvider provider, IReadOnlyCollection<string> only,
        params (string Name, ModuleType Type, string Source)[] modules)
        => ReloadOnlyAsync(provider, only, syntaxErrors: false, modules);

    /// <summary>
    /// Edits modules of a session like <see cref="ReloadOnlyAsync(EnvironmentSessionProvider, IReadOnlyCollection{string}, ValueTuple{string, ModuleType, string}[])"/>,
    /// where a module may have syntax errors: the language server sends what the parser made of it all the same.
    /// </summary>
    /// <param name="provider">The session's provider, as a test that inspects a run is given it.</param>
    /// <param name="only">The names of the modules that are defined and loaded again.</param>
    /// <param name="syntaxErrors">Whether a module may have syntax errors.</param>
    /// <param name="modules">Every module of the workspace, as it now reads.</param>
    /// <returns>What each reloaded module's code load said is wrong with it; none when it compiles.</returns>
    public static async Task<IReadOnlyList<string>> ReloadOnlyAsync(EnvironmentSessionProvider provider, IReadOnlyCollection<string> only, bool syntaxErrors,
        params (string Name, ModuleType Type, string Source)[] modules)
    {
        var defined = await DefineAsync(provider, "Project1",
            [.. modules.Select(module => (module.Name, module.Type == ModuleType.ClassModule ? "cls" : "bas", module.Type, module.Source))], forms: null, librarySymbols: null,
            only: only, syntaxErrors: syntaxErrors);
        return [.. defined.SelectMany(module => module.CodeErrors)];
    }

    // as the language server does, in three phases: the types of every module are declared, then the declarations of every module are bound, and then the
    // code of each is sent. The names are bound among every module; only those named, when they are, are defined and loaded.
    private static async Task<(string Name, Uri Uri, ModuleParseResult Parse, IReadOnlyList<string> CodeErrors)[]> DefineAsync(
        EnvironmentSessionProvider sessionProvider, string projectName, (string Name, string Extension, ModuleType Type, string Source)[] modules,
        IReadOnlyList<(string Name, string Source, FormDescriptor Designer)>? forms, IReadOnlyList<RDCore.SDK.Model.Symbols.Abstract.Symbol>? librarySymbols,
        IReadOnlyCollection<string>? only = null, IReadOnlyDictionary<string, DocClassType>? documentHosts = null, bool syntaxErrors = false)
    {
        var workspaceRoot = Wire.FileUri(Root);
        var parsed = modules.Select(module =>
        {
            var parse = new ModuleParser().Parse(Wire.FileUri(Path.Combine(Root, $"{module.Name}.{module.Extension}")), module.Source);
            Assert.IsTrue(syntaxErrors || parse.IsSuccess, $"{module.Name}: {string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose))}");

            // as the language server does: the parse of a form has what its designer declares, which the parser does not read.
            parse = parse with { Designer = forms?.FirstOrDefault(form => form.Name == module.Name).Designer };
            return (Module: module, Uri: new UriBuilder(workspaceRoot) { Fragment = module.Name }.Uri, Parse: parse);
        }).ToArray();

        var resolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot, parsed.Select(module => (module.Uri, module.Module.Type, module.Parse)), new IntrinsicSymbolResolver(), projectName,
            libraries: librarySymbols, documentHosts: documentHosts);
        if (only is not null)
        {
            parsed = [.. parsed.Where(module => only.Contains(module.Module.Name))];
        }

        foreach (var declareTypesOnly in new[] { true, false })
        {
            foreach (var module in parsed)
            {
                var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, module.Uri, module.Module.Type, module.Parse, resolver, withImplicitDeclarations: true).ProvideSymbols();
                await new DefineSymbolsHandler(sessionProvider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance)
                    .Handle(Wire.RoundTrip(new DefineSymbolsParams
                    {
                        WorkspaceRoot = workspaceRoot,
                        ModuleUri = module.Uri,
                        ModuleName = module.Module.Name,
                        Symbols = SymbolDescriptorProjector.Project(symbols, module.Uri),
                        Directives = ModuleKinds.DirectivesOf(module.Module.Type, module.Parse.SyntaxTree!.GetModuleDirectives()),
                        ImplementedInterfaceNames = module.Parse.SyntaxTree!.GetImplementedInterfaceNames(),
                        ImplementedInterfaceRanges = module.Parse.SyntaxTree!.GetImplementedInterfaceRanges(),
                        PrecompilerTriviaJson = DefineSymbolsParams.PrecompilerTriviaJsonOf(module.Parse),
                        Replace = true,
                        DeclareTypesOnly = declareTypesOnly,
                    }), CancellationToken.None);
            }
        }

        var defined = new List<(string Name, Uri Uri, ModuleParseResult Parse, IReadOnlyList<string> CodeErrors)>();
        foreach (var module in parsed)
        {
            var loaded = await new DefineSymbolsHandler(sessionProvider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance)
                .Handle(new DefineSymbolsParams
                {
                    WorkspaceRoot = workspaceRoot,
                    ModuleUri = module.Uri,
                    ModuleName = module.Module.Name,
                    Directives = ModuleKinds.DirectivesOf(module.Module.Type, module.Parse.SyntaxTree.GetModuleDirectives()),
                    ImplementedInterfaceNames = module.Parse.SyntaxTree!.GetImplementedInterfaceNames(),
                    ImplementedInterfaceRanges = module.Parse.SyntaxTree!.GetImplementedInterfaceRanges(),
                    ParseResultJson = PlatformJson.Serialize(module.Parse),
                    CodeOnly = true,
                }, CancellationToken.None);
            defined.Add((module.Module.Name, module.Uri, module.Parse, [.. loaded.CodeErrors]));
        }

        return [.. defined];
    }
}
