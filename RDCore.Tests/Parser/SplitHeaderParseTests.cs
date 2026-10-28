using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Parser;

/// <summary>
/// A procedure header written once in each branch of a <c>#If</c> block, around one body (<strong>MS-VBAL §3.4</strong>): one procedure, whose other headers
/// are its <see cref="MemberDeclarationNode.HeaderAlternatives"/>. MS-VBA compiles the live one (recorded in Excel).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.4 Conditional Compilation")]
public sealed class SplitHeaderParseTests
{
    private static ModuleParseResult Parse(params string[] lines) => new ModuleParser().Parse(TestUri.TestModuleUri(), string.Join("\r\n", lines) + "\r\n");

    private static MemberDeclarationNode Procedure(ModuleParseResult result, string name = "F")
    {
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : $"{result.SyntaxErrors[0].Location.Range.Start.Line}: {result.SyntaxErrors[0].Verbose}");
        return result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single(member => member.Name == name);
    }

    private static string? ReturnTypeOf(MemberDeclarationNode member) => member.HeaderChildren.OfType<AsTypeExpressionNode>().SingleOrDefault()?.TypeName;

    private static readonly string[] PtrSafe =
    [
        "#If VBA7 Then",
        "Private Function F(ByVal a As LongPtr) As LongPtr",
        "#Else",
        "Private Function F(ByVal a As Long) As Long",
        "#End If",
        "    F = a * 2",
        "End Function",
    ];

    [TestMethod]
    public void AHeaderWrittenInEachBranch_IsOneProcedure_WithTheOtherHeaderAsItsAlternative()
    {
        var procedure = Procedure(Parse(PtrSafe));

        Assert.AreEqual("LongPtr", ReturnTypeOf(procedure));
        var alternative = Assert.ContainsSingle(procedure.HeaderAlternatives);
        Assert.AreEqual("Long", ReturnTypeOf(alternative));
        Assert.AreEqual(3, alternative.SourceLocation.Range.Start.Line, "the alternative is where its branch writes it");
        Assert.AreEqual("Long", alternative.HeaderChildren.OfType<ParameterDeclarationNode>().Single().Children.OfType<AsTypeExpressionNode>().Single().TypeName);
        Assert.ContainsSingle(procedure.Children.OfType<SDK.Model.AST.Statements.AssignmentStatementNode>(), "one body, read once");
    }

    [TestMethod]
    public void TheHeaderTheMainParseReads_IsTheOneOfTheKindOfTheStatementThatClosesIt()
    {
        // a Sub in the first branch and a Function in the second, closed by an End Function - which MS-VBA compiles where the Function is live.
        var procedure = Procedure(Parse("#If Mac Then", "Private Sub F()", "#Else", "Private Function F() As Long", "#End If", "    F = 3", "End Function"));

        Assert.AreEqual(MemberKind.Function, procedure.MemberKind);
        Assert.AreEqual(MemberKind.Procedure, Assert.ContainsSingle(procedure.HeaderAlternatives).MemberKind);
    }

    [TestMethod]
    public void AnEarlierBranchsStatements_DoNotComeBeforeTheHeaderTheMainParseRead()
    {
        var procedure = Procedure(Parse(
            "#If Mac Then", "Private Function F(ByVal a As Integer) As Integer", "    F = 1", "#Else", "Private Function F(ByVal a As Long) As Long", "#End If",
            "    F = F + a", "End Function", "Public Sub Main()", "End Sub"), "F");

        Assert.AreEqual("Integer", ReturnTypeOf(procedure), "the first branch's header is of the closer's kind too, and is read");
        Assert.AreEqual("Long", ReturnTypeOf(Assert.ContainsSingle(procedure.HeaderAlternatives)));
        CollectionAssert.AreEqual(new[] { 2, 6 }, procedure.Children.OfType<SDK.Model.AST.Statements.AssignmentStatementNode>()
            .Select(statement => statement.SourceLocation.Range.Start.Line).ToArray());
    }

    [TestMethod]
    public void AnAlternativeBeforeTheMainHeader_KeepsTheMainHeaderFirstInTheChildren()
    {
        var procedure = Procedure(Parse(
            "#If Mac Then", "Private Sub F()", "    Debug.Print 1", "#Else", "Private Function F(ByVal a As Long) As Long", "#End If", "    F = a", "End Function"));

        Assert.AreEqual("Long", ReturnTypeOf(procedure));
        Assert.AreEqual("a", procedure.HeaderChildren.OfType<ParameterDeclarationNode>().Single().Name);
    }

    [TestMethod]
    public void WhatABranchWritesAfterItsHeader_IsOfTheBody_InSourceOrder()
    {
        // MS-VBA runs the live branch's statements, then the ones after the block.
        var procedure = Procedure(Parse(
            "#If VBA7 Then", "Private Function F() As Long", "    F = 1", "#Else", "Private Function F() As Long", "    F = 2", "#End If", "    F = F + 10", "End Function"));

        CollectionAssert.AreEqual(new[] { 2, 5, 7 }, procedure.Children.OfType<SDK.Model.AST.Statements.AssignmentStatementNode>()
            .Select(statement => statement.SourceLocation.Range.Start.Line).ToArray());
    }

    [TestMethod]
    public void EachBranchOfAnElseIfChain_IsAnAlternative()
    {
        var procedure = Procedure(Parse(
            "#If Mac Then", "Private Function F() As Integer", "#ElseIf Win64 Then", "Private Function F() As LongLong", "#Else", "Private Function F() As Long", "#End If",
            "    F = 7", "End Function"));

        Assert.AreEqual("Integer", ReturnTypeOf(procedure));
        CollectionAssert.AreEqual(new[] { "LongLong", "Long" }, procedure.HeaderAlternatives.Select(ReturnTypeOf).ToArray());
    }

    [TestMethod]
    public void ABlockNestedInABranch_GivesItsHeadersToTheProcedureToo()
    {
        var procedure = Procedure(Parse(
            "#If VBA7 Then", "#If Win64 Then", "Private Function F() As LongLong", "#Else", "Private Function F() As Long", "#End If",
            "#Else", "Private Function F() As Integer", "#End If", "    F = 7", "End Function"));

        Assert.AreEqual("LongLong", ReturnTypeOf(procedure));
        CollectionAssert.AreEquivalent(new[] { "Long", "Integer" }, procedure.HeaderAlternatives.Select(ReturnTypeOf).ToArray());
    }

    [TestMethod]
    public void AProcedureWrittenWholeInEachBranch_IsNoSplitHeader()
    {
        var result = Parse("#If VBA7 Then", "Private Function F() As Long", "    F = 1", "End Function", "#Else", "Private Function F() As Long", "    F = 2", "End Function", "#End If");

        Assert.IsTrue(result.IsSuccess);
        var procedures = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToList();
        Assert.HasCount(2, procedures);
        Assert.IsTrue(procedures.All(procedure => procedure.HeaderAlternatives.IsEmpty));
    }

    [TestMethod]
    public void WhatABranchHoldsBeforeItsHeader_IsTheModules_WhereItIsWritten()
    {
        var result = Parse(
            "#If VBA7 Then", "Private Declare PtrSafe Function Ticks Lib \"kernel32\" Alias \"GetTickCount\" () As Long", "Private Function F() As LongPtr",
            "#Else", "Private Declare Function Ticks Lib \"kernel32\" Alias \"GetTickCount\" () As Long", "Private Sub G()", "End Sub", "Private Function F() As Long",
            "#End If", "    F = 1", "End Function");
        var procedure = Procedure(result);

        Assert.AreEqual("LongPtr", ReturnTypeOf(procedure));
        Assert.AreEqual("Long", ReturnTypeOf(Assert.ContainsSingle(procedure.HeaderAlternatives)));
        var members = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToList();
        CollectionAssert.AreEqual(new[] { "Ticks", "F", "Ticks", "G" }, members.Select(member => member.Name).ToArray(), "in source order, each where it is written");
        CollectionAssert.AreEqual(new[] { 1, 2, 4, 5 }, members.Select(member => member.SourceLocation.Range.Start.Line).ToArray());
        Assert.IsTrue(members[2] is ExternalMemberDeclarationNode { IsPtrSafe: false }, "each branch's Declare, as any #If block's");
    }

    [TestMethod]
    public void ABranchsHeader_IsTheProcedureItLeavesOpen_NotTheFirstItOpens()
    {
        // a whole Function before a Sub header in the first branch, a Function header in the second, closed by an End Function: the second is read.
        var result = Parse(
            "#If Mac Then", "Private Function G() As Long", "End Function", "Private Sub F()", "#Else", "Private Function F() As Long", "#End If",
            "    F = 3", "End Function");
        var procedure = Procedure(result);

        Assert.AreEqual(MemberKind.Function, procedure.MemberKind);
        Assert.AreEqual(5, procedure.SourceLocation.Range.Start.Line);
        var alternative = Assert.ContainsSingle(procedure.HeaderAlternatives);
        Assert.AreEqual(MemberKind.Procedure, alternative.MemberKind);
        Assert.AreEqual(3, alternative.SourceLocation.Range.Start.Line);
        Assert.AreEqual(1, Procedure(result, "G").SourceLocation.Range.Start.Line, "the whole Function is the module's");
    }

    [TestMethod]
    public void TheSelectionAtAPositionInABranchsWholeProcedure_GrowsThroughThatProcedure()
    {
        // G is on lines 3-5, within the lines of F (1-9), whose header the block splits; Main follows.
        var result = Parse(
            "#If VBA7 Then", "Private Function F() As Long", "#Else", "Private Sub G()", "    Debug.Print 1", "End Sub", "Private Function F() As Long", "#End If",
            "    F = 1", "End Function", "Public Sub Main()", "End Sub");
        var tree = result.SyntaxTree!;
        var f = Procedure(result).SourceLocation.Range;
        var g = Procedure(result, "G").SourceLocation.Range;

        var inG = SyntaxSelection.RangesAt(tree, new SourcePosition(4, 8));
        CollectionAssert.Contains(inG.ToArray(), g);
        CollectionAssert.DoesNotContain(inG.ToArray(), f, "a selection in G does not grow through F");
        CollectionAssert.Contains(SyntaxSelection.RangesAt(tree, new SourcePosition(8, 6)).ToArray(), f);
    }

    [TestMethod]
    public void TheSelectionAtWhereTwoNodesTouch_GrowsFromTheFirst()
    {
        // a caret between Foo and (a): the callee's end is where the parenthesized argument begins, and the selection grows from the callee, as it always has.
        var tree = Parse("Public Sub Main()", "    Foo(a)", "End Sub", "Private Sub Foo(ByVal x As Long)", "End Sub").SyntaxTree!;
        var foo = tree.Descendants().OfType<SimpleNameExpressionNode>().First(name => name.IdentifierName == "Foo").SourceLocation.Range;
        var at = foo.End;
        Assert.IsTrue(tree.Descendants().Any(node => node.SourceLocation.Range.Start == at && node.SourceLocation.Range != foo), "the argument begins where the callee ends");

        var ranges = SyntaxSelection.RangesAt(tree, at);

        Assert.AreEqual(foo, ranges[0]);
    }

    [TestMethod]
    public void NoTwoNodes_HaveOneIdentity()
    {
        var result = Parse([
            "#If VBA7 Then", "Private Declare PtrSafe Function Ticks Lib \"kernel32\" Alias \"GetTickCount\" () As Long", "Private Function H() As Long", "    H = 1",
            "#Else", "Private Declare Function Ticks Lib \"kernel32\" Alias \"GetTickCount\" () As Long", "Private Sub G()", "    Debug.Print 1", "End Sub",
            "Private Function H() As Long", "    H = 2", "#End If", "    H = H + 10", "End Function",
            "Public Sub Main()", "    Dim x As Long", "End Sub", .. PtrSafe,
            "#If VBA7 Then", "#If Win64 Then", "Private Function K() As LongLong", "    K = 1", "#Else", "Private Function K() As Long", "    K = 2", "#End If",
            "#Else", "Private Function K() As Integer", "    K = 3", "#End If", "    K = K + 7", "End Function"]);
        var procedures = new[] { "F", "H", "K" }.Select(name => Procedure(result, name)).ToList();
        Assert.HasCount(2, procedures[2].HeaderAlternatives, "the nested block's alternative and the outer one's");
        var nodes = result.SyntaxTree!.Descendants()
            .Concat(procedures.SelectMany(procedure => procedure.HeaderAlternatives).SelectMany(alternative => alternative.Descendants().Prepend(alternative))).ToList();

        Assert.HasCount(nodes.Count, nodes.Select(node => node.Identity).Distinct());
    }

    [TestMethod]
    public void TheAlternatives_TravelWithTheTree()
    {
        var result = Parse(PtrSafe);
        var travelled = PlatformJson.Deserialize<ModuleParseResult>(PlatformJson.Serialize(result))!;

        var alternative = Assert.ContainsSingle(Procedure(travelled).HeaderAlternatives);
        Assert.AreEqual("Long", ReturnTypeOf(alternative));
        Assert.AreEqual(3, alternative.SourceLocation.Range.Start.Line);
    }
}
