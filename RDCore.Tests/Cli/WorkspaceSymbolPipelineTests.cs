using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Runtime;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// End-to-end over the whole symbol path minus the JSON-RPC transport: load a <c>.rdproj</c>, compose
/// the environment-host session, then for each module parse it, extract its symbols, project them to
/// descriptors, and hand them to the <c>rdcore/host/symbols/define</c> handler.
/// </summary>
[TestClass]
public sealed class WorkspaceSymbolPipelineTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-pipeline-ws");

    private const string Mod1 = "Attribute VB_Name = \"Mod1\"\r\n"
        + "Public Const MaxItems As Long = 10\r\n"
        + "Public Function Add(ByVal a As Long, ByVal b As Long) As Long\r\n"
        + "End Function\r\n";

    // Event is a declarations-section-only construct, so it must precede the Property block.
    private const string Class1 = "Attribute VB_Name = \"Class1\"\r\n"
        + "Private mWidget As Object\r\n"
        + "Public Event Changed(ByVal NewValue As Long)\r\n"
        + "Public Property Get Widget() As Object\r\n"
        + "End Property\r\n";

    private static MockFileSystem FileSystem()
    {
        var project = new ProjectFile(Root, new RDCoreProject
        {
            Modules =
            [
                new RDCoreModule { RelativeUri = "src/Mod1.bas" },
                new RDCoreModule { RelativeUri = "src/Class1.cls" },
            ],
        });

        return new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)),
            [Path.Combine(Root, "src", "Mod1.bas")] = new(Mod1),
            [Path.Combine(Root, "src", "Class1.cls")] = new(Class1),
        });
    }

    [TestMethod]
    public async Task LoadsComposesExtractsAndDefinesEveryModuleSymbol()
    {
        var fs = FileSystem();
        var project = await new ProjectFileLoader(fs).LoadAsync(Root);

        var sessionProvider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            NullLogger<EnvironmentSessionProvider>.Instance);
        sessionProvider.Compose(project.ProjectInfo, new Uri(project.Uri));

        var handler = new DefineSymbolsHandler(sessionProvider, NullLogger<DefineSymbolsHandler>.Instance);
        var resolver = new IntrinsicSymbolResolver();
        var parser = new ModuleParser();

        var results = new Dictionary<string, DefineSymbolsResult>();
        foreach (var module in project.ProjectInfo.Modules)
        {
            var name = module.DefaultName;
            var path = Path.Combine(Root, module.RelativeUri);
            var moduleType = path.EndsWith(".cls") ? ModuleType.ClassModule : ModuleType.StdModule;
            var workspaceRoot = new Uri(project.Uri);
            var moduleUri = new UriBuilder(workspaceRoot) { Fragment = name }.Uri;

            var parseResult = parser.Parse(new Uri(path), moduleType, fs.File.ReadAllText(path));
            var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, parseResult, resolver).ProvideSymbols();
            var descriptors = SymbolDescriptorProjector.Project(symbols, moduleUri);

            results[name] = await handler.Handle(new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = name,
                Symbols = descriptors,
            }, CancellationToken.None);
        }

        // Mod1: the Const and the Function.
        Assert.AreEqual(2, results["Mod1"].Defined);
        Assert.AreEqual(0, results["Mod1"].Skipped.Count);

        // Class1: the field, the Property Get, and the Event.
        Assert.AreEqual(3, results["Class1"].Defined,
            $"skipped=[{string.Join(",", results["Class1"].Skipped)}]");
        Assert.AreEqual(0, results["Class1"].Skipped.Count);
    }
}
