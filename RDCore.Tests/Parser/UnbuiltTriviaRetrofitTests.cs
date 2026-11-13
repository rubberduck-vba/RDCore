using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// Author correction, mid-round: "build nothing" was itself the wrong fallback for every guard added
/// this round in response to the adversarial review — truncated operators/arguments/Print clauses
/// (<c>fix/pop-last-children-guard</c>), the null-deref guards (<c>fix/unguarded-null-derefs</c>), and
/// <c>ExitOnErrorStmt</c>'s own fabrication fix all left recovered-but-incomplete input building
/// nothing, silently losing source text a formatter/case-correction pass would need to reconstruct it.
/// This retrofits those guards to build an <see cref="UnbuiltExpressionTriviaNode"/> or
/// <see cref="UnbuiltStatementTriviaNode"/> instead, per <see cref="UnbuiltExpressionTriviaNode"/>'s own
/// remarks. See <c>UnbuiltExpressionTests.cs</c> for the New/TypeOf...Is case that established the
/// pattern.
/// </summary>
[TestClass]
public sealed class UnbuiltTriviaRetrofitTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();

    private static ModuleParseResult Parse(string source)
        => new ModuleParser().Parse(Uri, source);

    // a binary operator at the end of its statement, with no right operand, is a syntax error, and the operation is kept unbuilt with its left operand and the
    // operator: never the left operand alone, which the assignment would take as if `a = 1` were all the statement said. The parse reads some operations
    // (`1 +`) and leaves the operator of others unread, where it could also begin something that follows an expression (`1 -`, `1 Mod`): either way, the
    // statement keeps all it said.
    [TestMethod]
    [DataRow("+")]
    [DataRow("-")]
    [DataRow("*")]
    [DataRow("/")]
    [DataRow("\\")]
    [DataRow("Mod")]
    [DataRow("^")]
    [DataRow("&")]
    [DataRow("=")]
    [DataRow("<>")]
    [DataRow("<")]
    [DataRow(">")]
    [DataRow("<=")]
    [DataRow(">=")]
    [DataRow("Like")]
    [DataRow("Is")]
    [DataRow("And")]
    [DataRow("Or")]
    [DataRow("Xor")]
    [DataRow("Eqv")]
    [DataRow("Imp")]
    public void TruncatedBinaryOperator_WrapsTheSurvivingOperandInTrivia(string @operator)
        => AssertTheOperationIsKept($"a = 1 {@operator}", $"1 {@operator}");

    // whatever ends the statement after the operator, and wherever the expression is.
    [TestMethod]
    [DataRow("a = 1 - ' a comment", "1 -", DisplayName = "a comment after it")]
    [DataRow("a = 1 Mod: b = 2", "1 Mod", DisplayName = "a colon after it")]
    [DataRow("a = 1 _\r\n    -", "1 _\r\n    -", DisplayName = "a line continuation before it")]
    [DataRow("a = 1 - _\r\n", "1 -", DisplayName = "a line continuation after it")]
    [DataRow("a = (1 + 2) And", "(1 + 2) And", DisplayName = "after a parenthesized operation")]
    public void TruncatedBinaryOperator_WrapsTheSurvivingOperandInTrivia_WhereverItIs(string statement, string kept)
        => AssertTheOperationIsKept(statement, kept);

    [TestMethod]
    public void TruncatedBinaryOperator_InTheArgumentOfACall_WrapsTheArgumentInTrivia()
    {
        var result = Parse("Sub S()\r\nFoo 1 -\r\nEnd Sub");

        Assert.IsFalse(result.IsSuccess);
        var call = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.OfType<CallStatementNode>().Single();
        var trivia = Assert.IsInstanceOfType<UnbuiltExpressionTriviaNode>(call.Children.Last());
        Assert.AreEqual("1 -", trivia.Source);
    }

    // an operator keyword that is the name of a member is no operator, and leaves the code it is in as it is.
    [TestMethod]
    [DataRow("a = x.And")]
    [DataRow("Debug.Print x; d!Mod")]
    public void AnOperatorKeyword_ThatIsAName_IsNoTruncatedOperation(string statement)
    {
        var result = Parse($"Sub S()\r\n{statement}\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        Assert.IsFalse(result.SyntaxTree!.Descendants().OfType<UnbuiltExpressionTriviaNode>().Any());
    }

    // a statement no alternative reads - an operator with no space before it, or a word that begins a block and no statement of it - is kept as its text,
    // where it is: the statement never vanishes from the member it is in. An If whose condition has no right operand is the If it is (BlockHeaderRecoveryTests).
    [TestMethod]
    [DataRow("a = 1+")]
    [DataRow("a = 1-")]
    [DataRow("a = 1*")]
    [DataRow("a = 1/")]
    [DataRow("a = 1\\")]
    [DataRow("a = 1=")]
    [DataRow("a = 1<>")]
    [DataRow("a = 1<")]
    [DataRow("a = 1>")]
    [DataRow("a = 1<=")]
    [DataRow("a = 1>=")]
    [DataRow("a = 1Mod")]
    [DataRow("For = 1")]
    public void AStatementNoRuleReads_IsKeptAsTheStatementItSays(string statement)
    {
        var result = Parse($"Sub S()\r\n{statement}\r\nEnd Sub");

        Assert.IsFalse(result.IsSuccess);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var trivia = Assert.IsInstanceOfType<UnbuiltStatementTriviaNode>(Assert.ContainsSingle(member.Children.OfType<StatementNode>()));
        Assert.AreEqual(statement, trivia.Source);
        Assert.AreEqual(new SourcePosition(1, 0), trivia.SourceLocation.Range.Start);
    }

    // a statement a rule reads, even in part, is the node that rule builds, and only that one.
    [TestMethod]
    [DataRow("a = 1 +", typeof(AssignmentStatementNode), DisplayName = "an operator the parse reads")]
    [DataRow("a = 1 -", typeof(AssignmentStatementNode), DisplayName = "an operator the parse leaves unread")]
    [DataRow("Foo (1, 2)-(3, 4)", typeof(UnbuiltStatementTriviaNode), DisplayName = "the coordinates of a statement that is not Line")]
    public void AStatementARuleReads_IsOneStatement(string statement, Type node)
    {
        var result = Parse($"Sub S()\r\n{statement}\r\nEnd Sub");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.IsInstanceOfType(Assert.ContainsSingle(member.Children.OfType<StatementNode>()), node);
    }

    // wherever the statement is: in the body of a block, or after a label.
    [TestMethod]
    public void AStatementNoRuleReads_InTheBodyOfABlock_IsKeptInThatBody()
    {
        var result = Parse("Sub S()\r\nIf c Then\r\na = 1-\r\nEnd If\r\nEnd Sub");

        Assert.IsFalse(result.IsSuccess);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var block = Assert.ContainsSingle(member.Children.OfType<StatementNode>());
        var trivia = Assert.IsInstanceOfType<UnbuiltStatementTriviaNode>(Assert.ContainsSingle(block.Descendants().OfType<UnbuiltStatementTriviaNode>()));
        Assert.AreEqual("a = 1-", trivia.Source);
    }

    [TestMethod]
    public void AStatementNoRuleReads_AfterALabel_IsKeptAfterTheLabel()
    {
        var result = Parse("Sub S()\r\nHere: a = 1-\r\nEnd Sub");

        Assert.IsFalse(result.IsSuccess);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var trivia = Assert.ContainsSingle(member.Descendants().OfType<UnbuiltStatementTriviaNode>());
        Assert.AreEqual("a = 1-", trivia.Source);
    }

    private static void AssertTheOperationIsKept(string statement, string kept)
    {
        var result = Parse($"Sub S()\r\n{statement}\r\nEnd Sub");

        Assert.IsFalse(result.IsSuccess);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().First();
        var trivia = Assert.IsInstanceOfType<UnbuiltExpressionTriviaNode>(assignment.Value);
        Assert.AreEqual(kept.ReplaceLineEndings("\n"), trivia.Source.ReplaceLineEndings("\n"));
        var survivingOperand = Assert.ContainsSingle(trivia.Inputs);
        Assert.IsTrue(survivingOperand is LiteralExpressionNode or VBUnaryOperatorExpressionNode, survivingOperand.GetType().Name);
    }

    [TestMethod]
    public void NamedArgumentWithNoValue_WrapsInTrivia_AsAnArgumentOfTheCall()
    {
        var result = Parse("Sub S()\r\nFoo x:=\r\nEnd Sub");

        Assert.IsFalse(result.IsSuccess);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var call = member.Children.OfType<CallStatementNode>().Single();
        var trivia = Assert.IsInstanceOfType<UnbuiltExpressionTriviaNode>(call.Arguments.Single());
        StringAssert.Contains(trivia.Source, "x:=");
    }

    [TestMethod]
    public void BareRaiseEvent_WithNoName_BuildsUnbuiltStatementTrivia()
    {
        // same double-invocation quirk as ExitOnErrorStmt: ANTLR calls Exit once with
        // context.exception set, then again with a zero-width synthesized identifier() match.
        var result = Parse("Sub S()\r\nRaiseEvent\r\nEnd Sub");

        Assert.IsFalse(result.IsSuccess);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var trivia = Assert.IsInstanceOfType<UnbuiltStatementTriviaNode>(member.Children.Single());
        StringAssert.Contains(trivia.Source, "RaiseEvent");
    }

    [TestMethod]
    public void RaiseEventWithName_StillBuildsNormally()
    {
        var result = Parse("Sub S()\r\nRaiseEvent Foo\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var keyword = Assert.IsInstanceOfType<KeywordStatementNode>(member.Children.Single());
        Assert.AreEqual(Tokens.RaiseEvent, keyword.Token);
    }
}
