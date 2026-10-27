using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdcore/host/project/reload</c>: the environment host composes its runtime session again from the project file as it is now, so that a class module the
/// project gained since start-up is a class of the session - which defining its members alone does not make it.
/// </summary>
[TestClass]
public sealed class HostProjectReloadHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-project-reload-ws");

    private const string Disc = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Disc\"\r\nPublic Radius As Long\r\n";

    private const string Program = "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDim d As Disc\r\nSet d = New Disc\r\nd.Radius = 21\r\nDebug.Print d.Radius\r\nEnd Sub\r\n";

    private readonly MockFileSystem _files = new();
    private readonly EnvironmentSessionProvider _session;
    private readonly HostProjectReloadHandler _sut;

    public HostProjectReloadHandlerTests()
    {
        _files.AddFile(Path.Combine(Root, "Program.bas"), new MockFileData(Program));
        _files.AddFile(Path.Combine(Root, "Disc.cls"), new MockFileData(Disc));
        _session = new EnvironmentSessionProvider(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), _files, NullLogger<EnvironmentSessionProvider>.Instance);
        _sut = new HostProjectReloadHandler(_session, new ProjectFileLoader(_files), Microsoft.Extensions.Options.Options.Create(new RDCore.SDK.Server.Configuration.SdkAppOptions()),
            NullLogger<HostProjectReloadHandler>.Instance);
    }

    private void WriteProject(params string[] modules)
        => _files.AddFile(Path.Combine(Root, ProjectFile.FileName), new MockFileData(JsonSerializer.Serialize(new ProjectFile(Root, new RDCoreProject
        {
            Name = "Program",
            Modules = [.. modules.Select(module => new RDCoreModule { RelativeUri = module })],
        }))));

    // the session as the host composes it when it initializes, from the project file as it is then.
    private async Task ComposeAsync(params string[] modules)
    {
        WriteProject(modules);
        var project = await new ProjectFileLoader(_files).LoadAsync(Root);
        _session.Compose(project.ProjectInfo, new Uri(project.Uri));
    }

    // what the language server does after a reload: every module defined, in the phases, then Main run.
    private async Task<ExecuteSessionResult> DefineAndRunAsync()
    {
        var workspaceRoot = new Uri((await new ProjectFileLoader(_files).LoadAsync(Root)).Uri);
        (string Name, string File, ModuleType Type)[] sources = [("Disc", "Disc.cls", ModuleType.ClassModule), ("Program", "Program.bas", ModuleType.StdModule)];
        var parsed = sources.Select(module => (
            Uri: new UriBuilder(workspaceRoot) { Fragment = module.Name }.Uri, module.Name, module.Type,
            Parse: new ModuleParser().Parse(new Uri(Path.Combine(Root, module.File)), _files.File.ReadAllText(Path.Combine(Root, module.File))))).ToList();
        var resolver = WorkspaceSymbolResolver.Compose(workspaceRoot, parsed.Select(module => (module.Uri, module.Type, module.Parse)), new IntrinsicSymbolResolver());

        var define = new DefineSymbolsHandler(_session, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance);
        foreach (var module in parsed)
        {
            var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, module.Uri, module.Type, module.Parse, resolver, withImplicitDeclarations: true).ProvideSymbols();
            await define.Handle(new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = module.Uri,
                ModuleName = module.Name,
                Symbols = SymbolDescriptorProjector.Project(symbols, module.Uri),
                Directives = module.Parse.SyntaxTree.GetModuleDirectives(),
                ParseResultJson = module.Name == "Program" ? string.Empty : PlatformJson.Serialize(module.Parse),
            }, CancellationToken.None);
        }

        var main = parsed.Single(module => module.Name == "Program");
        return await new HostExecuteHandler(_session, Substitute.For<IVerboseMessageBuilder>(), NullLogger<HostExecuteHandler>.Instance)
            .Handle(new HostExecuteParams
            {
                Json = PlatformJson.Serialize(new HostExecutePayload(main.Uri, main.Parse)),
                ModuleName = "Program",
                EntryPoint = "Main",
            }, CancellationToken.None);
    }

    [TestMethod]
    public async Task AClassTheProjectGained_IsAClassOfTheSession_OnceItIsComposedAgain()
    {
        await ComposeAsync("Program.bas");
        WriteProject("Program.bas", "Disc.cls");

        var reloaded = await _sut.Handle(new HostProjectReloadParams { WorkspaceRoot = new Uri(Root) }, CancellationToken.None);
        var result = await DefineAndRunAsync();

        Assert.IsTrue(reloaded.Composed);
        Assert.AreEqual(2, reloaded.ModuleCount);
        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { " 21 " }, result.Output.ToArray());
    }

    // what the reload is for: the members of a class the session was not composed with are defined, and make no class of it.
    [TestMethod]
    public async Task AClassTheProjectGained_IsNoClassOfTheSession_UntilItIsComposedAgain()
    {
        await ComposeAsync("Program.bas");
        WriteProject("Program.bas", "Disc.cls");

        var result = await DefineAndRunAsync();

        Assert.AreNotEqual(ExecutionOutcome.Completed, result.Outcome);
    }

    // the same program, the class listed from the start: what a reload is to make of the session.
    [TestMethod]
    public async Task AClassTheProjectListedAtStartUp_IsAClassOfTheSession()
    {
        await ComposeAsync("Program.bas", "Disc.cls");

        var result = await DefineAndRunAsync();

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { " 21 " }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task ASessionNotComposedYet_IsNotComposedByAReload()
    {
        WriteProject("Program.bas");

        var reloaded = await _sut.Handle(new HostProjectReloadParams { WorkspaceRoot = new Uri(Root) }, CancellationToken.None);

        Assert.IsFalse(reloaded.Composed);
        Assert.IsFalse(_session.IsComposed);
    }
}
