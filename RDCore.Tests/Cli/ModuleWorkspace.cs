using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

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

    /// <summary>
    /// Runs <c>Program.Main</c> of a workspace of <paramref name="classes"/> and the <c>Program</c> standard module whose source is <paramref name="program"/>.
    /// </summary>
    /// <returns>What it printed, a line each, trimmed.</returns>
    public static Task<string[]> RunAsync(IReadOnlyList<(string Name, string Source)> classes, string program)
        => RunCoreAsync(classes, program, errorsOnly: false);

    /// <summary>
    /// Loads the workspace of <paramref name="classes"/> and the <c>Program</c> standard module the way <see cref="RunAsync"/> does, and gives the
    /// errors that stopped a module from loading, instead of running it.
    /// </summary>
    /// <returns>The errors of every module, one each; empty when every module loaded.</returns>
    public static Task<string[]> LoadErrorsAsync(IReadOnlyList<(string Name, string Source)> classes, string program)
        => RunCoreAsync(classes, program, errorsOnly: true);

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
    public static Task<string[]> LoadErrorsAsync(IReadOnlyList<(string Name, string Source)> classes, string program, WorkspaceLibraries libraries)
        => RunCoreAsync(classes, program, errorsOnly: true, libraries: libraries);

    /// <summary>
    /// Runs <c>Program.Main</c> like <see cref="RunAsync"/>, for a project that references libraries.
    /// </summary>
    public static Task<string[]> RunAsync(IReadOnlyList<(string Name, string Source)> classes, string program, WorkspaceLibraries libraries)
        => RunCoreAsync(classes, program, errorsOnly: false, libraries: libraries);

    /// <summary>
    /// Loads the workspace like <see cref="LoadErrorsAsync"/>, and asks the host what the semantic analysis pass found out about it, as the language server does.
    /// </summary>
    /// <param name="classes">The class modules of the workspace.</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    /// <param name="moduleName">The module whose model is asked for, or empty for the models of every module.</param>
    /// <param name="language">The language the code is written in; RD-VBA unless said otherwise.</param>
    public static async Task<SemanticsPayload> SemanticsAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program, string moduleName = "", SupportedLanguage? language = null)
    {
        SemanticsPayload? payload = null;
        await RunCoreAsync(classes, program, errorsOnly: true, afterLoading: async sessionProvider =>
        {
            var result = await new HostSemanticsHandler(sessionProvider).Handle(new HostSemanticsParams { ModuleName = moduleName }, CancellationToken.None);
            payload = PlatformJson.Deserialize<SemanticsPayload>(result.Json);
        }, language: language);

        return payload!;
    }

    /// <summary>
    /// Loads the workspace like <see cref="LoadErrorsAsync"/>, and hands the host's session provider to <paramref name="inspect"/>.
    /// </summary>
    public static Task WithHostAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program, Func<EnvironmentSessionProvider, Task> inspect)
        => RunCoreAsync(classes, program, errorsOnly: true, afterLoading: inspect);

    /// <summary>
    /// Loads the workspace like <see cref="LoadErrorsAsync"/>, and gives the model of a module as the host keeps it, before it travels.
    /// </summary>
    /// <param name="classes">The class modules of the workspace.</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    /// <param name="moduleName">The module whose model is given.</param>
    /// <param name="language">The language the code is written in; RD-VBA unless said otherwise.</param>
    public static async Task<SDK.Semantics.ModuleSemanticModel> ModelAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program, string moduleName = "Program", SupportedLanguage? language = null,
        WorkspaceLibraries? libraries = null)
    {
        SDK.Semantics.ModuleSemanticModel? model = null;
        await RunCoreAsync(classes, program, errorsOnly: true, afterLoading: async sessionProvider =>
        {
            // the facts of the code are evaluated in the background, after the model is stored.
            await sessionProvider.Image.Semantics.WhenAllEvaluatedAsync();
            model = sessionProvider.Image.Semantics.All.Single(candidate => candidate.Module.Fragment.TrimStart('#') == moduleName);
        }, language: language, libraries: libraries);

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
    /// <param name="cancelAfter">How long the first run has before its request is cancelled, counted from when it starts and not from when the workspace
    /// began to be composed. Never, when omitted.</param>
    public static Task InspectAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program,
        Func<EnvironmentSessionProvider, ExecuteSessionResult, Func<Task<ExecuteSessionResult>>, Task> inspect, TimeSpan? cancelAfter = null, bool debug = false,
        WorkspaceLibraries? libraries = null)
        => RunCoreAsync(classes, program, errorsOnly: false, inspect: inspect, cancelAfter: cancelAfter, debug: debug, libraries: libraries);

    private static async Task<string[]> RunCoreAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program, bool errorsOnly, Func<EnvironmentSessionProvider, Task>? afterLoading = null,
        SupportedLanguage? language = null,
        Func<EnvironmentSessionProvider, ExecuteSessionResult, Func<Task<ExecuteSessionResult>>, Task>? inspect = null, TimeSpan? cancelAfter = null, bool debug = false,
        WorkspaceLibraries? libraries = null)
    {
        var loadErrors = new List<string>();
        (string Name, string Extension, ModuleType Type, string Source)[] modules =
        [
            .. classes.Select(module => (module.Name, "cls", ModuleType.ClassModule, module.Source)),
            ("Program", "bas", ModuleType.StdModule, program),
        ];

        var project = new ProjectFile(Root, new RDCoreProject
        {
            Name = "Project1",
            Modules = [.. modules.Select(module => new RDCoreModule { RelativeUri = $"{module.Name}.{module.Extension}" })],
            References = [RDCoreReference.VBStandardLibrary, .. (libraries?.References ?? []).Select(name => new RDCoreReference { Name = name })],
        });
        var files = new Dictionary<string, MockFileData> { [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)) };
        foreach (var module in modules)
        {
            files[Path.Combine(Root, $"{module.Name}.{module.Extension}")] = new(module.Source);
        }

        var sessionProvider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, SourceLanguage: language, AllowAutomation: libraries?.AllowAutomation ?? true),
            new MockFileSystem(files), NullLogger<EnvironmentSessionProvider>.Instance, libraries?.Source,
            // a test that brings its own servers brings no libraries of its own: those are the machine's still.
            libraries?.Outside ?? (libraries?.Automation is { } automation ? MachineExternal.World with { Automation = automation } : null));
        var workspaceRoot = new Uri(Root);
        sessionProvider.Compose(project.ProjectInfo, workspaceRoot);

        var parsed = modules.Select(module =>
        {
            var parse = new ModuleParser().Parse(new Uri(Path.Combine(Root, $"{module.Name}.{module.Extension}")), module.Source);
            Assert.IsTrue(parse.IsSuccess, $"{module.Name}: {string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose))}");
            return (Module: module, Uri: new UriBuilder(workspaceRoot) { Fragment = module.Name }.Uri, Parse: parse);
        }).ToArray();

        // the language server binds the workspace's names among the libraries the project references, as the host's session has them.
        var librarySymbols = libraries is null
            ? null
            : (IReadOnlyList<RDCore.SDK.Model.Symbols.Abstract.Symbol>)[.. new LibrarySymbolProvider(
                workspaceRoot, LibrarySymbolProvider.Load(libraries.Source, project.ProjectInfo.References)).ProvideSymbols()];
        var resolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot, parsed.Select(module => (module.Uri, module.Module.Type, module.Parse)), new IntrinsicSymbolResolver(), libraries: librarySymbols);

        // as the language server does: every module is defined, and then the code of each is sent.
        foreach (var module in parsed)
        {
            var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, module.Uri, module.Module.Type, module.Parse, resolver, withImplicitDeclarations: true).ProvideSymbols();
            await new DefineSymbolsHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance)
                .Handle(new DefineSymbolsParams
                {
                    WorkspaceRoot = workspaceRoot,
                    ModuleUri = module.Uri,
                    ModuleName = module.Module.Name,
                    Symbols = SymbolDescriptorProjector.Project(symbols, module.Uri),
                    Directives = module.Parse.SyntaxTree!.GetModuleDirectives(),
                    ImplementedInterfaceNames = module.Parse.SyntaxTree!.GetImplementedInterfaceNames(),
                    ImplementedInterfaceRanges = module.Parse.SyntaxTree!.GetImplementedInterfaceRanges(),
                    Replace = true,
                }, CancellationToken.None);
        }

        foreach (var module in parsed)
        {
            var defined = await new DefineSymbolsHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance)
                .Handle(new DefineSymbolsParams
                {
                    WorkspaceRoot = workspaceRoot,
                    ModuleUri = module.Uri,
                    ModuleName = module.Module.Name,
                    Directives = module.Parse.SyntaxTree.GetModuleDirectives(),
                    ImplementedInterfaceNames = module.Parse.SyntaxTree!.GetImplementedInterfaceNames(),
                    ImplementedInterfaceRanges = module.Parse.SyntaxTree!.GetImplementedInterfaceRanges(),
                    ParseResultJson = PlatformJson.Serialize(module.Parse),
                    CodeOnly = true,
                }, CancellationToken.None);

            if (errorsOnly)
            {
                loadErrors.AddRange(defined.CodeErrors);
                continue;
            }

            Assert.IsEmpty(defined.CodeErrors, $"{module.Module.Name}: {string.Join("; ", defined.CodeErrors)}");
        }

        if (errorsOnly)
        {
            if (afterLoading is not null)
            {
                await afterLoading(sessionProvider);
            }

            return [.. loadErrors];
        }

        var entry = parsed.Single(module => module.Module.Name == "Program");
        Task<ExecuteSessionResult> Execute(CancellationToken cancellation)
            => new HostExecuteHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<HostExecuteHandler>.Instance)
                .Handle(new HostExecuteParams
                {
                    Json = PlatformJson.Serialize(new HostExecutePayload(entry.Uri, entry.Parse)),
                    ModuleName = "Program",
                    EntryPoint = "Main",
                    Debug = debug,
                }, cancellation);

        using var cancellation = new CancellationTokenSource();
        if (cancelAfter is { } timeout)
        {
            cancellation.CancelAfter(timeout);
        }

        var result = await Execute(cancellation.Token);
        if (inspect is not null)
        {
            await inspect(sessionProvider, result, () => Execute(CancellationToken.None));
            return [.. result.Output.Select(line => line.Trim())];
        }

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, $"{result.ErrorMessage} {string.Join("; ", result.Diagnostics ?? [])}");
        return [.. result.Output.Select(line => line.Trim())];
    }
}
