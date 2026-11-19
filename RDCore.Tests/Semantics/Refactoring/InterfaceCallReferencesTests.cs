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
/// A call through an interface (<strong>MS-VBAL §5.2.4.2</strong>) is a reference of the interface's member and of each procedure that implements it,
/// which the call may run: the code lens of an implementation counts it, and the references of the interface's member list the implementations.
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

    private static string[] ReferencesOf(RenameContext context, string module, string line, string text)
    {
        var position = At(context, module, line, text);
        var found = SymbolNavigation.ReferencesAt(context, ModuleUri(module), new SourcePosition(position.Line, position.Character + 1));
        Assert.IsNotNull(found, $"'{text}' on '{line}' names nothing");
        return Described(context, found.Sites);
    }

    [TestMethod]
    [DataRow("Square", DisplayName = "the implementation of the class the variable is set to")]
    [DataRow("Disc", DisplayName = "another implementation")]
    public void TheLensOfAnImplementation_CountsTheCallsThroughItsInterface(string implementation)
    {
        var context = Workspace();

        CollectionAssert.Contains(LensOf(context, implementation, "Private Function IShape_Area", "IShape_Area"), "Program: Debug.Print s.Area");
    }

    [TestMethod]
    public void TheLensOfAnImplementationOfAPropertyProcedure_CountsTheCallsOfItsProcedureThroughItsInterface()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { "Program: Debug.Print s.Name" }, LensOf(context, "Square", "Private Property Get IShape_Name", "IShape_Name"));
        CollectionAssert.AreEqual(new[] { "Program: s.Name = \"sq\"" }, LensOf(context, "Square", "Private Property Let IShape_Name", "IShape_Name"));
    }

    [TestMethod]
    public void TheLensOfAnInterfaceMember_CountsTheCallsThroughIt_AndNotItsImplementations()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { "Program: Debug.Print s.Area" }, LensOf(context, "IShape", "Public Function Area", "Area"));
    }

    [TestMethod]
    public void TheReferencesOfAnInterfaceMember_ListEachImplementation()
    {
        var context = Workspace();

        var references = ReferencesOf(context, "Program", "Debug.Print s.Area", "Area");

        CollectionAssert.IsSubsetOf(
            new[] { "Disc: Private Function IShape_Area() As Double", "IShape: Public Function Area() As Double", "Program: Debug.Print s.Area",
                "Square: Private Function IShape_Area() As Double" },
            references);
    }

    [TestMethod]
    public void TheReferencesOfAnImplementation_ListTheCallsThroughItsInterface()
    {
        var context = Workspace();

        var references = ReferencesOf(context, "Square", "Private Function IShape_Area", "IShape_Area");

        CollectionAssert.IsSubsetOf(new[] { "Program: Debug.Print s.Area", "Square: Private Function IShape_Area() As Double" }, references);
    }
}
