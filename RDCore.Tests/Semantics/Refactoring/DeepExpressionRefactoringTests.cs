using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
using RDCore.Tests.Parser;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// What a client asks of a module whose expression is nested past the limit (<c>VBC00060</c>) - a hover, the references of a name, the hints of a call - is
/// answered on a thread as small as one the language server answers a request on: the static pass that binds the module's names recurses once for each level
/// of an expression, and runs on a large-stack thread of its own.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.6.1 Syntax Errors")]
public sealed class DeepExpressionRefactoringTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri Main = new UriBuilder(Root) { Fragment = "Main" }.Uri;

    private static readonly string[] Lines =
    [
        "Attribute VB_Name = \"Main\"",
        "Public Sub Add(ByVal amount As Long)",
        "End Sub",
        "Public Sub Main()",
        "    Dim x As Variant",
        "    Dim y As Long",
        $"    x = {ExpressionNestingTests.Chain(40_000)}",
        "    y = 1",
        "    Add y",
        "End Sub",
    ];

    private static readonly SourcePosition AssignedY = new(7, 5);

    private static RenameContext Workspace() => Workspace(Lines);

    private static RenameContext Workspace(string[] lines)
    {
        var source = string.Join("\r\n", lines) + "\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Main.bas"), source);
        Assert.IsTrue(parse.SyntaxErrors.Any(error => error.ErrorId == (int)VBCompileErrorId.ExpressionNestedTooDeeply), "the module is past the limit");

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(Main, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        return new RenameContext(composition.Resolver, composition.ScopeTree, [new RenameModule(Main, parse.SyntaxTree!, source)]);
    }

    [TestMethod]
    public void AHover_IsAnsweredOnASmallStack()
    {
        var context = Workspace();

        string? named = null;
        var thrown = SmallStack.Run(() => named = SymbolNavigation.SymbolAt(context, Main, AssignedY)?.Symbol.Name);

        Assert.IsNull(thrown, thrown?.ToString());
        Assert.AreEqual("y", named);
    }

    [TestMethod]
    public void TheReferencesOfAName_AreFoundOnASmallStack()
    {
        var context = Workspace();

        int? lines = null;
        var thrown = SmallStack.Run(() => lines = SymbolNavigation.ReferencesAt(context, Main, AssignedY)?.Sites
            .Select(site => site.Range.Start.Line).Distinct().Count());

        Assert.IsNull(thrown, thrown?.ToString());
        Assert.AreEqual(3, lines, "its declaration, the assignment, and the argument");
    }

    // what an assignment assigns is found through each argument list after the name, as many as are written: 10,000 of them, four times the limit. The static
    // pass spends more of its stack on an index expression than on an operator, and 40,000 would not fit the large-stack thread either.
    [TestMethod]
    public void TheReferencesOfANameAssignedThroughManyArgumentLists_AreFoundOnASmallStack()
    {
        var context = Workspace(
        [
            "Attribute VB_Name = \"Main\"",
            "Public Sub Main()",
            "    Dim a As Variant",
            $"    a{string.Concat(Enumerable.Repeat("(1)", 10_000))} = 1",
            "End Sub",
        ]);

        int? lines = null;
        var thrown = SmallStack.Run(() => lines = SymbolNavigation.ReferencesAt(context, Main, new SourcePosition(2, 9))?.Sites
            .Select(site => site.Range.Start.Line).Distinct().Count());

        Assert.IsNull(thrown, thrown?.ToString());
        Assert.AreEqual(2, lines, "its declaration, and the assignment");
    }

    [TestMethod]
    public void TheHintsOfACall_AreMadeOnASmallStack()
    {
        var context = Workspace();

        IReadOnlyList<ParameterHint> hints = [];
        var thrown = SmallStack.Run(() => hints = ParameterHints.In(context, Main, new SourceRange(new(0, 0), new(Lines.Length, 0))));

        Assert.IsNull(thrown, thrown?.ToString());
        Assert.AreEqual("amount", Assert.ContainsSingle(hints).Name);
    }
}
