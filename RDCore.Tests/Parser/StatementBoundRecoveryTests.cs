using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.Runtime.LanguageCore.Errors;

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

    private static IEnumerable<SyntaxNode> BodyOf(SyntaxNode block) => block switch
    {
        ForStatementNode @for => @for.Body.Children,
        ForEachStatementNode each => each.Body.Children,
        IfBlockStatementNode @if => @if.Body.Children,
        ElseIfBlockStatementNode elseIf => elseIf.Body.Children,
        _ => [],
    };

    [TestMethod]
    [DataRow("a = 1 +", "b = 2", typeof(AssignmentStatementNode), DisplayName = "an addition, then an assignment")]
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
    [DataRow("+")]
    [DataRow("-")]
    [DataRow("*")]
    [DataRow("=")]
    [DataRow("Mod")]
    [DataRow("Like")]
    [DataRow("And")]
    public void ADanglingOperatorAfterAnItemOfDebugPrint_IsOneErrorAtTheOperator(string op)
    {
        var statement = $"Debug.Print 1 {op}";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{statement}\r\nEnd Sub\r\n");

        var error = Assert.ContainsSingle(parse.SyntaxErrors);
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId);
        var at = new SourcePosition(1, statement.Length - op.Length);
        Assert.AreEqual(new SourceRange(at, at), error.Location.Range);
    }

    // a statement the parse fails in, and fails in again after - the rule the statement is in fails at what its recovery left of it - is one syntax error,
    // the first.
    [TestMethod]
    [DataRow("Dim For As Long", 4, DisplayName = "a statement keyword as the name of a variable")]
    [DataRow("Dim Sub As Long", 4, DisplayName = "a procedure keyword as the name of a variable")]
    [DataRow("Dim 1 As Long", 4, DisplayName = "a number as the name of a variable")]
    public void AStatementTheParseFailsInTwice_IsOneSyntaxError_TheFirst(string statement, int column)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{statement}\r\nEnd Sub\r\n");

        var error = Assert.ContainsSingle(parse.SyntaxErrors, string.Join(" | ", parse.SyntaxErrors.Select(e => $"{e.Location.Range}: {e.Verbose}")));
        Assert.AreEqual(new SourcePosition(1, column), error.Location.Range.Start);
    }

    // the errors of two statements are two, the statements on one line too.
    [TestMethod]
    [DataRow("a = 1 -: b = = 2", DisplayName = "on one line")]
    [DataRow("a = 1 -\r\nb = = 2", DisplayName = "on two lines")]
    public void TheErrorsOfTwoStatements_AreBothReported(string statements)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{statements}\r\nEnd Sub\r\n");

        Assert.HasCount(2, parse.SyntaxErrors, string.Join(" | ", parse.SyntaxErrors.Select(e => $"{e.Location.Range}: {e.Verbose}")));
    }

    // a token that ends a block around where the parse expects a statement is no statement of its own: the block a statement is missing from, or whose end
    // is missing, ends at it, the procedure ends at its End Sub, and the next procedure is read on its own.
    [TestMethod]
    [DataRow("Public Sub M()\r\nIf a Then\r\nFor i = 1 To 2\r\nEnd If\r\nEnd Sub", DisplayName = "an End If after a For with no Next")]
    [DataRow("Public Sub M()\r\nNext\r\nEnd Sub", DisplayName = "a Next with no For")]
    [DataRow("Public Sub M()\r\nDo While x\r\nb = 2\r\nEnd Sub", DisplayName = "a Do with no Loop")]
    [DataRow("Public Sub M()\r\nIf Not x Then\r\nb = 2\r\nEnd Sub", DisplayName = "an If with no End If")]
    [DataRow("Public Sub M()\r\nWith New Collection\r\nb = 2\r\nEnd Sub", DisplayName = "a With with no End With")]
    public void ATokenThatEndsABlock_IsNoStatement_AndTheNextProcedureIsReadOnItsOwn(string procedure)
    {
        var source = $"{procedure}\r\nSub N()\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        Assert.IsNotEmpty(parse.SyntaxErrors);
        var lines = source.Split("\r\n").ToList();
        var next = lines.IndexOf("Sub N()");
        var members = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToArray();
        Assert.IsGreaterThanOrEqualTo(next - 1, members[0].SourceLocation.Range.End.Line, "the procedure ends at its End Sub");
        Assert.IsLessThanOrEqualTo(0, members[0].SourceLocation.Range.End.CompareTo(new SourcePosition(next, 0)), "the procedure ends before the next one");
        Assert.IsTrue(members.Any(member => member.SourceLocation.Range.Start.Line == next), "the next procedure is read on its own");
    }

    // a word that ends a block, where no block around it ends (a Loop with no Do, an End If with no If), is a statement no rule reads: the block goes on
    // after it, and the procedure ends at its End Sub, not at the next procedure.
    [TestMethod]
    [DataRow("Loop", DisplayName = "a Loop with no Do")]
    [DataRow("With x\r\nLoop\r\nEnd With", DisplayName = "a Loop with no Do, in a With")]
    [DataRow("With x\r\nEnd If\r\nEnd With", DisplayName = "an End If with no If, in a With")]
    [DataRow("Wend", DisplayName = "a Wend with no While")]
    [DataRow("Do\r\nIf a Then\r\nLoop", DisplayName = "a Loop in an If with no End If, in a Do")]
    public void AWordThatEndsNoBlockAroundIt_IsAStatement_AndTheProcedureEndsAtItsEndSub(string body)
    {
        foreach (var after in new[] { "\r\nSub N()\r\nEnd Sub\r\n", "" })
        {
            var source = $"Public Sub M()\r\n{body}\r\nb = 2\r\nEnd Sub{after}";
            var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

            Assert.IsNotEmpty(parse.SyntaxErrors);
            var lines = source.Split("\r\n").ToList();
            var members = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToArray();
            Assert.AreEqual(new SourcePosition(lines.IndexOf("End Sub"), "End Sub".Length), members[0].SourceLocation.Range.End, "the procedure ends at its End Sub");
            Assert.IsTrue(Descendants(members[0]).OfType<StatementNode>().Any(node => TextOf(source, node.SourceLocation.Range) == "b = 2"),
                "the statement after the word is the procedure's");
            if (after.Length > 0)
            {
                var next = Assert.ContainsSingle(members[1..]);
                Assert.AreEqual(lines.IndexOf("Sub N()"), next.SourceLocation.Range.Start.Line, "the next procedure is read on its own");
            }
        }
    }

    // a procedure with no end ends where the next procedure's header begins, its modifiers included (MS-VBAL §5.3.1): the header is no statement of the
    // procedure, and the recovery neither deletes it nor skips past it. The next procedure is read on its own, its range its own, and the syntax error is
    // at its header.
    [TestMethod]
    [DataRow("Sub N()", "End Sub", DisplayName = "a Sub")]
    [DataRow("Public Sub N()", "End Sub", DisplayName = "a Public Sub")]
    [DataRow("Private Sub N()", "End Sub", DisplayName = "a Private Sub")]
    [DataRow("Friend Sub N()", "End Sub", DisplayName = "a Friend Sub")]
    [DataRow("Static Sub N()", "End Sub", DisplayName = "a Static Sub")]
    [DataRow("Public Static Sub N()", "End Sub", DisplayName = "a Public Static Sub")]
    [DataRow("Public Function N() As Long", "End Function", DisplayName = "a Public Function")]
    [DataRow("Property Get N() As Long", "End Property", DisplayName = "a Property Get")]
    [DataRow("Private Property Let N(ByVal v As Long)", "End Property", DisplayName = "a Private Property Let")]
    public void AProcedureWithNoEnd_EndsAtTheNextProcedure_WhichIsReadOnItsOwn(string header, string end)
    {
        var source = $"Public Sub M()\r\nStatic a As Long\r\na = 1\r\n\r\n{header}\r\nb = 2\r\n{end}\r\n";

        AssertTheNextProcedureIsReadOnItsOwn(source, header, end);
    }

    // so does one whose last block has no end either: the block ends at the header too.
    [TestMethod]
    [DataRow("If a Then", DisplayName = "in an If")]
    [DataRow("For i = 1 To 2", DisplayName = "in a For")]
    [DataRow("For Each v In c", DisplayName = "in a For Each")]
    [DataRow("Do While a", DisplayName = "in a Do")]
    [DataRow("While a", DisplayName = "in a While")]
    [DataRow("With x", DisplayName = "in a With")]
    [DataRow("Select Case a\r\nCase 1", DisplayName = "in a Select Case")]
    public void AProcedureWithNoEnd_InABlockWithNoEnd_EndsAtTheNextProcedure(string opened)
    {
        foreach (var header in new[] { "Sub N()", "Public Sub N()" })
        {
            var source = $"Public Sub M()\r\n{opened}\r\na = 1\r\n\r\n{header}\r\nb = 2\r\nEnd Sub\r\n";

            AssertTheNextProcedureIsReadOnItsOwn(source, header, "End Sub");
        }
    }

    private static void AssertTheNextProcedureIsReadOnItsOwn(string source, string header, string end)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        var lines = source.Split("\r\n").ToList();
        var at = new SourcePosition(lines.IndexOf(header), 0);
        var members = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToArray();
        Assert.HasCount(2, members, source);
        Assert.AreEqual("M", members[0].Name);
        Assert.AreEqual(at, members[0].SourceLocation.Range.End, "the procedure with no end ends where the next one begins");
        Assert.AreEqual("N", members[1].Name);
        Assert.AreEqual(new SourceRange(at, new SourcePosition(lines.IndexOf(end), end.Length)), members[1].SourceLocation.Range, "the next procedure's range");
        Assert.IsTrue(Descendants(members[1]).OfType<StatementNode>().Any(node => TextOf(source, node.SourceLocation.Range) == "b = 2"),
            "the next procedure's statement is its own");

        var error = parse.SyntaxErrors.First();
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId);
        Assert.AreEqual(at, error.Location.Range.Start, "the error is at the header");
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
    [DataRow("Debug.Print 1 +", "1", typeof(LiteralExpressionNode))]
    [DataRow("Debug.Print 1; 2 +", "2", typeof(LiteralExpressionNode))]
    [DataRow("Print #1, 1; 2 +", "2", typeof(LiteralExpressionNode))]
    [DataRow("Print #1, 1; 2 &", "2", typeof(LiteralExpressionNode))]
    [DataRow("Print #1, 1; x -", "x", typeof(SimpleNameExpressionNode))]
    [DataRow("Debug.Print 1 Mod", "1", typeof(LiteralExpressionNode))]
    [DataRow("Debug.Print x And", "x", typeof(SimpleNameExpressionNode))]
    [DataRow("Debug.Print 1; 2 Like", "2", typeof(LiteralExpressionNode))]
    [DataRow("Print #1, 1; x Or", "x", typeof(SimpleNameExpressionNode))]
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

    // a binary operator right before a word of a block's header (To, Step, Then), or a run of operators at the end of a For's header, is one error at the
    // operator: the block is the block it is, its body, its end, the statements after it and the end of the procedure read as they are.
    [TestMethod]
    [DataRow("For i = 1 - To 2", "Next", typeof(ForStatementNode), 10, DisplayName = "an operator before To")]
    [DataRow("For i = 1 To 2 - Step 1", "Next", typeof(ForStatementNode), 15, DisplayName = "an operator before Step")]
    [DataRow("For i = 1 To 2 * Step 1", "Next", typeof(ForStatementNode), 15, DisplayName = "a multiplication before Step")]
    [DataRow("For i = 1 To 2 - -", "Next", typeof(ForStatementNode), 15, DisplayName = "two operators at the end of a For header")]
    [DataRow("For i = 1 To 2 - - ' c", "Next", typeof(ForStatementNode), 15, DisplayName = "two operators at the end of a For header, then a comment")]
    [DataRow("For i = 1 To 9 Step 2 + -", "Next", typeof(ForStatementNode), 22, DisplayName = "two operators at the end of a Step")]
    [DataRow("For Each x In xs - -", "Next", typeof(ForEachStatementNode), 17, DisplayName = "two operators at the end of a For Each header")]
    [DataRow("If x - Then", "End If", typeof(IfBlockStatementNode), 5, DisplayName = "an operator before Then")]
    [DataRow("If x And Then", "End If", typeof(IfBlockStatementNode), 5, DisplayName = "an operator the parse reads as a name, before Then")]
    [DataRow("If x = Then", "End If", typeof(IfBlockStatementNode), 5, DisplayName = "a comparison before Then")]
    public void AnOperatorRightBeforeAWordOfABlocksHeader_IsOneErrorAtTheOperator_AndTheBlockIsKept(string header, string closes, Type kind, int column)
    {
        var source = $"Public Sub Main()\r\n{header}\r\nb = 2\r\n{closes}\r\nc = 3\r\nEnd Sub\r\nPublic Sub Other()\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        var error = Assert.ContainsSingle(parse.SyntaxErrors, string.Join(" | ", parse.SyntaxErrors.Select(e => $"{e.Location.Range}: {e.Verbose}")));
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId);
        Assert.AreEqual(new SourcePosition(1, column), error.Location.Range.Start, "the error is at the operator");
        var members = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToArray();
        Assert.HasCount(2, members);
        Assert.AreEqual(5, members[0].SourceLocation.Range.End.Line, "the procedure ends at its End Sub");
        var block = Assert.ContainsSingle(members[0].Children.Where(kind.IsInstanceOfType));
        Assert.AreEqual(new SourcePosition(3, closes.Length), block.SourceLocation.Range.End, $"the block ends at its {closes}");
        Assert.IsTrue(BodyOf(block).OfType<AssignmentStatementNode>().Any(node => TextOf(source, node.SourceLocation.Range) == "b = 2"), "the block's body");
        Assert.IsFalse(Descendants(members[0]).OfType<UnbuiltStatementTriviaNode>().Any(), "no statement is unbuilt");
        Assert.IsTrue(members[0].Children.OfType<AssignmentStatementNode>().Any(node => TextOf(source, node.SourceLocation.Range) == "c = 3"), "the statement after the block");
    }

    // the same in an ElseIf: the If keeps its branches.
    [TestMethod]
    public void AnOperatorRightBeforeTheThenOfAnElseIf_IsOneErrorAtTheOperator_AndTheIfKeepsItsBranches()
    {
        var source = "Public Sub Main()\r\nIf a Then\r\nb = 1\r\nElseIf x - Then\r\nb = 2\r\nElse\r\nb = 3\r\nEnd If\r\nc = 3\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        Assert.AreEqual(new SourcePosition(3, 9), Assert.ContainsSingle(parse.SyntaxErrors).Location.Range.Start);
        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        var block = Assert.ContainsSingle(member.Children.OfType<IfBlockStatementNode>());
        Assert.AreEqual(7, block.SourceLocation.Range.End.Line, "the If ends at its End If");
        Assert.IsTrue(BodyOf(Assert.ContainsSingle(block.ElseIfBlocks)).OfType<AssignmentStatementNode>().Any(node => TextOf(source, node.SourceLocation.Range) == "b = 2"));
        Assert.IsNotNull(block.ElseBlock);
        Assert.IsTrue(member.Children.OfType<AssignmentStatementNode>().Any(node => TextOf(source, node.SourceLocation.Range) == "c = 3"));
    }

    // valid code where a name the grammar reads as a keyword follows an operator is read as it is written, with recovery too: Step is no reserved word
    // (MS-VBAL §3.3.5.2).
    [TestMethod]
    [DataRow("For i = 1 To n - Step\r\nb = 2\r\nNext", DisplayName = "a variable named Step, at the end of a For header")]
    [DataRow("For i = 1 To n - Step Step 2\r\nb = 2\r\nNext", DisplayName = "a variable named Step, then a Step")]
    public void AnOperatorBeforeANameThatIsAWord_InValidCode_IsTheOperationItIs(string body)
    {
        // a syntax error in another procedure makes the whole module parse with recovery.
        var source = $"Public Sub Main()\r\n{body}\r\nEnd Sub\r\nPublic Sub Other()\r\na = 1 -\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        var other = source.Split("\r\n").ToList().IndexOf("a = 1 -");
        Assert.IsTrue(parse.SyntaxErrors.All(error => error.Location.Range.Start.Line == other), string.Join(" | ", parse.SyntaxErrors.Select(e => $"{e.Location.Range}: {e.Verbose}")));
        var loop = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().First().Children.OfType<ForStatementNode>());
        Assert.IsInstanceOfType<VBBinaryOperatorExpressionNode>(loop.EndExpression);
    }

    // what an unbuilt node keeps of its source is the text at its range: no token the parse made up for one missing, and no blank after its last token.
    [TestMethod]
    [DataRow("Call Foo(-", DisplayName = "a call whose closing parenthesis the parse made up")]
    [DataRow("x = Foo(Bar(-", DisplayName = "two calls whose closing parentheses the parse made up")]
    [DataRow("a = (1 +  ", DisplayName = "an operation in parentheses, then blanks")]
    [DataRow("a = (1 + _\r\n  ", DisplayName = "an operation in parentheses, then a line continuation")]
    [DataRow("a = (1 +  ' c", DisplayName = "an operation in parentheses, then blanks and a comment")]
    public void AnUnbuiltNode_KeepsTheTextAtItsRange(string broken)
    {
        var source = $"Public Sub Main()\r\n{broken}\r\nb = 2\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);

        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        var unbuilt = Descendants(member).Where(node => node is UnbuiltExpressionTriviaNode or UnbuiltStatementTriviaNode).ToArray();
        Assert.IsNotEmpty(unbuilt);
        foreach (var node in unbuilt)
        {
            var kept = node is UnbuiltExpressionTriviaNode expression ? expression.Source : ((UnbuiltStatementTriviaNode)node).Source;
            Assert.AreEqual(TextOf(source, node.SourceLocation.Range), kept, $"{node.GetType().Name} at {node.SourceLocation.Range}");
            Assert.AreEqual(kept.TrimEnd(), kept, $"{node.GetType().Name} at {node.SourceLocation.Range}: no blank after its last token");
        }

        Assert.IsTrue(member.Children.OfType<AssignmentStatementNode>().Any(node => TextOf(source, node.SourceLocation.Range) == "b = 2"), "the next statement");
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
    [DataRow("a = 1 +", " ' c", DisplayName = "an addition, then a comment")]
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
