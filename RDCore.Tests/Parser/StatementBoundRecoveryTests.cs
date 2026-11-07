using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// The recovery from a syntax error never reads past the end of the statement the error is in: after a statement that ends with a binary operator, the next
/// statement is read on its own, and is in the tree as the statement it is. The error is reported once, before the next statement.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.6.1 Syntax Errors")]
public sealed class StatementBoundRecoveryTests
{
    private static string TextOf(string source, SourceRange range)
    {
        var lines = source.Split("\r\n");
        return range.Start.Line == range.End.Line
            ? lines[range.Start.Line][range.Start.Character..range.End.Character]
            : string.Join("\r\n", [lines[range.Start.Line][range.Start.Character..], .. lines[(range.Start.Line + 1)..range.End.Line], lines[range.End.Line][..range.End.Character]]);
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node) => node.Children.SelectMany(Descendants).Prepend(node);

    [TestMethod]
    [DataRow("a = 1 +", "b = 2", typeof(AssignmentStatementNode), DisplayName = "an operator the parse reads, then an assignment")]
    [DataRow("a = 1 &", "b = 2", typeof(AssignmentStatementNode), DisplayName = "a concatenation, then an assignment")]
    [DataRow("a = 1 + _\r\n", "b = 2", typeof(AssignmentStatementNode), DisplayName = "an operator, a line continuation and a blank line, then an assignment")]
    [DataRow("a = 1 -", "b = 2", typeof(AssignmentStatementNode), DisplayName = "an operator the parse leaves unread, then an assignment")]
    [DataRow("Print #ff, 1 +", "Close #ff", null, DisplayName = "an item of a Print, then a Close")]
    [DataRow("Debug.Print 1 +", "Debug.Print 2", typeof(DebugPrintStatementNode), DisplayName = "an item of Debug.Print, then another")]
    [DataRow("Debug.Print 1 -", "Debug.Print 2", typeof(DebugPrintStatementNode), DisplayName = "an item of Debug.Print and an operator the parse leaves unread, then another")]
    public void TheStatementAfterADanglingOperator_IsReadOnItsOwn(string broken, string next, Type? kind)
    {
        var source = $"Public Sub Main()\r\n{broken}\r\n{next}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        var error = Assert.ContainsSingle(parse.SyntaxErrors);
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId);
        var nextLine = source.Split("\r\n").ToList().IndexOf(next);
        Assert.IsLessThan(nextLine, error.Location.Range.Start.Line, "the error is before the next statement");

        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        var statement = Assert.ContainsSingle(Descendants(member).OfType<StatementNode>().Where(node => node.SourceLocation.Range.Start.Line == nextLine));
        Assert.AreEqual(next, TextOf(source, statement.SourceLocation.Range));
        Assert.IsNotInstanceOfType<UnbuiltStatementTriviaNode>(statement);
        if (kind is not null)
        {
            Assert.IsInstanceOfType(statement, kind);
        }
    }

    // the parse's decision for an item of Debug.Print looks past the operator to the line end, and fails; the statement fails before that, at the operator,
    // and that is its one error.
    [TestMethod]
    [DataRow("-")]
    [DataRow("*")]
    [DataRow("=")]
    public void ADanglingOperatorAfterAnItemOfDebugPrint_IsOneErrorAtTheOperator(string op)
    {
        var statement = $"Debug.Print 1 {op}";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{statement}\r\nEnd Sub\r\n");

        var error = Assert.ContainsSingle(parse.SyntaxErrors);
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId);
        Assert.AreEqual(new SourceRange(new SourcePosition(1, statement.Length - 1), new SourcePosition(1, statement.Length - 1)), error.Location.Range);
    }

    // a statement after a colon is read on its own too: the end of the statement the error is in is the colon.
    [TestMethod]
    public void TheStatementAfterADanglingOperatorAndAColon_IsReadOnItsOwn()
    {
        var source = "Public Sub Main()\r\na = 1 -: b = 2\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, Assert.ContainsSingle(parse.SyntaxErrors).ErrorId);
        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        var assignments = member.Children.OfType<AssignmentStatementNode>().Select(node => TextOf(source, node.SourceLocation.Range)).ToArray();
        CollectionAssert.AreEqual(new[] { "a = 1 -", "b = 2" }, assignments);
    }

    // a comment after the operator is the comment it is, and the procedure ends where it does.
    [TestMethod]
    public void ACommentAfterADanglingOperator_IsKept_AndTheProcedureEndsWhereItDoes()
    {
        var source = "Public Sub Main()\r\na = 1 - ' c\r\nb = 2\r\nEnd Sub\r\nPublic Sub Other()\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, Assert.ContainsSingle(parse.SyntaxErrors).ErrorId);
        Assert.AreEqual("' c", TextOf(source, Assert.ContainsSingle(parse.Trivia.OfType<CommentTriviaNode>()).SourceLocation.Range));
        var members = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToArray();
        Assert.HasCount(2, members);
        Assert.AreEqual(new SourcePosition(3, 7), members[0].SourceLocation.Range.End, "the first procedure ends with its End Sub");
        CollectionAssert.AreEqual(new[] { "a = 1 -", "b = 2" },
            members[0].Children.OfType<AssignmentStatementNode>().Select(node => TextOf(source, node.SourceLocation.Range)).ToArray());
    }
}
