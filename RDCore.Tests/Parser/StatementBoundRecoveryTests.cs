using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
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

    // the operation is the last item the statement prints, unbuilt, its left operand in it: an item the parse read before it is the item it is, and a name
    // there is the reference it is.
    [TestMethod]
    [DataRow("Debug.Print 1 -", "1", typeof(LiteralExpressionNode))]
    [DataRow("Debug.Print 1 *", "1", typeof(LiteralExpressionNode))]
    [DataRow("Debug.Print 1 &", "1", typeof(LiteralExpressionNode))]
    [DataRow("Debug.Print x -", "x", typeof(SimpleNameExpressionNode))]
    [DataRow("Debug.Print 1; 2 &", "2", typeof(LiteralExpressionNode))]
    [DataRow("Debug.Print 1; 2 -", "2", typeof(LiteralExpressionNode))]
    [DataRow("Print #1, 1; 2 &", "2", typeof(LiteralExpressionNode))]
    [DataRow("Print #1, 1; x -", "x", typeof(SimpleNameExpressionNode))]
    public void ADanglingOperatorAfterAnItemToPrint_IsTheLastItem_Unbuilt_WithItsLeftOperand(string statement, string operand, Type kind)
    {
        var source = $"Public Sub Main()\r\n{statement}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        var print = Assert.ContainsSingle(member.Children.OfType<StatementNode>());
        var items = print.Children.OfType<PrintOutputItemNode>().ToArray();
        Assert.AreEqual(statement.Contains(';') ? 3 : 1, items.Length, "the items the statement is written with");
        var unbuilt = Assert.IsInstanceOfType<UnbuiltExpressionTriviaNode>(Assert.ContainsSingle(items[^1].Children));
        var input = Assert.ContainsSingle(unbuilt.Children);
        Assert.IsInstanceOfType(input, kind);
        Assert.AreEqual(operand, TextOf(source, input.SourceLocation.Range));
        Assert.IsFalse(Descendants(print).OfType<VBBinaryOperatorExpressionNode>().Any(), "no operation is built");
    }

    // the items before the operation are the items the statement is written with, as they are without the operator: its separators, a comma as a semicolon,
    // and its Spc and Tab clauses.
    [TestMethod]
    [DataRow("Debug.Print 1, 2", "-", DisplayName = "Debug.Print, a comma")]
    [DataRow("Debug.Print 1, 2", "+", DisplayName = "Debug.Print, a comma, and an operator the parse reads")]
    [DataRow("Debug.Print 1,, 2", "-", DisplayName = "Debug.Print, two commas")]
    [DataRow("Debug.Print 1, 2; 3", "-", DisplayName = "Debug.Print, a comma and a semicolon")]
    [DataRow("Debug.Print 1, Spc(2), 3", "-", DisplayName = "Debug.Print, a Spc clause between commas")]
    [DataRow("Debug.Print 1; Tab(4); 3", "-", DisplayName = "Debug.Print, a Tab clause between semicolons")]
    [DataRow("Debug.Print 1, Tab, 3", "-", DisplayName = "Debug.Print, a Tab clause with no column")]
    [DataRow("Debug.Print Spc(1), 2", "-", DisplayName = "Debug.Print, a Spc clause first")]
    [DataRow("Print #1, 1, Spc(2), 3", "-", DisplayName = "Print, a Spc clause between commas")]
    public void ADanglingOperatorAfterAnItemToPrint_KeepsTheItemsBeforeIt(string statement, string op)
    {
        static string[] ItemsOf(string text)
        {
            var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{text}\r\nEnd Sub\r\n");
            var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
            return Assert.ContainsSingle(member.Children.OfType<StatementNode>()).Children.OfType<PrintOutputItemNode>()
                .Select(item => item.Separator ?? item.Value?.GetType().Name ?? "(none)")
                .ToArray();
        }

        var written = ItemsOf(statement);
        var dangling = ItemsOf($"{statement} {op}");

        Assert.AreEqual(string.Join(" ", written[..^1]), string.Join(" ", dangling[..^1]), "the items before the operation");
        Assert.AreEqual(nameof(UnbuiltExpressionTriviaNode), dangling[^1], "the operation is the last item, unbuilt");
    }

    // a For whose header ends with an operator is the For it is: its body, its Next, the statements after it and the end of the procedure are read as they are.
    [TestMethod]
    [DataRow("For i = 1 To 2 -", "", DisplayName = "a For header, an operator the parse leaves unread")]
    [DataRow("For i = 1 To 2 +", "", DisplayName = "a For header, an operator the parse reads")]
    [DataRow("For i = 1 To 2 *", "", DisplayName = "a For header, a multiplication")]
    [DataRow("For i = 1 To 2 -", " ' c", DisplayName = "a For header, an operator the parse leaves unread, then a comment")]
    [DataRow("For i = 1 To 2 +", " ' c", DisplayName = "a For header, an operator the parse reads, then a comment")]
    [DataRow("For i = 1 To 2 -", ": b = 1", DisplayName = "a For header, an operator the parse leaves unread, then a colon")]
    [DataRow("For i = 1 To 9 Step 2 -", "", DisplayName = "a For header with a Step, an operator the parse leaves unread")]
    [DataRow("For i = 1 To 9 Step 2 +", "", DisplayName = "a For header with a Step, an operator the parse reads")]
    [DataRow("For Each x In xs -", "", DisplayName = "a For Each header, an operator the parse leaves unread")]
    [DataRow("For Each x In xs +", "", DisplayName = "a For Each header, an operator the parse reads")]
    public void AForWhoseHeaderEndsWithADanglingOperator_KeepsItsBody(string header, string after)
    {
        var source = $"Public Sub Main()\r\n{header}{after}\r\nb = 2\r\nNext\r\nc = 3\r\nEnd Sub\r\nPublic Sub Other()\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        var error = Assert.ContainsSingle(parse.SyntaxErrors);
        Assert.AreEqual(1, error.Location.Range.Start.Line, "the error is in the For's header");
        var members = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToArray();
        Assert.HasCount(2, members);
        Assert.AreEqual(5, members[0].SourceLocation.Range.End.Line, "the procedure ends at its End Sub");
        var loop = Assert.ContainsSingle(members[0].Children.Where(node => node is ForStatementNode or ForEachStatementNode));
        Assert.AreEqual(3, loop.SourceLocation.Range.End.Line, "the For ends at its Next");
        var body = loop is ForStatementNode @for ? @for.Body : ((ForEachStatementNode)loop).Body;
        Assert.IsTrue(body.Children.OfType<AssignmentStatementNode>().Any(node => TextOf(source, node.SourceLocation.Range) == "b = 2"), "the For's body");
        Assert.IsTrue(members[0].Children.OfType<AssignmentStatementNode>().Any(node => TextOf(source, node.SourceLocation.Range) == "c = 3"), "the statement after the For");
        if (after.TrimStart().StartsWith('\''))
        {
            Assert.AreEqual(after.TrimStart(), TextOf(source, Assert.ContainsSingle(parse.Trivia.OfType<CommentTriviaNode>()).SourceLocation.Range));
        }

        // the header's last expression is the operation, unbuilt: `2 -`, its operand and its operator.
        var last = new[] { " To ", " Step ", " In " }.Select(word => header.LastIndexOf(word, StringComparison.Ordinal) + word.Length).Max();
        var operation = Assert.ContainsSingle(Descendants(loop).OfType<UnbuiltExpressionTriviaNode>());
        Assert.AreEqual(header[last..], TextOf(source, operation.SourceLocation.Range), "the header's last expression");
    }

    // a statement of a For's body that ends with an operator is a statement of the body: the body goes on after it, and the For ends at its Next.
    [TestMethod]
    [DataRow("For i = 1 To 2", "a = 1 -", "Next", DisplayName = "an assignment, in a For")]
    [DataRow("For Each x In xs", "a = 1 -", "Next x", DisplayName = "an assignment, in a For Each")]
    [DataRow("For i = 1 To 2", "Debug.Print 1, 2 -", "Next i", DisplayName = "an item of Debug.Print, in a For")]
    [DataRow("For Each x In xs", "Print #1, 1; 2 *", "Next", DisplayName = "an item of a Print, in a For Each")]
    public void AStatementOfAForBodyThatEndsWithADanglingOperator_KeepsTheBody(string header, string broken, string closes)
    {
        var source = $"Public Sub Main()\r\n{header}\r\n{broken}\r\nb = 2\r\n{closes}\r\nc = 3\r\nEnd Sub\r\nPublic Sub Other()\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        Assert.AreEqual(2, Assert.ContainsSingle(parse.SyntaxErrors).Location.Range.Start.Line, "the error is in the statement of the body");
        var members = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToArray();
        Assert.HasCount(2, members);
        Assert.AreEqual(6, members[0].SourceLocation.Range.End.Line, "the procedure ends at its End Sub");
        var loop = Assert.ContainsSingle(members[0].Children.Where(node => node is ForStatementNode or ForEachStatementNode));
        Assert.AreEqual(4, loop.SourceLocation.Range.End.Line, $"the For ends at its {closes}");
        var body = loop is ForStatementNode @for ? @for.Body : ((ForEachStatementNode)loop).Body;
        Assert.IsTrue(body.Children.Any(node => node.SourceLocation.Range.Start.Line == 2), "the statement is the body's");
        Assert.IsTrue(body.Children.OfType<AssignmentStatementNode>().Any(node => TextOf(source, node.SourceLocation.Range) == "b = 2"), "the body goes on after it");
        Assert.IsTrue(members[0].Children.OfType<AssignmentStatementNode>().Any(node => TextOf(source, node.SourceLocation.Range) == "c = 3"), "the statement after the For");
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
    [DataRow("a = 1 -", " ' c", DisplayName = "an operator the parse leaves unread, then a comment")]
    [DataRow("a = 1 &", "'c", DisplayName = "a concatenation, then a comment with no space before it")]
    [DataRow("a = 1 /", " ' c", DisplayName = "a division, then a comment")]
    [DataRow("a = 1 &", " ' c: d", DisplayName = "a concatenation, then a comment with a colon in it")]
    [DataRow("a = 1 ^", " Rem c", DisplayName = "an exponentiation, then a Rem comment")]
    public void ACommentAfterADanglingOperator_IsKept_AndTheProcedureEndsWhereItDoes(string broken, string comment)
    {
        var source = $"Public Sub Main()\r\n{broken}{comment}\r\nb = 2\r\nEnd Sub\r\nPublic Sub Other()\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        var error = Assert.ContainsSingle(parse.SyntaxErrors);
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId);
        Assert.AreEqual(1, error.Location.Range.Start.Line, "the error is in the statement's line");
        Assert.AreEqual(comment.TrimStart(), TextOf(source, Assert.ContainsSingle(parse.Trivia.OfType<CommentTriviaNode>()).SourceLocation.Range));
        var members = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToArray();
        Assert.HasCount(2, members);
        Assert.AreEqual(new SourcePosition(3, 7), members[0].SourceLocation.Range.End, "the first procedure ends with its End Sub");
        // an operation the parse reads, its right operand missing, ends after the blank it read before the comment.
        CollectionAssert.AreEqual(new[] { broken, "b = 2" },
            members[0].Children.OfType<AssignmentStatementNode>().Select(node => TextOf(source, node.SourceLocation.Range).TrimEnd()).ToArray());
    }

    // a statement the parse cannot read ends at its comment too, whether the parse skips what it cannot read, or supplies the token the statement is
    // missing before the comment.
    [TestMethod]
    [DataRow("State = this.", DisplayName = "a member access with no member, skipped to the comment")]
    [DataRow("x = Foo(Bar(", DisplayName = "two unclosed argument lists, skipped to the comment")]
    [DataRow("Set p = A.Offset(IIf(this.", DisplayName = "a closing parenthesis supplied before the comment")]
    public void ACommentAfterAStatementTheParseCannotRead_IsKept_AndTheNextStatementIsReadOnItsOwn(string broken)
    {
        var source = $"Public Sub Main()\r\n{broken} ' c\r\nb = 2\r\nEnd Sub\r\nPublic Sub Other()\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        Assert.IsNotEmpty(parse.SyntaxErrors);
        Assert.IsTrue(parse.SyntaxErrors.All(error => error.Location.Range.Start.Line == 1), "the errors are in the statement's line");
        Assert.AreEqual("' c", TextOf(source, Assert.ContainsSingle(parse.Trivia.OfType<CommentTriviaNode>()).SourceLocation.Range));
        var members = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToArray();
        Assert.HasCount(2, members);
        Assert.AreEqual(new SourcePosition(3, 7), members[0].SourceLocation.Range.End, "the first procedure ends with its End Sub");
        Assert.AreEqual("b = 2", TextOf(source, Assert.ContainsSingle(members[0].Children.OfType<AssignmentStatementNode>()
            .Where(node => node.SourceLocation.Range.Start.Line == 2)).SourceLocation.Range));
    }

    // the errors of the directives are not the parse's: a later one stays when the parse reports an error before it.
    [TestMethod]
    public void AnErrorOfADirective_AfterADanglingOperator_IsKept()
    {
        var source = "Public Sub Main()\r\na = 1 +\r\n#If x = #13/45/2000# Then\r\n#End If\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, parse.SyntaxErrors.Select(error => error.Location.Range.Start.Line).ToArray());
    }
}
