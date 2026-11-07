using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// An operator-identifier is a reserved identifier (<strong>MS-VBAL §3.3.5.2</strong>). After an item of a list to print, it is an operator, and with no
/// right operand it is a syntax error, as a symbol operator is. After a <c>.</c> or a <c>!</c>, it names a member, as any keyword does.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.6.1 Syntax Errors")]
public sealed class PrintItemKeywordOperatorTests
{
    private static ModuleParseResult Parse(string statement)
        => new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{statement}\r\nEnd Sub\r\n");

    [TestMethod]
    [DataRow("Debug.Print 1 Like")]
    [DataRow("Debug.Print 1 Is")]
    [DataRow("Debug.Print 1 Mod")]
    [DataRow("Debug.Print 1 And")]
    [DataRow("Debug.Print 1 Or")]
    [DataRow("Debug.Print 1 Xor")]
    [DataRow("Debug.Print 1 Eqv")]
    [DataRow("Debug.Print 1 Imp")]
    [DataRow("Debug.Print x; Mod", DisplayName = "an operator alone after a separator")]
    [DataRow("Debug.Print 1; 2 Like", DisplayName = "after the second item")]
    [DataRow("Debug.Print Spc(2) Mod", DisplayName = "after a Spc clause")]
    [DataRow("Debug.Print Tab(2) And", DisplayName = "after a Tab clause")]
    [DataRow("Print #1, 1 Like", DisplayName = "after an item of a Print")]
    [DataRow("Print #1, x; Or", DisplayName = "alone after a separator of a Print")]
    [DataRow("Print #1, Tab(2) Xor", DisplayName = "after a Tab clause of a Print")]
    [DataRow("Write #1, 1 And", DisplayName = "after an item of a Write")]
    [DataRow("Write #1, 1, 2 Imp", DisplayName = "after the second item of a Write")]
    public void AKeywordOperatorAfterAnItemToPrint_IsOneSyntaxError(string statement)
    {
        var parse = Parse(statement);

        var error = Assert.ContainsSingle(parse.SyntaxErrors, Said(parse));
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId);
        Assert.AreEqual(1, error.Location.Range.Start.Line);
    }

    // an operator keyword names a member, and the item that names it is printed.
    [TestMethod]
    [DataRow("Debug.Print x.Mod")]
    [DataRow("Debug.Print x.And; d!Xor")]
    [DataRow("Print #1, x.Like")]
    [DataRow("Write #1, x.Is, x.Or")]
    [DataRow("Debug.Print 1 Mod 2; 3 And 4")]
    public void AnOperatorKeyword_ThatNamesAMember_IsAnItemToPrint(string statement)
    {
        var parse = Parse(statement);

        Assert.IsEmpty(parse.SyntaxErrors, Said(parse));
        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        Assert.IsNotInstanceOfType<UnbuiltStatementTriviaNode>(Assert.ContainsSingle(member.Children.OfType<StatementNode>()));
    }

    // an operator keyword is no name of a variable: a statement that uses it as one is one syntax error, at the keyword.
    [TestMethod]
    [DataRow("Mod = 1", 0)]
    [DataRow("And = 1", 0)]
    [DataRow("Dim Mod As Long", 4)]
    [DataRow("Dim Like", 4)]
    public void AnOperatorKeyword_AsTheNameOfAVariable_IsOneSyntaxError_AtTheKeyword(string statement, int column)
    {
        var parse = Parse(statement);

        var error = Assert.ContainsSingle(parse.SyntaxErrors, Said(parse));
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId);
        Assert.AreEqual(new SourcePosition(1, column), error.Location.Range.Start);
    }

    // the name of a second variable of the statement too.
    [TestMethod]
    public void AnOperatorKeyword_AsTheNameOfASecondVariable_IsOneSyntaxError()
    {
        var parse = Parse("Dim a As Long, Or As Long");

        Assert.AreEqual(1, Assert.ContainsSingle(parse.SyntaxErrors, Said(parse)).Location.Range.Start.Line);
    }

    // an error inside a statement stays in it: the statement is kept where it is, and the next one is read on its own.
    [TestMethod]
    [DataRow("Mod = 1", DisplayName = "an operator keyword where a statement begins")]
    [DataRow("Dim Mod As Long", DisplayName = "an operator keyword as the name of a variable")]
    [DataRow("Dim For As Long", DisplayName = "a statement keyword as the name of a variable")]
    [DataRow("Dim 1 As Long", DisplayName = "a number as the name of a variable")]
    public void AnErrorInsideAStatement_KeepsTheStatement_AndTheNextIsReadOnItsOwn(string statement)
    {
        var parse = Parse($"{statement}\r\nb = 2");

        Assert.AreEqual(1, Assert.ContainsSingle(parse.SyntaxErrors, Said(parse)).Location.Range.Start.Line);
        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        var statements = member.Children.OfType<StatementNode>().ToArray();
        Assert.IsTrue(statements.Any(node => node.SourceLocation.Range.Start.Line == 1), "the statement with the error is kept");
        Assert.IsTrue(statements.OfType<AssignmentStatementNode>().Any(node => node.SourceLocation.Range.Start.Line == 2), "the next statement is read on its own");
    }

    // two statements of one line, each with its error: each is reported, each is kept as the statement no rule reads that it is, and the procedure ends where
    // it does.
    [TestMethod]
    public void TwoStatementsOfALine_EachWithAnOperatorKeyword_AreTwoSyntaxErrors()
    {
        var parse = Parse("Mod = 1: And = 2");

        Assert.HasCount(2, parse.SyntaxErrors, Said(parse));
        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        CollectionAssert.AreEqual(new[] { "Mod = 1", "And = 2" }, member.Children.OfType<UnbuiltStatementTriviaNode>().Select(node => node.Source).ToArray());
        Assert.AreEqual(2, member.SourceLocation.Range.End.Line, "the procedure ends at its End Sub");
    }

    // statements no rule reads, one after the other: each is kept, each is its own error, and the statement after them is read on its own.
    [TestMethod]
    [DataRow("Mod = 1\r\nAnd = 2", 2, DisplayName = "two operator keywords where statements begin")]
    [DataRow("Mod = 1\r\n= 2", 2, DisplayName = "an operator keyword, then an operator, where statements begin")]
    [DataRow("= 1\r\n) = 2", 2, DisplayName = "an operator, then a parenthesis, where statements begin")]
    [DataRow("Mod = 1\r\n\r\nAnd = 2", 2, DisplayName = "two operator keywords, a blank line between them")]
    [DataRow("Mod = 1\r\n' c\r\nAnd = 2", 2, DisplayName = "two operator keywords, a comment line between them")]
    [DataRow("Mod = 1: And = 2", 2, DisplayName = "two operator keywords on one line")]
    [DataRow("Mod = 1\r\nAnd = 2\r\nOr = 3", 3, DisplayName = "three operator keywords where statements begin")]
    public void StatementsNoRuleReads_OneAfterTheOther_AreEachKept_AndTheNextIsReadOnItsOwn(string statements, int count)
    {
        var parse = Parse($"{statements}\r\nb = 3");
        var lines = statements.Split("\r\n").Length;

        Assert.HasCount(count, parse.SyntaxErrors, Said(parse));
        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        Assert.HasCount(count, member.Children.OfType<UnbuiltStatementTriviaNode>());
        Assert.IsTrue(member.Children.OfType<AssignmentStatementNode>().Any(node => node.SourceLocation.Range.Start.Line == lines + 1), "the next statement is read on its own");
        Assert.AreEqual(lines + 2, member.SourceLocation.Range.End.Line, "the procedure ends at its End Sub");
    }

    // a label before the statement is no part of it: its colon ends no statement, and the error inside the statement stays in it. The error is at the word
    // the statement goes wrong at, and the statement kept is the statement alone.
    [TestMethod]
    [DataRow("L1: Dim For As Long", "Dim For As Long", 8, DisplayName = "a label, then a declaration with no name")]
    [DataRow("20: Dim For As Long", "Dim For As Long", 8, DisplayName = "a line number and its colon, then a declaration with no name")]
    [DataRow("L1: Mod = 1", "Mod = 1", 4, DisplayName = "a label, then an operator keyword where a statement begins")]
    [DataRow("20 Mod = 2", "Mod = 2", 3, DisplayName = "a line number, then an operator keyword where a statement begins")]
    [DataRow("10 L1: Mod = 2", "Mod = 2", 7, DisplayName = "a line number and a label, then an operator keyword where a statement begins")]
    public void AnErrorInsideAStatementAfterALabel_KeepsTheStatement_AndTheNextIsReadOnItsOwn(string statement, string kept, int column)
    {
        var parse = Parse($"{statement}\r\nb = 3");

        var error = Assert.ContainsSingle(parse.SyntaxErrors, Said(parse));
        Assert.AreEqual(new SourcePosition(1, column), error.Location.Range.Start, Said(parse));
        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        Assert.AreEqual(kept, Assert.ContainsSingle(member.Children.OfType<UnbuiltStatementTriviaNode>()).Source, "the statement alone is kept");
        Assert.IsTrue(member.Children.OfType<AssignmentStatementNode>().Any(node => node.SourceLocation.Range.Start.Line == 2), "the next statement is read on its own");
        Assert.AreEqual(3, member.SourceLocation.Range.End.Line, "the procedure ends at its End Sub");
    }

    // a statement no rule reads in the body of a loop or a block is a statement of that body: the loop or the block ends at its own end, and the statement
    // after it is the procedure's.
    [TestMethod]
    [DataRow("For i = 1 To 2", "Mod = 1\r\nAnd = 2", "Next", DisplayName = "in a For")]
    [DataRow("For i = 1 To 2", "L1: Mod = 1\r\n20 And = 2", "Next", DisplayName = "after labels, in a For")]
    [DataRow("For Each x In xs", "Mod = 1\r\n' c\r\nAnd = 2", "Next x", DisplayName = "in a For Each, a comment line between them")]
    [DataRow("Do", "Mod = 1\r\nAnd = 2", "Loop", DisplayName = "in a Do")]
    [DataRow("Do", "Mod = 1", "Loop While a", DisplayName = "in a Do with its condition after Loop")]
    [DataRow("Do While a", "Mod = 1", "Loop", DisplayName = "in a Do While")]
    [DataRow("While a", "Mod = 1", "Wend", DisplayName = "in a While")]
    [DataRow("With a", "Mod = 1", "End With", DisplayName = "in a With")]
    [DataRow("If a Then", "Mod = 1", "End If", DisplayName = "in an If")]
    [DataRow("For i = 1 To 2", "Dim For As Long", "Next", DisplayName = "a declaration with no name, in a For")]
    [DataRow("Do", "Dim 1 As Long", "Loop", DisplayName = "a declaration with no name, in a Do")]
    public void AStatementNoRuleReads_InTheBodyOfABlock_IsAStatementOfIt(string opens, string statements, string closes)
    {
        var lines = statements.Split("\r\n").Length;
        var count = statements.Split("\r\n").Count(line => !line.StartsWith('\''));
        var parse = Parse($"{opens}\r\n{statements}\r\nb = 3\r\n{closes}\r\nc = 4");

        Assert.HasCount(count, parse.SyntaxErrors, Said(parse));
        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        var block = member.Children.First();
        Assert.AreEqual(lines + 3, block.SourceLocation.Range.End.Line, $"the block ends at its {closes}");
        var body = BodyOf(block);
        Assert.HasCount(count, body.OfType<UnbuiltStatementTriviaNode>());
        Assert.IsTrue(body.OfType<AssignmentStatementNode>().Any(node => node.SourceLocation.Range.Start.Line == lines + 2), "the statement after them is the body's");
        Assert.IsTrue(member.Children.OfType<AssignmentStatementNode>().Any(node => node.SourceLocation.Range.Start.Line == lines + 4), "the statement after the block is the procedure's");
        Assert.AreEqual(lines + 5, member.SourceLocation.Range.End.Line, "the procedure ends at its End Sub");
    }

    // the two forms of a Do whose condition is not before its body are told apart after Loop: an error in the body leaves either one a Do.
    [TestMethod]
    [DataRow("Loop", DisplayName = "a Do with no condition")]
    [DataRow("Loop Until a", DisplayName = "a Do with its condition after Loop")]
    public void ASyntaxErrorInTheBodyOfADo_KeepsTheDo(string closes)
    {
        var parse = Parse($"Do\r\na = = 1\r\nb = 3\r\n{closes}\r\nc = 4");

        Assert.AreEqual(2, Assert.ContainsSingle(parse.SyntaxErrors, Said(parse)).Location.Range.Start.Line);
        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        var block = member.Children.First();
        Assert.AreEqual(4, block.SourceLocation.Range.End.Line, $"the Do ends at its {closes}");
        Assert.IsTrue(BodyOf(block).OfType<AssignmentStatementNode>().Any(node => node.SourceLocation.Range.Start.Line == 3), "the statement after the error is the body's");
        Assert.IsTrue(member.Children.OfType<AssignmentStatementNode>().Any(node => node.SourceLocation.Range.Start.Line == 5), "the statement after the Do is the procedure's");
    }

    private static IEnumerable<SyntaxNode> BodyOf(SyntaxNode block) => block switch
    {
        ForStatementNode loop => loop.Body.Children,
        ForEachStatementNode loop => loop.Body.Children,
        DoLoopStatementNode loop => loop.Body.Children,
        DoLoopWhileStatementNode loop => loop.Body.Children,
        DoLoopUntilStatementNode loop => loop.Body.Children,
        DoWhileLoopStatementNode loop => loop.Body.Children,
        WhileWendStatementNode loop => loop.Body.Children,
        WithStatementNode with => with.Body.Children,
        IfBlockStatementNode @if => @if.Body.Children,
        _ => throw new AssertFailedException($"{block.GetType().Name} is no block of the test."),
    };

    // a declaration with no name is kept as the whole statement it is written as: the recovery reads what is after the error as the rest of it.
    [TestMethod]
    [DataRow("Dim For As Long")]
    [DataRow("Dim 1 As Long")]
    [DataRow("Dim Mod As Long")]
    public void ADeclarationWithNoName_IsKept_AsTheWholeStatement(string statement)
    {
        var parse = Parse(statement);

        var member = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
        var unbuilt = Assert.ContainsSingle(member.Children.OfType<UnbuiltStatementTriviaNode>());
        Assert.AreEqual(statement, unbuilt.Source);
        Assert.AreEqual(new SourceRange(new SourcePosition(1, 0), new SourcePosition(1, statement.Length)), unbuilt.SourceLocation.Range);
    }

    // a parameter's name is a name, as a variable's is (MS-VBAL 5.3.1.5): a reserved identifier there is a syntax error.
    [TestMethod]
    [DataRow("Public Sub S(ByVal Mod As Long)", DisplayName = "an operator keyword, ByVal")]
    [DataRow("Public Sub S(And)", DisplayName = "an operator keyword alone")]
    [DataRow("Public Sub S(Dim As Long)", DisplayName = "a statement keyword")]
    [DataRow("Public Sub S(ByVal For As Long)", DisplayName = "a statement keyword, ByVal")]
    [DataRow("Public Sub S(ByVal Next As Long)", DisplayName = "the statement keyword Next, ByVal")]
    [DataRow("Public Declare PtrSafe Sub S Lib \"x\" (ByVal Like As Long)", DisplayName = "an operator keyword, in a Declare")]
    public void AReservedIdentifierAsTheNameOfAParameter_IsASyntaxError(string header)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), header.Contains("Declare") ? $"{header}\r\n" : $"{header}\r\nEnd Sub\r\n");

        Assert.IsTrue(parse.SyntaxErrors.Any(error => error.ErrorId == (int)VBCompileErrorId.SyntaxError && error.Location.Range.Start.Line == 0), Said(parse));
    }

    [TestMethod]
    [DataRow("Public Sub S(ByVal Name As String)")]
    [DataRow("Public Sub S(ByVal Text As String, Optional ByVal Value$ = \"\")")]
    [DataRow("Public Sub S(ByRef Items() As Variant, ParamArray Args() As Variant)")]
    [DataRow("Public Sub S([Mod] As Long)")]
    public void AParameterWithAName_IsValid(string header)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"{header}\r\nEnd Sub\r\n");

        Assert.IsEmpty(parse.SyntaxErrors, Said(parse));
    }

    // Static is the declaration of a variable, and a block keyword after it is no block: Static For As Long is a syntax error, and no "For without Next".
    [TestMethod]
    [DataRow("Static For As Long")]
    [DataRow("Static Do As Long")]
    [DataRow("Static With As Long")]
    [DataRow("Static Select As Long")]
    public void AStaticDeclarationOfABlockKeyword_IsASyntaxError_AndNoBlockError(string statement)
    {
        var parse = Parse(statement);

        var error = Assert.ContainsSingle(parse.SyntaxErrors, Said(parse));
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId, Said(parse));
        Assert.AreEqual(new SourcePosition(1, "Static ".Length), error.Location.Range.Start, "the error is at the keyword");
    }

    private static string Said(ModuleParseResult parse)
        => string.Join(" | ", parse.SyntaxErrors.Select(error => $"{error.Location.Range}: {error.Verbose}"));
}
