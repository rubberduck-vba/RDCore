using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// A hover over a name says the declaration of its symbol as code writes it, what declares it, and its doc string.
/// </summary>
[TestClass]
public sealed class SymbolHoverTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static readonly string Class1 = Lines(
        "VERSION 1.0 CLASS",
        "BEGIN",
        "  MultiUse = -1  'True",
        "END",
        "Attribute VB_Name = \"Class1\"",
        "Option Explicit",
        "Private mValue As String",
        "Public Property Get Value() As String",
        "    Value = mValue",
        "End Property",
        "Public Property Let Value(ByVal v As String)",
        "    mValue = v",
        "End Property");

    private static readonly string Helpers = Module("Helpers",
        "Option Explicit",
        "Public Total As Long",
        "Private Const Max As Long = 10",
        "Public Enum Colour",
        "    Red",
        "End Enum",
        "Public Type Point",
        "    X As Long",
        "End Type",
        "Public Function GetTotal() As Long",
        "    GetTotal = Total",
        "End Function",
        "Public Sub Add(ByVal amount As Long, Optional by As Colour = Red)",
        "    Total = Total + amount + Max",
        "End Sub");

    private static readonly string Program = Module("Program",
        "Option Explicit",
        "Public Sub Main()",
        "    Dim count As Long",
        "    Static p As Point",
        "    count = GetTotal",
        "    p.X = count",
        "    Dim c As Class1",
        "    c.Value = \"x\"",
        "    MsgBox Total",
        "End Sub");

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static string Module(string name, params string[] lines) => Lines([$"Attribute VB_Name = \"{name}\"", .. lines]);

    // the workspace of the modules, with the standard library it references.
    private static RenameContext Workspace()
    {
        (string Name, string Source)[] modules = [("Class1", Class1), ("Helpers", Helpers), ("Program", Program)];
        var parsed = modules.Select(module =>
        {
            var isClass = module.Source.StartsWith("VERSION", StringComparison.Ordinal);
            var parse = new ModuleParser().Parse(new Uri($"file:///c:/ws/{module.Name}{(isClass ? ".cls" : ".bas")}"), module.Source);
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
            return (Uri: ModuleUri(module.Name), Type: isClass ? ModuleType.ClassModule : ModuleType.StdModule, Parse: parse, module.Source);
        }).ToList();

        var libraries = new LibrarySymbolProvider(Root, ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols();
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [.. parsed.Select(module => (module.Uri, module.Type, module.Parse))],
            new IntrinsicSymbolResolver(), libraries: [.. libraries]);
        return new RenameContext(composition.Resolver, composition.ScopeTree, [.. parsed.Select(module => new RenameModule(module.Uri, module.Parse.SyntaxTree!, module.Source))]);
    }

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    // what a hover says of the first `text` on the first line that has `line`, in the module.
    private static SymbolHover HoverAt(RenameContext context, string module, string line, string text)
    {
        var lines = context.Modules.Single(candidate => candidate.Module.Fragment == "#" + module).Source.Split("\r\n");
        var index = Array.FindIndex(lines, candidate => candidate.Contains(line, StringComparison.Ordinal));
        Assert.IsTrue(index >= 0, $"no line has '{line}'");
        var named = SymbolNavigation.SymbolAt(context, ModuleUri(module), new SourcePosition(index, lines[index].IndexOf(text, StringComparison.Ordinal) + 1));
        Assert.IsNotNull(named, $"'{text}' on '{line}' names nothing known");
        return SymbolHover.Of(named.Value.Symbol);
    }

    [TestMethod]
    [DataRow("Program", "count = GetTotal", "GetTotal", "Public Function GetTotal() As Long", "Helpers", DisplayName = "a function")]
    [DataRow("Helpers", "Public Sub Add", "Add", "Public Sub Add(ByVal amount As Long, Optional by As Colour)", "Helpers", DisplayName = "a procedure")]
    [DataRow("Program", "c.Value = ", "Value", "Public Property Get Value() As String", "Class1", DisplayName = "a property")]
    [DataRow("Helpers", "Public Total As Long", "Total", "Public Total As Long", "Helpers", DisplayName = "a variable of a module")]
    [DataRow("Helpers", "Private Const Max", "Max", "Private Const Max As Long", "Helpers", DisplayName = "a constant")]
    [DataRow("Program", "count = GetTotal", "count", "Dim count As Long", "Program.Main", DisplayName = "a local")]
    [DataRow("Program", "p.X = count", "p", "Static p As Point", "Program.Main", DisplayName = "a Static local of a Type")]
    [DataRow("Helpers", "Total = Total + amount", "amount", "ByVal amount As Long", "Helpers.Add", DisplayName = "a parameter")]
    [DataRow("Helpers", "Public Enum Colour", "Colour", "Public Enum Colour", "Helpers", DisplayName = "an Enum")]
    [DataRow("Helpers", "    Red", "Red", "Red", "Helpers.Colour", DisplayName = "a member of an Enum")]
    [DataRow("Helpers", "Public Type Point", "Point", "Public Type Point", "Helpers", DisplayName = "a Type")]
    [DataRow("Program", "p.X = count", "X", "X As Long", "Helpers.Point", DisplayName = "a field of a Type")]
    [DataRow("Program", "Dim c As Class1", "Class1", "Class Class1", null, DisplayName = "a class")]
    public void AHover_SaysTheDeclaration_AndWhatDeclaresIt(string module, string line, string text, string declaration, string? container)
    {
        var hover = HoverAt(Workspace(), module, line, text);

        Assert.AreEqual(declaration, hover.Declaration);
        Assert.AreEqual(container, hover.Container);
    }

    [TestMethod]
    public void AMemberOfTheLibrary_IsSaidByItsSignature_ItsModule_AndItsDocString()
    {
        var hover = HoverAt(Workspace(), "Program", "MsgBox Total", "MsgBox");

        StringAssert.StartsWith(hover.Declaration, "Public Function MsgBox(ByVal Prompt As Variant, ");
        Assert.AreEqual("VBA.Interaction", hover.Container);
        Assert.IsFalse(string.IsNullOrEmpty(hover.Documentation), "the standard library documents MsgBox");
    }

    [TestMethod]
    public void AsMarkdown_TheDeclarationIsABlockOfVBA_AndWhatDeclaresIt_IsSaidInTheLanguageOfTheReader()
    {
        var hover = new SymbolHover("Dim count As Long", "Program.Main", null);

        Assert.AreEqual("```vba\nDim count As Long\n```\n\nMember of Program.Main", hover.ToMarkdown(CultureInfo.InvariantCulture));
        Assert.AreEqual("```vba\nDim count As Long\n```\n\nMembre de Program.Main", hover.ToMarkdown(CultureInfo.GetCultureInfo("fr-CA")));
        Assert.AreEqual("Dim count As Long\nMember of Program.Main", hover.ToPlainText(CultureInfo.InvariantCulture));
    }
}
