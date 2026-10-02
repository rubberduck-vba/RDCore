using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// Runs a workspace of several modules the way the platform does - the language server's composition over every module, the host's session, each module's symbols and code
/// defined in it, then the entry point run - and gives what it printed.
/// </summary>
internal static class ModuleWorkspace
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-module-workspace");

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

    private static async Task<string[]> RunCoreAsync(
        IReadOnlyList<(string Name, string Source)> classes, string program, bool errorsOnly, Func<EnvironmentSessionProvider, Task>? afterLoading = null,
        SupportedLanguage? language = null)
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
        });
        var files = new Dictionary<string, MockFileData> { [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)) };
        foreach (var module in modules)
        {
            files[Path.Combine(Root, $"{module.Name}.{module.Extension}")] = new(module.Source);
        }

        var sessionProvider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, SourceLanguage: language), new MockFileSystem(files), NullLogger<EnvironmentSessionProvider>.Instance);
        var workspaceRoot = new Uri(Root);
        sessionProvider.Compose(project.ProjectInfo, workspaceRoot);

        var parsed = modules.Select(module =>
        {
            var parse = new ModuleParser().Parse(new Uri(Path.Combine(Root, $"{module.Name}.{module.Extension}")), module.Source);
            Assert.IsTrue(parse.IsSuccess, $"{module.Name}: {string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose))}");
            return (Module: module, Uri: new UriBuilder(workspaceRoot) { Fragment = module.Name }.Uri, Parse: parse);
        }).ToArray();

        var resolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot, parsed.Select(module => (module.Uri, module.Module.Type, module.Parse)), new IntrinsicSymbolResolver());

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
                    Directives = module.Parse.SyntaxTree.GetModuleDirectives(),
                    ImplementedInterfaceNames = module.Parse.SyntaxTree.GetImplementedInterfaceNames(),
                    ImplementedInterfaceRanges = module.Parse.SyntaxTree.GetImplementedInterfaceRanges(),
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
                    ImplementedInterfaceNames = module.Parse.SyntaxTree.GetImplementedInterfaceNames(),
                    ImplementedInterfaceRanges = module.Parse.SyntaxTree.GetImplementedInterfaceRanges(),
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
        var result = await new HostExecuteHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<HostExecuteHandler>.Instance)
            .Handle(new HostExecuteParams
            {
                Json = PlatformJson.Serialize(new HostExecutePayload(entry.Uri, entry.Parse)),
                ModuleName = "Program",
                EntryPoint = "Main",
            }, CancellationToken.None);

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, $"{result.ErrorMessage} {string.Join("; ", result.Diagnostics ?? [])}");
        return [.. result.Output.Select(line => line.Trim())];
    }
}
