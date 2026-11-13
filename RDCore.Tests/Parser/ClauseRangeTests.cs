using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// A clause of a block statement (<c>ElseIf</c>, <c>Else</c>, <c>Case</c>, <c>Case Else</c>) ends where its last statement does, or with its own header
/// when it has none: the comment after its last statement, the blank lines and the indentation before the keyword that closes it are no part of it.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.4.2.8 If Statement")]
[TestCategory("RD-VBAL §5.4.2.10 Select Case Statement")]
public sealed class ClauseRangeTests
{
    private static string TextOf(string source, SourceRange range)
    {
        var lines = source.Split("\r\n");
        return range.Start.Line == range.End.Line
            ? lines[range.Start.Line][range.Start.Character..range.End.Character]
            : string.Join("\r\n", [lines[range.Start.Line][range.Start.Character..], .. lines[(range.Start.Line + 1)..range.End.Line], lines[range.End.Line][..range.End.Character]]);
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node) => node.Children.SelectMany(Descendants).Prepend(node);

    // the clauses of the block statements of the module, which are members of their block and not its children.
    private static IEnumerable<SyntaxNode> ClausesOf(SyntaxNode tree)
    {
        foreach (var node in Descendants(tree))
        {
            if (node is IfBlockStatementNode @if)
            {
                foreach (var elseIf in @if.ElseIfBlocks)
                {
                    yield return elseIf;
                }
                if (@if.ElseBlock is { } @else)
                {
                    yield return @else;
                }
            }
            else if (node is SelectCaseStatementNode select)
            {
                foreach (var @case in select.CaseExpressionBlocks)
                {
                    yield return @case;
                }
                if (select.CaseElseBlock is { } caseElse)
                {
                    yield return caseElse;
                }
            }
        }
    }

    [TestMethod]
    [DataRow("If a Then\r\n    b = 1\r\nElse\r\n    b = 2\r\n\r\nEnd If", typeof(ElseBlockStatementNode), "Else\r\n    b = 2",
        DisplayName = "an Else, then a blank line")]
    [DataRow("If a Then\r\n    b = 1\r\nElseIf c Then\r\n    b = 2\r\n    \r\nElse\r\nEnd If", typeof(ElseIfBlockStatementNode), "ElseIf c Then\r\n    b = 2",
        DisplayName = "an ElseIf, then an indented blank line")]
    [DataRow("If a Then\r\n    b = 1\r\nElseIf c Then\r\n    b = 2\r\n    \r\nElse\r\nEnd If", typeof(ElseBlockStatementNode), "Else",
        DisplayName = "an Else of no statement")]
    [DataRow("If a Then\r\nElse\r\n    b = 2 ' two\r\n    ' more\r\nEnd If", typeof(ElseBlockStatementNode), "Else\r\n    b = 2",
        DisplayName = "an Else, then comments")]
    [DataRow("If a Then\r\nElse\r\n    If c Then\r\n        b = 2\r\n    End If\r\n\r\nEnd If", typeof(ElseBlockStatementNode), "Else\r\n    If c Then\r\n        b = 2\r\n    End If",
        DisplayName = "an Else that ends with a block If")]
    [DataRow("Select Case a\r\n    Case 1: b = 1\r\n    Case Else\r\n        b = 2\r\n\r\nEnd Select", typeof(CaseExpressionStatementNode), "Case 1: b = 1",
        DisplayName = "a Case of one line")]
    [DataRow("Select Case a\r\n    Case 1: b = 1\r\n    Case Else\r\n        b = 2\r\n\r\nEnd Select", typeof(CaseElseClauseStatementNode), "Case Else\r\n        b = 2",
        DisplayName = "a Case Else, then a blank line")]
    [DataRow("Select Case a\r\n    Case 1, 2\r\n    Case Else\r\nEnd Select", typeof(CaseExpressionStatementNode), "Case 1, 2",
        DisplayName = "a Case of no statement")]
    [DataRow("Select Case a\r\n    Case 1, 2\r\n    Case Else ' none\r\nEnd Select", typeof(CaseElseClauseStatementNode), "Case Else",
        DisplayName = "a Case Else of no statement, with a comment")]
    public void AClause_EndsWithItsLastStatement(string block, Type kind, string expected)
    {
        var source = $"Public Sub Main()\r\n{block}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        Assert.IsEmpty(parse.SyntaxErrors);
        var clause = Assert.ContainsSingle(ClausesOf(parse.SyntaxTree!).Where(kind.IsInstanceOfType));
        Assert.AreEqual(expected, TextOf(source, clause.SourceLocation.Range));
    }
}
