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

namespace RDCore.Tests.Cli;

/// <summary>
/// A program whose procedures live in several modules, run in the environment host the way the platform runs one: the
/// workspace's modules are defined with their code, and a module is then run that calls into them. Without the
/// code the host keeps of the modules it is not asked to run, the class members an object is made of have nothing to run.
/// </summary>
[TestClass]
public sealed class ClassModuleHostExecutionTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-classes-ws");

    private const string ClassHeader = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\n";

    private const string IShape = ClassHeader + "Attribute VB_Name = \"IShape\"\r\n"
        + "Public Function Area() As Double\r\nEnd Function\r\n";

    private const string Disc = ClassHeader + "Attribute VB_Name = \"Disc\"\r\n"
        + "Implements IShape\r\n"
        + "Public Radius As Long\r\n"
        + "Public Cells(1 To 3) As Long\r\n"
        + "Private Function IShape_Area() As Double\r\n    IShape_Area = Radius * 2\r\nEnd Function\r\n";

    private static string Program(params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n" + string.Join("\r\n", body) + "\r\nEnd Sub\r\n";

    private static async Task<(ExecuteSessionResult Result, EnvironmentSessionProvider Session)> RunAsync(string program, bool defineWithCode = true)
    {
        (string Name, string Source, ModuleType Type)[] sources =
        [
            ("IShape", IShape, ModuleType.ClassModule),
            ("Disc", Disc, ModuleType.ClassModule),
            ("Program", program, ModuleType.StdModule),
        ];

        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(new ProjectFile(Root, new RDCoreProject
            {
                Name = "Program",
                Modules = [.. sources.Select(module => new RDCoreModule { RelativeUri = $"{module.Name}{(module.Type == ModuleType.ClassModule ? ".cls" : ".bas")}" })],
            }))),
        });
        foreach (var (name, source, type) in sources)
        {
            fs.AddFile(Path.Combine(Root, $"{name}{(type == ModuleType.ClassModule ? ".cls" : ".bas")}"), new MockFileData(source));
        }

        var project = await new ProjectFileLoader(fs).LoadAsync(Root);
        var sessionProvider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), fs, NullLogger<EnvironmentSessionProvider>.Instance);
        var workspaceRoot = new Uri(project.Uri);
        sessionProvider.Compose(project.ProjectInfo, workspaceRoot);

        var parsed = sources.Select(module =>
        {
            var uri = new UriBuilder(workspaceRoot) { Fragment = module.Name }.Uri;
            var extension = module.Type == ModuleType.ClassModule ? ".cls" : ".bas";
            return (Uri: uri, module.Name, module.Type, Parse: new ModuleParser().Parse(new Uri(Path.Combine(Root, module.Name + extension)), module.Source));
        }).ToList();
        foreach (var module in parsed)
        {
            Assert.IsTrue(module.Parse.IsSuccess, string.Join("; ", module.Parse.SyntaxErrors.Select(error => error.Verbose)));
        }

        var resolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot, parsed.Select(module => (module.Uri, module.Type, module.Parse)), new IntrinsicSymbolResolver());

        var define = new DefineSymbolsHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance);
        foreach (var module in parsed)
        {
            var symbols = new SyntaxTreeSymbolProvider(
                workspaceRoot, module.Uri, module.Type, module.Parse, resolver, withImplicitDeclarations: true).ProvideSymbols();

            // the program being run is defined without code, as the platform does: it arrives with the request to run it.
            var withCode = defineWithCode && module.Name != "Program";
            var defined = await define.Handle(new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = module.Uri,
                ModuleName = module.Name,
                Symbols = SymbolDescriptorProjector.Project(symbols, module.Uri),
                Directives = module.Parse.SyntaxTree.GetModuleDirectives(),
                ImplementedInterfaceNames = module.Parse.SyntaxTree?.GetImplementedInterfaceNames() ?? [],
                ParseResultJson = withCode ? PlatformJson.Serialize(module.Parse) : string.Empty,
            }, CancellationToken.None);
            Assert.AreEqual(0, defined.CodeErrors.Count, string.Join("; ", defined.CodeErrors));
        }

        var main = parsed.Single(module => module.Name == "Program");
        var result = await new HostExecuteHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<HostExecuteHandler>.Instance)
            .Handle(new HostExecuteParams
            {
                Json = PlatformJson.Serialize(new HostExecutePayload(main.Uri, main.Parse)),
                ModuleName = "Program",
                EntryPoint = "Main",
            }, CancellationToken.None);
        return (result, sessionProvider);
    }

    [TestMethod]
    public async Task AMemberOfAClassDefinedWithItsCode_RunsWhenAProgramCallsIt()
    {
        var (result, _) = await RunAsync(Program(
            "Dim d As Disc",
            "Set d = New Disc",
            "d.Radius = 21",
            "Debug.Print d.Radius"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { " 21 " }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AFixedSizeArrayOfAClass_IsAsBigAsItsBoundsSay_ForEachObject()
    {
        var (result, _) = await RunAsync(Program(
            "Dim a As Disc",
            "Dim b As Disc",
            "Set a = New Disc",
            "Set b = New Disc",
            "a.Cells(3) = 5",
            "b.Cells(3) = 6",
            "Debug.Print UBound(a.Cells)",
            "Debug.Print a.Cells(3)",
            "Debug.Print b.Cells(3)"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "3", "5", "6" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task ACallThroughAnInterface_RunsTheImplementationOfTheClassOfTheObject()
    {
        var (result, _) = await RunAsync(Program(
            "Dim d As Disc",
            "Dim s As IShape",
            "Set d = New Disc",
            "d.Radius = 21",
            "Set s = d",
            "Debug.Print s.Area()"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome,
            $"{result.ErrorMessage} @{result.ErrorLine}:{result.ErrorCharacter} [{string.Join(" <- ", result.StackTrace.Select(frame => frame.Procedure))}]");
        CollectionAssert.AreEqual(new[] { " 42 " }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AClassDefinedWithoutItsCode_HasNothingToRun()
    {
        var (result, _) = await RunAsync(Program(
            "Dim d As Disc",
            "Dim s As IShape",
            "Set d = New Disc",
            "Set s = d",
            "Debug.Print s.Area()"), defineWithCode: false);

        Assert.AreEqual(ExecutionOutcome.NotImplemented, result.Outcome);
    }

    [TestMethod]
    public async Task TheCodeOfAClassModule_IsKeyedByTheMembersItDeclares()
    {
        var (_, session) = await RunAsync(Program("Debug.Print 1"));

        var members = session.Session.Symbols.MembersOf(new UriBuilder(new Uri(session.Session.Symbols.Resolver.ResolveType("Disc", RDCore.SDK.Model.Symbols.Abstract.ScopeKind.Unallocated, new Uri(Root + "/")).Symbol!.Uri.AbsoluteUri)).Uri);
        var implementation = members.Single(member => member.Name == "IShape_Area");
        Assert.IsTrue(session.Image.ContainsKey(implementation.SemanticId), string.Join("\n", session.Image.Keys));
    }
}
