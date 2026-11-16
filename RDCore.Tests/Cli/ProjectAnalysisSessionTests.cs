using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host.Handlers;
using RDCore.Parsing;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdcore/host/project/analyze</c> and <c>/release</c>: a project of the workspace the host does not run is analyzed in a session of its own, which holds
/// its modules and no other project's, and whose models are asked for by the address of a module.
/// </summary>
[TestClass]
public sealed class ProjectAnalysisSessionTests
{
    private static readonly string App = Path.Combine(Path.GetTempPath(), "rdcore-analysis-ws", "App");
    private static readonly string Tools = Path.Combine(Path.GetTempPath(), "rdcore-analysis-ws", "Tools");

    // each project has a module named Shared, and a module of its own; each Shared declares a function the other's does not.
    private static readonly (string Root, string Name, string Source)[] Modules =
    [
        (App, "Shared", "Attribute VB_Name = \"Shared\"\r\nOption Explicit\r\nPublic Function FromApp() As Long\r\nFromApp = 1\r\nEnd Function\r\n"),
        (App, "AppOnly", "Attribute VB_Name = \"AppOnly\"\r\nOption Explicit\r\nPublic Sub Main()\r\nDebug.Print FromApp\r\nEnd Sub\r\n"),
        (Tools, "Shared", "Attribute VB_Name = \"Shared\"\r\nOption Explicit\r\nPublic Function FromTools() As Long\r\nFromTools = 2\r\nEnd Function\r\n"),
        (Tools, "ToolsOnly", "Attribute VB_Name = \"ToolsOnly\"\r\nOption Explicit\r\nPublic Sub Run()\r\nDebug.Print FromTools\r\nEnd Sub\r\n"),
    ];

    private readonly MockFileSystem _files = new();
    private readonly EnvironmentSessionProvider _runtime;
    private readonly ProjectAnalysisSessions _analyses;

