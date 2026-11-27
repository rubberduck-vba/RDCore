using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using RDCore.Runtime.LanguageCore.Refactoring;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// A call through an interface (<strong>MS-VBAL §5.2.4.2</strong>) binds to the interface's member: it is a reference of that member, and of none of the
/// procedures that implement it. Go to Definition of the call leads to the member, and Go to Implementation to the procedures. The lenses say both: how many
/// procedures implement a member of an interface, and which member a procedure implements (<strong>MS-VBAL §5.3.1.9</strong>).
/// </summary>
[TestClass]
public sealed class InterfaceCallReferencesTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static string Class(string name, params string[] lines)
        => Lines(["VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", $"Attribute VB_Name = \"{name}\"", .. lines]);

    private static readonly string IShape = Class("IShape", "Option Explicit",
        "Public Function Area() As Double",
        "End Function",
        "Public Property Get Name() As String",
        "End Property",
        "Public Property Let Name(ByVal value As String)",
        "End Property");

    private static readonly string Square = Class("Square", "Option Explicit", "Implements IShape",
        "Private Function IShape_Area() As Double",
        "    IShape_Area = 4",
        "End Function",
        "Private Property Get IShape_Name() As String",
        "End Property",
        "Private Property Let IShape_Name(ByVal value As String)",
        "End Property");

    private static readonly string Disc = Class("Disc", "Option Explicit", "Implements IShape",
        "Private Function IShape_Area() As Double",
        "End Function",
        "Private Property Get IShape_Name() As String",
        "End Property",
        "Private Property Let IShape_Name(ByVal value As String)",
        "End Property");

    private static readonly string Program = Lines("Attribute VB_Name = \"Program\"", "Option Explicit",
        "Public Sub Main()",
        "    Dim s As IShape",
        "    Set s = New Square",
        "    Debug.Print s.Area",
        "    s.Name = \"sq\"",
        "    Debug.Print s.Name",
        "End Sub");

    private static RenameContext Workspace()
    {
        (string Name, string Source)[] modules = [("IShape", IShape), ("Square", Square), ("Disc", Disc), ("Program", Program)];
        var parsed = modules.Select(module =>
        {
            var isClass = module.Source.StartsWith("VERSION", StringComparison.Ordinal);
            var parse = new ModuleParser().Parse(new Uri($"file:///c:/ws/{module.Name}{(isClass ? ".cls" : ".bas")}"), module.Source);
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
            return (Uri: ModuleUri(module.Name), Type: isClass ? ModuleType.ClassModule : ModuleType.StdModule, Parse: parse, module.Source);
        }).ToList();

        var libraries = new LibrarySymbolProvider(Root, ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName]))
            .ProvideSymbols();
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [.. parsed.Select(module => (module.Uri, module.Type, module.Parse))],
            new IntrinsicSymbolResolver(), libraries: [.. libraries]);
        return new RenameContext(composition.Resolver, composition.ScopeTree,
            [.. parsed.Select(module => new RenameModule(module.Uri, module.Parse.SyntaxTree!, module.Source))]);
    }

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    // where `text` begins on the first line of the module that has `line`.
    private static SourcePosition At(RenameContext context, string module, string line, string text)
    {
        var lines = context.Modules.Single(candidate => SymbolUri.ModuleNameOf(candidate.Module) == module).Source.Split("\r\n");
        var index = Array.FindIndex(lines, candidate => candidate.Contains(line, StringComparison.Ordinal));
        Assert.IsTrue(index >= 0, $"no line has '{line}'");
        return new SourcePosition(index, lines[index].IndexOf(text, StringComparison.Ordinal));
    }

    // each site as the module and the trimmed line it is on.
    private static string[] Described(RenameContext context, IEnumerable<SymbolSite> sites)
        => [.. sites.Select(site =>
            {
                var source = context.Modules.Single(candidate => candidate.Module.AbsoluteUri == site.Module.AbsoluteUri).Source.Split("\r\n");
                return $"{SymbolUri.ModuleNameOf(site.Module)}: {source[site.Range.Start.Line].Trim()}";
            })
            .Order(StringComparer.Ordinal)];

    private static string[] LensOf(RenameContext context, string module, string line, string text)
        => Described(context, SymbolLenses.ReferencesOf(context, ModuleUri(module), At(context, module, line, text)));

    private static string[] ReferencesOf(RenameContext context, string module, string line, string text, bool includeDeclaration = true)
        => Described(context, Found(
            (at, module, position, declared) => SymbolNavigation.ReferencesAt(at, module, position, declared),
            context, module, line, text, includeDeclaration).Sites);

    private static SymbolNavigationTarget Found(Func<RenameContext, Uri, SourcePosition, bool, SymbolNavigationTarget?> navigate, RenameContext context,
        string module, string line, string text, bool includeDeclaration = true)
    {
        var position = At(context, module, line, text);
        var found = navigate(context, ModuleUri(module), new SourcePosition(position.Line, position.Character + 1), includeDeclaration);
        Assert.IsNotNull(found, $"'{text}' on '{line}' names nothing");
        return found;
    }

    // each lens of a module as the trimmed line of its member, what it is, and of an Implements lens, the member and where it is declared.
    private static string[] LensesOf(RenameContext context, string module)
    {
        var source = context.Modules.Single(candidate => SymbolUri.ModuleNameOf(candidate.Module) == module);
        var lines = source.Source.Split("\r\n");
        return [.. SymbolLenses.In(context, source).Select(lens => $"{lines[lens.NameRange.Start.Line].Trim()}: {lens.Kind}"
            + (lens.Implemented is { } implemented
                ? $" {implemented.Interface}.{implemented.Member}, at {Described(context, [implemented.Declaration]).Single()}"
                : string.Empty))];
    }

    [TestMethod]
    public void TheReferencesOfAnInterfaceMember_AreTheCallsThroughIt_AndItsDeclaration_AndNoImplementation()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { "IShape: Public Function Area() As Double", "Program: Debug.Print s.Area" },
            ReferencesOf(context, "Program", "Debug.Print s.Area", "Area"));
        CollectionAssert.AreEqual(new[] { "Program: Debug.Print s.Area" },
            ReferencesOf(context, "Program", "Debug.Print s.Area", "Area", includeDeclaration: false));
    }

    [TestMethod]
    [DataRow("Square", "Square: IShape_Area = 4", DisplayName = "the implementation of the class the variable is set to, which assigns its result")]
    [DataRow("Disc", null, DisplayName = "another implementation")]
    public void TheReferencesOfAnImplementation_AreItsOwn_AndNoCallThroughItsInterface(string implementation, string? result)
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { $"{implementation}: Private Function IShape_Area() As Double" }.Concat(result is null ? [] : [result])
                .Order(StringComparer.Ordinal).ToArray(),
            ReferencesOf(context, implementation, "Private Function IShape_Area", "IShape_Area"), "its declaration, and the result its body assigns");
        Assert.IsEmpty(LensOf(context, implementation, "Private Function IShape_Area", "IShape_Area"));
    }

    [TestMethod]
    public void TheLensOfAnInterfaceMember_CountsTheCallsThroughIt()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { "Program: Debug.Print s.Area" }, LensOf(context, "IShape", "Public Function Area", "Area"));
    }

    [TestMethod]
    public void TheLensesOfAPropertyOfAnInterface_CountTheCallsOfEachOfItsProcedures_Apart()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { "Program: Debug.Print s.Name" }, LensOf(context, "IShape", "Public Property Get Name", "Name"));
        CollectionAssert.AreEqual(new[] { "Program: s.Name = \"sq\"" }, LensOf(context, "IShape", "Public Property Let Name", "Name"));
    }

    [TestMethod]
    public void GoToDefinition_OfACallThroughAnInterface_LeadsToTheInterfaceMember()
    {
        var context = Workspace();

        var found = Found((context, module, position, _) => SymbolNavigation.DefinitionsAt(context, module, position), context,
            "Program", "Debug.Print s.Area", "Area");

        CollectionAssert.AreEqual(new[] { "IShape: Public Function Area() As Double" }, Described(context, found.Sites));
    }

    [TestMethod]
    [DataRow("Program", "Debug.Print s.Area", "Area", DisplayName = "of a call through the interface")]
    [DataRow("IShape", "Public Function Area", "Area", DisplayName = "of the interface member")]
    public void GoToImplementation_ListsTheProcedureOfEachClassThatImplementsTheMember(string module, string line, string text)
    {
        var context = Workspace();

        var found = Found((context, module, position, _) => SymbolNavigation.ImplementationsAt(context, module, position), context, module, line, text);

        CollectionAssert.AreEqual(new[] { "Disc: Private Function IShape_Area() As Double", "Square: Private Function IShape_Area() As Double" },
            Described(context, found.Sites));
    }

    [TestMethod]
    public void EachMemberOfAnInterface_HasALensOfItsReferences_AndALensOfItsImplementations()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[]
        {
            "Public Function Area() As Double: References", "Public Function Area() As Double: Implementations",
            "Public Property Get Name() As String: References", "Public Property Get Name() As String: Implementations",
            "Public Property Let Name(ByVal value As String): References", "Public Property Let Name(ByVal value As String): Implementations",
        }, LensesOf(context, "IShape"));
        var area = At(context, "IShape", "Public Function Area", "Area");
        CollectionAssert.AreEqual(new[] { "Disc: Private Function IShape_Area() As Double", "Square: Private Function IShape_Area() As Double" },
            Described(context, SymbolLenses.ImplementationsOf(context, ModuleUri("IShape"), area)));
    }

    [TestMethod]
    public void EachProcedureThatImplementsAMember_HasALensOfTheMemberItImplements_ThatLeadsToItsDeclaration()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[]
        {
            "Private Function IShape_Area() As Double: References",
            "Private Function IShape_Area() As Double: Implements IShape.Area, at IShape: Public Function Area() As Double",
            "Private Property Get IShape_Name() As String: References",
            "Private Property Get IShape_Name() As String: Implements IShape.Name, at IShape: Public Property Get Name() As String",
            "Private Property Let IShape_Name(ByVal value As String): References",
            "Private Property Let IShape_Name(ByVal value As String): Implements IShape.Name, at IShape: Public Property Let Name(ByVal value As String)",
        }, LensesOf(context, "Square"));
    }

    [TestMethod]
    public void AProcedureOfAClassNoClassImplements_HasALensOfItsReferences_Only()
        => CollectionAssert.AreEqual(new[] { "Public Sub Main(): References" }, LensesOf(Workspace(), "Program"));
}
