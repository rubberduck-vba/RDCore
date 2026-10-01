using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The symbol path for a class module that implements another (MS-VBAL 5.2.4.2): what the language server
/// reads off the <c>Implements</c> directive travels in the descriptors' request, and the environment host's
/// class module symbol ends up as the class its members declare, holding the interfaces it implements.
/// </summary>
[TestClass]
public sealed class ImplementedInterfacesPipelineTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-implements-ws");

    private const string ClassHeader = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\n";

    private const string IShape = ClassHeader + "Attribute VB_Name = \"IShape\"\r\n"
        + "Public Function Area() As Double\r\nEnd Function\r\n";

    private const string Disc = ClassHeader + "Attribute VB_Name = \"Disc\"\r\n"
        + "Implements IShape\r\n"
        + "Public Radius As Double\r\n"
        + "Private Function IShape_Area() As Double\r\nEnd Function\r\n";

    private static async Task<IRuntimeSession> DefineAsync(params string[] moduleNamesInOrder)
    {
        var sources = new Dictionary<string, string> { ["IShape"] = IShape, ["Disc"] = Disc };
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(new ProjectFile(Root, new RDCoreProject
            {
                Modules = [.. sources.Keys.Select(name => new RDCoreModule { RelativeUri = $"src/{name}.cls" })],
            }))),
            [Path.Combine(Root, "src", "IShape.cls")] = new(IShape),
            [Path.Combine(Root, "src", "Disc.cls")] = new(Disc),
        });

        var project = await new ProjectFileLoader(fs).LoadAsync(Root);
        var sessionProvider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), fs, NullLogger<EnvironmentSessionProvider>.Instance);
        sessionProvider.Compose(project.ProjectInfo, new Uri(project.Uri));

        var handler = new DefineSymbolsHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance);
        var workspaceRoot = new Uri(project.Uri);
        foreach (var name in moduleNamesInOrder)
        {
            var path = Path.Combine(Root, "src", $"{name}.cls");
            var parseResult = new ModuleParser().Parse(new Uri(path), sources[name]);
            var moduleUri = new UriBuilder(workspaceRoot) { Fragment = name }.Uri;
            var symbols = new SyntaxTreeSymbolProvider(
                workspaceRoot, moduleUri, ModuleType.ClassModule, parseResult, new IntrinsicSymbolResolver()).ProvideSymbols();

            await handler.Handle(new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = name,
                Symbols = SymbolDescriptorProjector.Project(symbols, moduleUri),
                ImplementedInterfaceNames = parseResult.SyntaxTree?.GetImplementedInterfaceNames() ?? [],
            }, CancellationToken.None);
        }

        return sessionProvider.Session;
    }

    private static VBClassModuleSymbol ClassModule(IRuntimeSession session, string name)
    {
        Assert.IsTrue(session.Symbols.TryResolveType(name, GlobalSymbols.UnresolvedSymbol, out var symbol), $"{name} is not defined");
        return (VBClassModuleSymbol)symbol!;
    }

    // every class module also implements the language's own lifecycle interface; these are the ones the source declares.
    private static IEnumerable<VBClassModuleSymbol> DeclaredInterfaces(VBClassModuleSymbol module)
        => module.ImplementedInterfaces.Where(implemented => implemented.Uri.AbsoluteUri != ClassLifecycleInterface.Interface.Uri.AbsoluteUri);

    [DataTestMethod]
    [DataRow("IShape", "Disc")]
    [DataRow("Disc", "IShape")]
    public async Task AClassModule_ImplementsTheInterfaceItsDirectiveNames_WhicheverModuleIsDefinedFirst(string first, string second)
    {
        var session = await DefineAsync(first, second);

        var disc = ClassModule(session, "Disc");
        Assert.AreEqual("IShape", disc.ImplementedInterfaceNames.Single());
        Assert.AreEqual("IShape", DeclaredInterfaces(disc).Single().Name);
    }

    [TestMethod]
    public async Task AClassModule_HoldsTheMembersItDeclares_AndTheInterfaceHoldsItsOwn()
    {
        var session = await DefineAsync("IShape", "Disc");

        var discMembers = ClassModule(session, "Disc").Members.Select(member => member.Name).ToList();
        CollectionAssert.AreEquivalent(new[] { "Radius", "IShape_Area" }, discMembers);
        CollectionAssert.AreEquivalent(new[] { "Area" }, ClassModule(session, "IShape").Members.Select(member => member.Name).ToList());
    }

    [TestMethod]
    public async Task TheDefaultInterface_IsThePublicMembersOfTheClass()
    {
        var session = await DefineAsync("IShape", "Disc");

        CollectionAssert.AreEqual(
            new[] { "Radius" },
            ClassModule(session, "Disc").DefaultInterfaceMembers.Select(member => member.Name).ToList());
    }

    [TestMethod]
    public async Task AClassModule_ThatImplementsNothing_ImplementsNoInterface()
    {
        var session = await DefineAsync("IShape", "Disc");

        Assert.AreEqual(0, DeclaredInterfaces(ClassModule(session, "IShape")).Count());
    }
}
