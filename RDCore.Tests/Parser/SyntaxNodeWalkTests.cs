using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.Tests.Parser;

/// <summary>
/// The statements of the blocks of a structured statement are not among its <see cref="SyntaxNode.Children"/>: walking the tree for all of it is
/// <see cref="SyntaxNodeWalk"/>'s job.
/// </summary>
[TestClass]
public sealed class SyntaxNodeWalkTests
{
    // a Debug.Print in the block of every statement that holds one: 16 of them.
    private const string Body = """
        Public Sub Work(ByVal c As Boolean, ByVal n As Long, ByVal items As Collection, ByVal target As Object)
            If c Then
                Debug.Print 1
            ElseIf n > 1 Then
                Debug.Print 2
            Else
                Debug.Print 3
            End If
            If c Then Debug.Print 4 Else Debug.Print 5
            Select Case n
            Case 1
                Debug.Print 6
            Case Else
                Debug.Print 7
            End Select
            While c
                Debug.Print 8
            Wend
            Do While c
                Debug.Print 9
            Loop
            Do Until c
                Debug.Print 10
            Loop
            Do
                Debug.Print 11
            Loop While c
            Do
                Debug.Print 12
            Loop Until c
            Do
                Debug.Print 13
            Loop
            For n = 1 To 2
                Debug.Print 14
            Next
            For Each target In items
                Debug.Print 15
            Next
            With target
                Debug.Print 16
            End With
        End Sub
        """;

    private static MemberDeclarationNode Procedure()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), Body.Replace("\n", "\r\n") + "\r\n");
        Assert.IsEmpty(result.SyntaxErrors, string.Join("; ", result.SyntaxErrors.Select(error => error.Verbose)));
        return result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
    }

    [TestMethod]
    public void EveryStatementInEveryKindOfBlock_IsAmongTheDescendants()
        => Assert.AreEqual(16, Procedure().Descendants().OfType<DebugPrintStatementNode>().Count());

    [TestMethod]
    public void TheChildrenAlone_NeverGetInsideABlock()
        => Assert.AreEqual(0, Procedure().Children.OfType<DebugPrintStatementNode>().Count(), "which is why the tree is walked by SyntaxNodeWalk");

    [TestMethod]
    public void TheStatementsOfABlock_AreASiblingListOfTheirOwn()
    {
        var ifBlock = Procedure().Children.OfType<IfBlockStatementNode>().First();

        var lists = ifBlock.SiblingLists().ToArray();

        Assert.HasCount(2, lists, "the children of the statement, and its body");
        Assert.IsTrue(lists[1].OfType<DebugPrintStatementNode>().Any());
    }

    [TestMethod]
    public void TheBranchesOfAnIf_AndTheCasesOfASelect_AreChildNodesOfTheStatement()
    {
        var module = Procedure();
        var ifBlock = module.Children.OfType<IfBlockStatementNode>().First();
        var select = module.Children.OfType<SelectCaseStatementNode>().Single();

        Assert.IsTrue(ifBlock.ChildNodes().OfType<ElseIfBlockStatementNode>().Any());
        Assert.IsTrue(ifBlock.ChildNodes().OfType<ElseBlockStatementNode>().Any());
        Assert.IsTrue(select.ChildNodes().OfType<CaseExpressionStatementNode>().Any());
        Assert.IsTrue(select.ChildNodes().OfType<CaseElseClauseStatementNode>().Any());
    }
}
