using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
using RDCore.Tests.Parser;
using RDCore.Runtime.LanguageCore.Refactoring;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// What a client asks of a module whose expression is as deep as RD-VBA reads, or nested past the limit (<c>VBC00060</c>) - a hover, the references of a name, the hints of a call - is
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

    private static RenameContext Workspace(string[] lines, bool pastTheLimit = true)
    {
        var source = string.Join("\r\n", lines) + "\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Main.bas"), source);
        Assert.AreEqual(pastTheLimit, parse.SyntaxErrors.Any(error => error.ErrorId == (int)VBCompileErrorId.ExpressionNestedTooDeeply), "whether the module is past the limit");

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(Main, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        return new RenameContext(composition.Resolver, composition.ScopeTree, [new RenameModule(Main, parse.SyntaxTree!, source)]);
    }

    [TestMethod]
    public void AHover_IsAnsweredOnASmallStack()
    {
        var context = Workspace();

        string? named = null;
        var thrown = SmallStack.Run(() => named = RenameBinding.SymbolAt(context, Main, AssignedY)?.Symbol.Name);

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

    // what an assignment assigns is found through each argument list after the name: 2,500 of them are within the limit, and 40,000 are past it, where the
    // name is in the text of an error node and is no reference (RD-VBAL 2.6.1). The static pass spends more of its stack on an index expression than on an
    // operator: the 2,500 lists left of 40,000 fit the large-stack thread, and the request's thread is never asked for them.
    [TestMethod]
    [DataRow(ModuleParser.MaxExpressionNesting, 2, DisplayName = "at the limit: its declaration, and the assignment")]
    [DataRow(40_000, 1, DisplayName = "past it: its declaration")]
    public void TheReferencesOfANameAssignedThroughManyArgumentLists_AreFoundOnASmallStack(int argumentLists, int expected)
    {
        var context = Workspace(
        [
            "Attribute VB_Name = \"Main\"",
            "Public Sub Main()",
            "    Dim a As Variant",
            $"    a{string.Concat(Enumerable.Repeat("(1)", argumentLists))} = 1",
            "End Sub",
        ], pastTheLimit: argumentLists > ModuleParser.MaxExpressionNesting);

        int? lines = null;
        var thrown = SmallStack.Run(() => lines = SymbolNavigation.ReferencesAt(context, Main, new SourcePosition(2, 9))?.Sites
            .Select(site => site.Range.Start.Line).Distinct().Count());

        Assert.IsNull(thrown, thrown?.ToString());
        Assert.AreEqual(expected, lines);
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