    public ProjectAnalysisSessionTests()
    {
        foreach (var (root, name, source) in Modules)
        {
            _files.AddFile(Path.Combine(root, name + ".bas"), new MockFileData(source));
        }

        WriteProject(App, "App", "Shared.bas", "AppOnly.bas");
        WriteProject(Tools, "Tools", "Shared.bas", "ToolsOnly.bas");
        var profile = new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false);
        _runtime = new EnvironmentSessionProvider(profile, _files, NullLogger<EnvironmentSessionProvider>.Instance);
        _analyses = new ProjectAnalysisSessions(profile, _files, NullLogger<ProjectAnalysisSessions>.Instance);
    }

    private void WriteProject(string root, string name, params string[] modules)
        => _files.AddFile(Path.Combine(root, ProjectFile.FileName), new MockFileData(JsonSerializer.Serialize(new ProjectFile(root, new RDCoreProject
        {
            Name = name,
            Modules = [.. modules.Select(module => new RDCoreModule { RelativeUri = module })],
        }))));

    // the host as it initializes: the runtime session is composed of the project it runs.
    private async Task ComposeAppAsync()
    {
        var project = await new ProjectFileLoader(_files).LoadAsync(App);
        _runtime.Compose(project.ProjectInfo, SymbolUri.Root(project.Uri));
    }

    private Task<HostProjectAnalyzeResult> AnalyzeAsync(string root)
        => new HostProjectAnalyzeHandler(_runtime, _analyses, new ProjectFileLoader(_files), Microsoft.Extensions.Options.Options.Create(new RDCore.SDK.Server.Configuration.SdkAppOptions()),
            NullLogger<HostProjectAnalyzeHandler>.Instance).Handle(new HostProjectAnalyzeParams { WorkspaceRoot = SymbolUri.Root(root) }, CancellationToken.None);

    // what the language server does for a project: its modules are defined under its root, in the phases, their code loaded.
    private async Task DefineAsync(string root)
    {
        var workspaceRoot = SymbolUri.Root(root);
        var parsed = Modules.Where(module => module.Root == root).Select(module => (
            Uri: SymbolUri.Module(workspaceRoot, module.Name), module.Name,
            Parse: new ModuleParser().Parse(new Uri(Path.Combine(root, module.Name + ".bas")), module.Source))).ToList();
        var resolver = WorkspaceSymbolResolver.Compose(workspaceRoot, parsed.Select(module => (module.Uri, ModuleType.StdModule, module.Parse)), new IntrinsicSymbolResolver());

        var define = new DefineSymbolsHandler(_runtime, _analyses, Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance);
        foreach (var codeOnly in new[] { false, true })
        {
            foreach (var module in parsed)
            {
                var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, module.Uri, ModuleType.StdModule, module.Parse, resolver, withImplicitDeclarations: true).ProvideSymbols();
                await define.Handle(new DefineSymbolsParams
                {
                    WorkspaceRoot = workspaceRoot,
                    ModuleUri = module.Uri,
                    ModuleName = module.Name,
                    Symbols = codeOnly ? [] : SymbolDescriptorProjector.Project(symbols, module.Uri),
                    Directives = module.Parse.SyntaxTree.GetModuleDirectives(),
                    ParseResultJson = codeOnly ? PlatformJson.Serialize(module.Parse) : string.Empty,
                    CodeOnly = codeOnly,
                }, CancellationToken.None);
            }
        }
    }

    private async Task<string[]> ModelsAtAsync(string root, string module)
    {
        var result = await new HostSemanticsHandler(_runtime, _analyses).Handle(
            new HostSemanticsParams { ModuleName = module, ModuleUri = SymbolUri.Module(root, module) }, CancellationToken.None);
        return [.. PlatformJson.Deserialize<SemanticsPayload>(result.Json).Modules.Select(model => model.Module.AbsoluteUri)];
    }

    private static string[] MembersOf(SDK.Runtime.Abstract.Execution.IRuntimeSession session, string root, string module)
        => [.. session.Symbols.MembersOf(SymbolUri.Module(root, module)).Select(member => member.Name).Order(StringComparer.OrdinalIgnoreCase)];

    [TestMethod]
    public async Task AnotherProject_IsAnalyzedInASessionOfItsOwn_AndItsModuleIsAskedForByItsAddress()
    {
        await ComposeAppAsync();
        await DefineAsync(App);

        var analyzed = await AnalyzeAsync(Tools);
        await DefineAsync(Tools);

        Assert.IsTrue(analyzed.Composed);
        Assert.AreEqual(2, analyzed.ModuleCount);
        CollectionAssert.AreEqual(new[] { SymbolUri.Module(Tools, "Shared").AbsoluteUri }, await ModelsAtAsync(Tools, "Shared"));
        CollectionAssert.AreEqual(new[] { SymbolUri.Module(App, "Shared").AbsoluteUri }, await ModelsAtAsync(App, "Shared"));
    }

    [TestMethod]
    public async Task AProject_IsANamespaceOfItsOwn_EachWay()
    {
        await ComposeAppAsync();
        await DefineAsync(App);
        await AnalyzeAsync(Tools);
        await DefineAsync(Tools);
        Assert.IsTrue(_analyses.TryGet(SymbolUri.Root(Tools), out var tools));

        // the runtime session has the modules of the project it runs, and none of the other's.
        Assert.IsTrue(_runtime.Session.Symbols.TryGetModule("AppOnly", out _));
        Assert.IsFalse(_runtime.Session.Symbols.TryGetModule("ToolsOnly", out _));
        CollectionAssert.AreEqual(new[] { "FromApp" }, MembersOf(_runtime.Session, App, "Shared"));

        // and the analysis session the other way around: its Shared is its own.
        Assert.IsTrue(tools.Session.Symbols.TryGetModule("ToolsOnly", out _));
        Assert.IsFalse(tools.Session.Symbols.TryGetModule("AppOnly", out _));
        CollectionAssert.AreEqual(new[] { "FromTools" }, MembersOf(tools.Session, Tools, "Shared"));
    }

    [TestMethod]
    public async Task TheProjectTheHostRuns_IsNotAnalyzedApart()
    {
        await ComposeAppAsync();

        var analyzed = await AnalyzeAsync(App);

        Assert.IsFalse(analyzed.Composed);
        Assert.IsEmpty(_analyses.All);
    }

    [TestMethod]
    public async Task NothingIsAnalyzed_BeforeTheRuntimeSessionIsComposed()
    {
        var analyzed = await AnalyzeAsync(Tools);

        Assert.IsFalse(analyzed.Composed);
        Assert.IsEmpty(_analyses.All);
    }

    [TestMethod]
    public async Task AReleasedProject_HasNoModel_AndReleasingItAgainIsNoError()
    {
        await ComposeAppAsync();
        await AnalyzeAsync(Tools);
        await DefineAsync(Tools);
        var release = new HostProjectReleaseHandler(_analyses, NullLogger<HostProjectReleaseHandler>.Instance);

        var first = await release.Handle(new HostProjectReleaseParams { WorkspaceRoot = SymbolUri.Root(Tools) }, CancellationToken.None);
        var again = await release.Handle(new HostProjectReleaseParams { WorkspaceRoot = SymbolUri.Root(Tools) }, CancellationToken.None);

        Assert.IsTrue(first.Released);
        Assert.IsFalse(again.Released);
        Assert.IsEmpty(await ModelsAtAsync(Tools, "Shared"));
    }

    [TestMethod]
    public async Task AModuleOfAProjectThatIsNotAnalyzed_HasNoModel_WhateverTheRuntimeSessionHasOfItsName()
    {
        await ComposeAppAsync();
        await DefineAsync(App);

        Assert.IsEmpty(await ModelsAtAsync(Tools, "Shared"));
    }

    [TestMethod]
    public async Task DiscardingByTheAddressOfAnAnalyzedModule_ChangesNeitherSession()
    {
        await ComposeAppAsync();
        await DefineAsync(App);
        await AnalyzeAsync(Tools);
        await DefineAsync(Tools);
        Assert.IsTrue(_analyses.TryGet(SymbolUri.Root(Tools), out var tools));

        await new HostDiscardHandler(_runtime, NullLogger<HostDiscardHandler>.Instance).Handle(
            new HostDiscardParams { ModuleUri = SymbolUri.Module(Tools, "Shared"), ModuleName = "Shared" }, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "FromApp" }, MembersOf(_runtime.Session, App, "Shared"));
        CollectionAssert.AreEqual(new[] { "FromTools" }, MembersOf(tools.Session, Tools, "Shared"));
    }
}
