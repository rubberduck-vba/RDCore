using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Cli;
using RDCore.Tests.Platform;
using RDCore.Runtime.LanguageCore.Refactoring;

namespace RDCore.Tests.Model.Symbols;

/// <summary>
/// A symbol's address is its identity (<see cref="Symbol.SemanticId"/>), and no two declarations of one module share one. An Enum, a Type and an Event may
/// take the name of a procedure, a variable or a constant of their module (<strong>MS-VBAL §5.2.3.1</strong> to <strong>§5.2.3.4</strong>,
/// <strong>§5.2.4.3</strong>): they are addressed with a suffix after the name, after a delimiter no name has (<c>PT]Enum</c>, <c>PT]Type</c>,
/// <c>PT]Event</c>), and their members under it. A name may be the suffix's word itself: only what follows the delimiter is a suffix.
/// </summary>
[TestClass]
public sealed class SymbolIdentityTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-symbol-identity-ws");

    private const string Enum = "Public Enum PT\r\n    Red\r\nEnd Enum";
    private const string Type = "Private Type PT\r\n    X As Long\r\nEnd Type";
    private const string Event = "Public Event PT(ByVal arg As Long)";

    private static IReadOnlyList<Symbol> SymbolsOf(string name, ModuleType moduleType, string source)
    {
        var workspaceRoot = Wire.FileUri(Root);
        var moduleUri = SymbolUri.Module(workspaceRoot, name);
        var parse = new ModuleParser().Parse(Wire.FileUri(Path.Combine(Root, $"{name}.cls")), source);
        var resolver = WorkspaceSymbolResolver.Compose(workspaceRoot, [(moduleUri, moduleType, parse)], new IntrinsicSymbolResolver());
        // a procedure's and an event's parameters are carried on it, and addressed under it.
        return [.. new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, moduleType, parse, resolver).ProvideSymbols()
            .SelectMany(symbol => symbol switch
            {
                VBProcedureMemberSymbol procedure => [symbol, .. procedure.Parameters],
                VBEventMemberSymbol @event => [symbol, .. @event.Parameters],
                _ => new[] { symbol },
            })];
    }

    private static IReadOnlyList<Symbol> SymbolsOfClass(params string[] declarations)
        => SymbolsOf("Cls", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Cls", ["Option Explicit", .. declarations]));

    // the ids two or more of the symbols share, each with the kinds of symbol that share it.
    private static string[] SharedIds(IEnumerable<Symbol> symbols)
        => [.. symbols.GroupBy(symbol => symbol.SemanticId).Where(group => group.Count() > 1)
            .Select(group => $"{SymbolUri.PathOf(group.Key.Uri)}: {string.Join(", ", group.Select(symbol => symbol.GetType().Name))}")];

    [TestMethod]
    [DataRow(Enum, "Public Property Get PT() As Long\r\nEnd Property", DisplayName = "Enum, Property Get")]
    [DataRow(Enum, "Public Function PT() As Long\r\nEnd Function", DisplayName = "Enum, Function")]
    [DataRow(Enum, "Public Sub PT()\r\nEnd Sub", DisplayName = "Enum, Sub")]
    [DataRow(Enum, "Private Declare PtrSafe Function PT Lib \"kernel32\" () As Long", DisplayName = "Enum, Declare Function")]
    [DataRow(Enum, "Public PT As Long", DisplayName = "Enum, module variable")]
    [DataRow(Enum, "Private Const PT As Long = 1", DisplayName = "Enum, Const")]
    [DataRow(Type, "Public Property Get PT() As Long\r\nEnd Property", DisplayName = "Type, Property Get")]
    [DataRow(Type, "Public Function PT() As Long\r\nEnd Function", DisplayName = "Type, Function")]
    [DataRow(Type, "Public Sub PT()\r\nEnd Sub", DisplayName = "Type, Sub")]
    [DataRow(Type, "Private Declare PtrSafe Function PT Lib \"kernel32\" () As Long", DisplayName = "Type, Declare Function")]
    [DataRow(Type, "Public PT As Long", DisplayName = "Type, module variable")]
    [DataRow(Type, "Private Const PT As Long = 1", DisplayName = "Type, Const")]
    [DataRow(Event, "Public Property Get PT() As Long\r\nEnd Property", DisplayName = "Event, Property Get")]
    [DataRow(Event, "Public Function PT() As Long\r\nEnd Function", DisplayName = "Event, Function")]
    [DataRow(Event, "Public Sub PT()\r\nEnd Sub", DisplayName = "Event, Sub")]
    [DataRow(Event, "Private Declare PtrSafe Function PT Lib \"kernel32\" () As Long", DisplayName = "Event, Declare Function")]
    [DataRow(Event, "Public PT As Long", DisplayName = "Event, module variable")]
    [DataRow(Event, "Private Const PT As Long = 1", DisplayName = "Event, Const")]
    public void TwoDeclarationsOfOneNameInAModule_HaveTwoSemanticIds(string typeOrEvent, string valueDeclaration)
    {
        var symbols = SymbolsOfClass(typeOrEvent, valueDeclaration);

        Assert.HasCount(2, symbols.Where(symbol => symbol.Name == "PT"), "both declarations are symbols");
        CollectionAssert.AreEqual(Array.Empty<string>(), SharedIds(symbols));
    }

    [TestMethod]
    [DataRow(Enum, "Red", DisplayName = "an enum member")]
    [DataRow(Type, "X", DisplayName = "a field of a type")]
    [DataRow(Event, "arg", DisplayName = "a parameter of an event")]
    public void AMemberOfATypeOrEventAndALocalOfAProcedureOfItsName_HaveTwoSemanticIds(string typeOrEvent, string memberName)
    {
        var symbols = SymbolsOfClass(typeOrEvent, $"Public Sub PT()\r\n    Dim {memberName} As Long\r\nEnd Sub");

        Assert.HasCount(2, symbols.Where(symbol => symbol.Name == memberName), "the member and the local are both symbols");
        CollectionAssert.AreEqual(Array.Empty<string>(), SharedIds(symbols));
    }

    [TestMethod]
    [DataRow(Enum, "Cls.PT]Enum", "Cls.PT]Enum.Red", DisplayName = "Enum")]
    [DataRow(Type, "Cls.PT]Type", "Cls.PT]Type.X", DisplayName = "Type")]
    [DataRow(Event, "Cls.PT]Event", "Cls.PT]Event.arg", DisplayName = "Event")]
    public void ATypeOrEvent_IsAddressedWithASuffixAfterItsName_ItsMembersUnderIt(string typeOrEvent, string path, string memberPath)
    {
        var symbols = SymbolsOfClass(typeOrEvent);

        CollectionAssert.Contains(symbols.Select(symbol => SymbolUri.PathOf(symbol.Uri)).ToArray(), path);
        CollectionAssert.Contains(symbols.Select(symbol => SymbolUri.PathOf(symbol.Uri)).ToArray(), memberPath);
        Assert.IsTrue(symbols.Where(symbol => SymbolUri.PathOf(symbol.Uri) is var p && (p == path || p == memberPath))
            .All(symbol => SymbolUri.NameOf(symbol.Uri) == symbol.Name), "the name read from the address is the symbol's own");
    }

    [TestMethod]
    [DataRow("Cls.Item", "Item", "Cls.Item", DisplayName = "a Property Get")]
    [DataRow("Cls.Item]Let", "Item", "Cls.Item", DisplayName = "a Property Let")]
    [DataRow("Cls.Item]Set", "Item", "Cls.Item", DisplayName = "a Property Set")]
    [DataRow("Cls.Item]Let.value", "value", "Cls.Item.value", DisplayName = "a parameter of a Property Let")]
    [DataRow("Cls.PT]Enum", "PT", "Cls.PT", DisplayName = "an Enum")]
    [DataRow("Cls.PT]Enum.Red", "Red", "Cls.PT.Red", DisplayName = "an enum member")]
    [DataRow("Cls.Tok]Type.Type", "Type", "Cls.Tok.Type", DisplayName = "a field of a Type named Type")]
    [DataRow("Cls.Tok]Type.Let", "Let", "Cls.Tok.Let", DisplayName = "a field of a Type named Let")]
    [DataRow("Worksheet.Type", "Type", "Worksheet.Type", DisplayName = "a library's member named Type")]
    [DataRow("Shapes.AddShape.Type", "Type", "Shapes.AddShape.Type", DisplayName = "a library's parameter named Type")]
    [DataRow("Cls.a[b]c", "a[b]c", "Cls.a[b]c", DisplayName = "a foreign name with brackets of its own")]
    [DataRow("Cls.a[b]c]Let", "a[b]c", "Cls.a[b]c", DisplayName = "a Property Let of a foreign name with brackets of its own")]
    [DataRow("Cls", "Cls", "Cls", DisplayName = "a module")]
    [DataRow("", "", "", DisplayName = "a root")]
    public void TheNameOfASymbol_IsTheLastNameOfItsAddress_WithoutTheSuffixAfterIt(string path, string name, string qualifiedName)
    {
        var address = new UriBuilder(Wire.FileUri(Root)) { Fragment = path }.Uri;

        Assert.AreEqual(name, SymbolUri.NameOf(address));
        Assert.AreEqual(qualifiedName, SymbolUri.QualifiedNameOf(address));
    }

    [TestMethod]
    public void TheFieldsOfATypeNamedTypeAndLet_AreNamedSo()
    {
        var fields = SymbolsOfClass("Private Type Tok\r\n    Type As Long\r\n    Let As Long\r\nEnd Type").OfType<VBUserDefinedTypeFieldSymbol>().ToList();

        CollectionAssert.AreEqual(new[] { "Type", "Let" }, fields.Select(field => SymbolUri.NameOf(field.Uri)).ToArray());
        CollectionAssert.AreEqual(new[] { "Cls.Tok.Type", "Cls.Tok.Let" }, fields.Select(field => SymbolUri.QualifiedNameOf(field.Uri)).ToArray());
    }

    [TestMethod]
    public void AGetAndALetOfAForeignNameWithBracketsOfItsOwn_AreNamedSo()
    {
        var properties = SymbolsOfClass("Public Property Get [a[b]c]() As Long\r\nEnd Property\r\nPublic Property Let [a[b]c](ByVal v As Long)\r\nEnd Property")
            .Where(symbol => symbol is VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol).ToList();

        Assert.HasCount(2, properties);
        Assert.AreEqual(2, properties.Select(property => property.Uri.AbsoluteUri).Distinct().Count(), "two addresses");
        CollectionAssert.AreEqual(new[] { "a[b]c", "a[b]c" }, properties.Select(property => SymbolUri.NameOf(property.Uri)).ToArray());
    }

    [TestMethod]
    public void ExcelsWorksheetType_IsNamedType_AndAHoverSaysItIsAMemberOfTheWorksheet()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
        {
            directory = directory.Parent;
        }

        var libraries = ReferencedLibraries.Load(
            new DirectoryLibrarySource(new System.IO.Abstractions.FileSystem(), Path.Combine(directory!.FullName, "Symbols")), ["Excel"], [StdLibSymbolProvider.LibraryName]);
        var symbols = new LibrarySymbolProvider(Wire.FileUri(Root), libraries).ProvideSymbols().ToList();
        var worksheet = symbols.OfType<VBClassModuleSymbol>().Single(symbol => symbol.Name == "Worksheet");
        var type = worksheet.DefaultInterfaceMembers.Single(member => member.Name == "Type");

        Assert.AreEqual("Type", SymbolUri.NameOf(type.Uri));
        Assert.AreEqual("Excel.Worksheet", SymbolHover.Of(type).Container);
    }

    [TestMethod]
    public void NoTwoDeclarationsOfAModuleOfTheBenchCorpus_ShareASemanticId()
    {
        var assembly = typeof(DefineSymbolsHandler).Assembly;
        var modules = assembly.GetManifestResourceNames().Where(resource => resource.StartsWith("Bench/Corpus/", StringComparison.Ordinal)).ToList();
        Assert.IsNotEmpty(modules);

        foreach (var resource in modules)
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            var name = Path.GetFileNameWithoutExtension(resource);
            var symbols = SymbolsOf(name, resource.EndsWith(".cls", StringComparison.Ordinal) ? ModuleType.ClassModule : ModuleType.StdModule, reader.ReadToEnd());

            CollectionAssert.AreEqual(Array.Empty<string>(), SharedIds(symbols), resource);
        }
    }

    // IPlayer.cls of OOPBattleship, as it is written: an Enum, then a Property Get of the Enum's name and type.
    private static string Player(string getBody) => ModuleWorkspace.ClassModule("Player",
        "Public Enum PlayerType", "    HumanControlled", "    ComputerControlled", "End Enum", "",
        "Option Explicit", "",
        "Public Property Get PlayerType() As PlayerType", getBody, "End Property");

    private static async Task<(EnvironmentSessionProvider Provider, DefineSymbolsResult Result)> DefineClassAsync(string name, string source)
    {
        var project = new ProjectFile(Root, new RDCoreProject { Name = "Project1", Modules = [new RDCoreModule { RelativeUri = $"{name}.cls" }] });
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)) });
        var provider = new EnvironmentSessionProvider(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), fs, NullLogger<EnvironmentSessionProvider>.Instance);
        var workspaceRoot = Wire.FileUri(Root);
        provider.Compose(project.ProjectInfo, workspaceRoot);

        var moduleUri = SymbolUri.Module(workspaceRoot, name);
        var parse = new ModuleParser().Parse(Wire.FileUri(Path.Combine(Root, $"{name}.cls")), source);
        var resolver = WorkspaceSymbolResolver.Compose(workspaceRoot, [(moduleUri, ModuleType.ClassModule, parse)], new IntrinsicSymbolResolver());
        var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, ModuleType.ClassModule, parse, resolver).ProvideSymbols();

        var result = await new DefineSymbolsHandler(provider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance)
            .Handle(Wire.RoundTrip(new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = name,
                Symbols = SymbolDescriptorProjector.Project(symbols, moduleUri),
                Replace = true,
            }), CancellationToken.None);
        return (provider, result);
    }

    [TestMethod]
    public async Task AnEnumAndAPropertyGetOfItsName_AreBothDefinedInTheHost()
    {
        var (provider, result) = await DefineClassAsync("Player", Player(""));

        CollectionAssert.AreEqual(Array.Empty<string>(), result.Skipped.ToArray(), "neither is a name declared twice");
        Assert.IsTrue(provider.Session.Symbols.TryGetModule("Player", out var module));
        var members = provider.Session.Symbols.MembersOf(module.Uri).Where(member => member.Name == "PlayerType").ToList();
        Assert.ContainsSingle(members.OfType<VBEnumMemberSymbol>());
        Assert.ContainsSingle(members.OfType<VBPropertyGetMemberSymbol>());
    }

    [TestMethod]
    public async Task AnEventAndASubOfItsName_AreBothDefinedInTheHost()
    {
        // an Event is defined with the procedures, not with the types: of two declarations of one address, the second was a name declared twice.
        var (provider, result) = await DefineClassAsync("Notifier", ModuleWorkspace.ClassModule("Notifier",
            "Option Explicit", "Public Event Changed(ByVal value As Long)", "Public Sub Changed()", "End Sub"));

        CollectionAssert.AreEqual(Array.Empty<string>(), result.Skipped.ToArray(), "neither is a name declared twice");
        Assert.IsTrue(provider.Session.Symbols.TryGetModule("Notifier", out var module));
        var members = provider.Session.Symbols.MembersOf(module.Uri).Where(member => member.Name == "Changed").ToList();
        Assert.ContainsSingle(members.OfType<VBEventMemberSymbol>());
        Assert.ContainsSingle(members.OfType<VBProcedureMemberSymbol>());
    }

    private const string ReadsThePlayerType = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Dim p As New Player\r\n"
        + "    Debug.Print p.PlayerType\r\n    Debug.Print p.PlayerType = HumanControlled\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task APropertyGetOfAnEnumsName_IsAMemberOfTheClass()
        => CollectionAssert.AreEqual(new[] { "0", "True" }, await ModuleWorkspace.RunAsync([("Player", Player(""))], ReadsThePlayerType));

    [TestMethod]
    public async Task APropertyGetOfAnEnumsName_AssignsItsResultByItsName_AndReturnsAValueOfTheEnum()
        // MS-VBAL §5.3.1: the name is the Get's result variable, a local of the Get, and is bound in the Get before the module's Enum is a candidate.
        => CollectionAssert.AreEqual(new[] { "1", "False" },
            await ModuleWorkspace.RunAsync([("Player", Player("    PlayerType = ComputerControlled"))], ReadsThePlayerType));
}
