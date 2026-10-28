using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Parser;

/// <summary>
/// A procedure header written once in each branch of a <c>#If</c> block, around one body (<strong>MS-VBAL §3.4</strong>): one procedure, whose other headers
/// are its <see cref="MemberDeclarationNode.HeaderAlternatives"/>. MS-VBA compiles the live one (recorded in Excel: oracle\cc-split).
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

    // S01: JsonConverter.bas's json_UnsignedAdd.
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
        // S04: a Sub in the first branch and a Function in the second, closed by an End Function - which MS-VBA compiles where the Function is live.
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
        // S11: MS-VBA runs the live branch's statements, then the ones after the block.
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
    public void NoTwoNodes_HaveOneIdentity()
    {
        var result = Parse(["Public Sub Main()", "    Dim x As Long", "End Sub", .. PtrSafe]);
        var procedure = Procedure(result);
        var nodes = result.SyntaxTree!.Descendants().Concat(procedure.HeaderAlternatives.SelectMany(alternative => alternative.Descendants().Prepend(alternative))).ToList();

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
