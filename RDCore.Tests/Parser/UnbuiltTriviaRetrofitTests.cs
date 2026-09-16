using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;

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

    [TestMethod]
    public void TruncatedBinaryOperator_WrapsTheSurvivingOperandInTrivia()
    {
        // pre-retrofit, this left a bare LiteralExpressionNode(1) sitting where the assignment's own
        // capture would silently adopt it as if `a = 1` were the complete, correct statement.
        var result = Parse("Sub S()\r\na = 1 +\r\nEnd Sub");

        Assert.IsFalse(result.IsSuccess);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().Single();
        var trivia = Assert.IsInstanceOfType<UnbuiltExpressionTriviaNode>(assignment.Value);
        StringAssert.Contains(trivia.Source, "1");
        var survivingOperand = Assert.IsInstanceOfType<LiteralExpressionNode>(trivia.Inputs.Single());
        Assert.AreEqual((short)1, ((RDCore.SDK.Model.Values.Intrinsic.VBIntegerValue)survivingOperand.StaticValue).Value);
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
