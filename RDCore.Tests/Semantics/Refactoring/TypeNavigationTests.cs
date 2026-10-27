using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// A navigation to a type leads to the class, <c>Type</c> or <c>Enum</c> a symbol is declared as (<strong>LSP 3.17</strong> §Goto Type Definition Request),
/// and a navigation to the implementations of a class or a member of one leads to the classes that implement it and their procedures that implement the
/// member (§Goto Implementation Request, <strong>MS-VBAL §5.2.4.2</strong>).
/// </summary>
[TestClass]
public sealed class TypeNavigationTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static string Class(string name, params string[] lines)
        => Lines(["VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", $"Attribute VB_Name = \"{name}\"", "Option Explicit", .. lines]);

    private static readonly string IShape = Class("IShape",
        "Public Function Area() As Double",
        "End Function",
        "Public Property Get Name() As String",
        "End Property");

    private static string Shape(string name) => Class(name,
        "Implements IShape",
        "Private Function IShape_Area() As Double",
        "End Function",
        "Private Property Get IShape_Name() As String",
        "End Property");

    private static readonly string Module1 = Lines(
        "Attribute VB_Name = \"Module1\"",
        "Option Explicit",
        "Public Type Point",
        "    X As Long",
        "End Type",
        "Public Enum Colour",
        "    Red",
        "End Enum",
        "Public Function Make() As IShape",
        "End Function",
        "Public Sub Main()",
        "    Dim s As IShape",
        "    Dim sq As New Square",
        "    Dim pts() As Point",
        "    Dim c As Colour",
        "    Dim n As Long",
        "    Debug.Print s.Area, s.Name, sq.Area",
        "End Sub");

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static RenameContext Workspace()
    {
        (string Name, string Source)[] modules = [("IShape", IShape), ("Square", Shape("Square")), ("Circle", Shape("Circle")), ("Module1", Module1)];
        var parsed = modules.Select(module =>
        {
            var isClass = module.Source.StartsWith("VERSION", StringComparison.Ordinal);
            var parse = new ModuleParser().Parse(new Uri($"file:///c:/ws/{module.Name}{(isClass ? ".cls" : ".bas")}"), module.Source);
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
            return (Uri: ModuleUri(module.Name), Type: isClass ? ModuleType.ClassModule : ModuleType.StdModule, Parse: parse, module.Source);
        }).ToList();

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [.. parsed.Select(module => (module.Uri, module.Type, module.Parse))], new IntrinsicSymbolResolver());
        return new RenameContext(composition.Resolver, composition.ScopeTree, [.. parsed.Select(module => new RenameModule(module.Uri, module.Parse.SyntaxTree!, module.Source))]);
    }

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private static string[] Source(RenameContext context, string module)
        => context.Modules.Single(candidate => candidate.Module.Fragment == "#" + module).Source.Split("\r\n");

    // the position of the `occurrence`th `text` on the first line that has `line`, in Module1.
    private static SourcePosition At(RenameContext context, string line, string text, int occurrence = 1)
    {
        var lines = Source(context, "Module1");
        var index = Array.FindIndex(lines, candidate => candidate.Contains(line, StringComparison.Ordinal));
        var character = -1;
        for (var found = 0; found < occurrence; found++)
        {
            character = lines[index].IndexOf(text, character + 1, StringComparison.Ordinal);
        }

        return new SourcePosition(index, character + 1);
    }

    // each site, as module:line, in the order answered.
    private static string[] Sites(RenameContext context, SymbolNavigationTarget? target)
    {
        Assert.IsNotNull(target);
        return [.. target.Sites.Select(site => $"{site.Module.Fragment.TrimStart('#')}:{Source(context, site.Module.Fragment.TrimStart('#'))[site.SelectionRange.Start.Line].Trim()}")];
    }

    [TestMethod]
    [DataRow("Dim s As IShape", "s", "IShape:VERSION 1.0 CLASS", DisplayName = "a variable of a class")]
    [DataRow("Dim sq As New Square", "sq", "Square:VERSION 1.0 CLASS", DisplayName = "an auto-object")]
    [DataRow("Dim pts() As Point", "pts", "Module1:Public Type Point", DisplayName = "an array of a Type")]
    [DataRow("Dim c As Colour", "c", "Module1:Public Enum Colour", DisplayName = "an Enum")]
    [DataRow("Public Function Make() As IShape", "Make", "IShape:VERSION 1.0 CLASS", DisplayName = "a function")]
    [DataRow("Dim s As IShape", "IShape", "IShape:VERSION 1.0 CLASS", DisplayName = "a type itself")]
    public void ATypeDefinition_IsTheTypeASymbolIsDeclaredAs(string line, string text, string expected)
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { expected }, Sites(context, SymbolNavigation.TypeDefinitionAt(context, ModuleUri("Module1"), At(context, line, text))));
    }

    [TestMethod]
    public void AnIntrinsicType_HasNoTypeDefinition()
    {
        var context = Workspace();

        Assert.IsEmpty(SymbolNavigation.TypeDefinitionAt(context, ModuleUri("Module1"), At(context, "Dim n As Long", "n"))!.Sites);
    }

    [TestMethod]
    public void AClass_IsImplementedByEachClassThatImplementsIt_AtItsImplements()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { "Circle:Implements IShape", "Square:Implements IShape" },
            Sites(context, SymbolNavigation.ImplementationsAt(context, ModuleUri("Module1"), At(context, "Dim s As IShape", "IShape"))));
    }

    [TestMethod]
    [DataRow("Area", "Private Function IShape_Area() As Double", DisplayName = "a function")]
    [DataRow("Name", "Private Property Get IShape_Name() As String", DisplayName = "a property")]
    public void AMemberOfAClass_IsImplementedByTheProcedureOfEachImplementation(string member, string implementation)
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { $"Circle:{implementation}", $"Square:{implementation}" },
            Sites(context, SymbolNavigation.ImplementationsAt(context, ModuleUri("Module1"), At(context, "Debug.Print s.Area", member))));
    }

    [TestMethod]
    public void AClassThatNoClassImplements_HasNoImplementation()
    {
        var context = Workspace();

        Assert.IsEmpty(SymbolNavigation.ImplementationsAt(context, ModuleUri("Module1"), At(context, "Dim sq As New Square", "Square"))!.Sites);
    }
}
