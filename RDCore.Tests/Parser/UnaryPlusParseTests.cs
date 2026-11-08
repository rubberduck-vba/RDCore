using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Parser;

/// <summary>
/// RD-VBAL §5.6.9.3: a unary <c>+</c> is read, and is no operator. The VBE erases it from the code: the tree is the tree of the code without it, and the
/// <c>+</c> is the module's trivia, where it is written.
/// </summary>
[TestClass]
public sealed class UnaryPlusParseTests
{
    private static ModuleParseResult Parse(string statement)
        => new ModuleParser().Parse(TestUri.TestModuleUri(), $"Sub S()\r\n{statement}\r\nEnd Sub\r\n");

    private static MemberDeclarationNode MemberOf(ModuleParseResult result)
    {
        Assert.IsTrue(result.IsSuccess, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        return result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
    }

    // wherever it is written, and however many there are, the + builds no node, and each is kept once.
    [TestMethod]
    [DataRow("x = +1", 1, DisplayName = "the value of an assignment")]
    [DataRow("x = + 1", 1, DisplayName = "a space after it")]
    [DataRow("x = ++1", 2, DisplayName = "twice")]
    [DataRow("x = -+1", 1, DisplayName = "under a negation")]
    [DataRow("x = +-1", 1, DisplayName = "over a negation")]
    [DataRow("x = 2 * +3", 1, DisplayName = "an operand of an operator")]
    [DataRow("x = +\"1\" & \"2\"", 1, DisplayName = "an operand of a concatenation")]
    [DataRow("x = Abs(+1)", 1, DisplayName = "an argument of a function")]
    [DataRow("Foo +x", 1, DisplayName = "the argument of a call statement")]
    [DataRow("Set o = +obj", 1, DisplayName = "the value of a Set")]
    [DataRow("Const C = +1", 1, DisplayName = "the value of a constant")]
    [DataRow("If +x Then Beep", 1, DisplayName = "a condition")]
    [DataRow("Print #1, +1", 1, DisplayName = "an item of a Print")]
    [DataRow("x = 2 ^ +3 ^ 2", 1, DisplayName = "an operand of a chain of ^")]
    [DataRow("x = +TypeOf o Is Collection", 1, DisplayName = "before TypeOf")]
    public void AUnaryPlus_IsNoNode_AndIsTheModulesTrivia(string statement, int signs)
    {
        var result = Parse(statement);
        var member = MemberOf(result);

        Assert.IsFalse(member.Descendants().OfType<VBUnaryOperatorExpressionNode>().Any(unary => unary.Token == Tokens.AdditionOp));
        Assert.HasCount(signs, result.Trivia.OfType<ErasedPlusTriviaNode>());
    }

    // the tree of `x = + 1` is the tree of `x = 1`: the literal, where it is written; the + is trivia, where it is written.
    [TestMethod]
    public void AUnaryPlus_IsKeptWhereItIsWritten_AndItsOperandIsWhereTheOperandIs()
    {
        var result = Parse("x = + 1");
        var assignment = MemberOf(result).Children.OfType<AssignmentStatementNode>().Single();

        var one = Assert.IsInstanceOfType<LiteralExpressionNode>(assignment.Value);
        Assert.AreEqual(new SourcePosition(1, 6), one.SourceLocation.Range.Start);
        var plus = Assert.ContainsSingle(result.Trivia.OfType<ErasedPlusTriviaNode>());
        Assert.AreEqual(new SourceRange(new SourcePosition(1, 4), new SourcePosition(1, 5)), plus.SourceLocation.Range);
    }

    // a negation over the + is a negative literal as `-1` is: the + is no operand of it.
    [TestMethod]
    public void ANegationOverAUnaryPlus_IsANegativeLiteral()
    {
        var assignment = MemberOf(Parse("x = -+1")).Children.OfType<AssignmentStatementNode>().Single();

        var literal = Assert.IsInstanceOfType<LiteralExpressionNode>(assignment.Value);
        Assert.AreEqual((short)-1, Assert.IsInstanceOfType<VBIntegerValue>(literal.StaticValue).Value);
    }

    // the argument is what the VBE keeps of it: `Foo (x)`, an argument in parentheses, which let-coerces what it encloses, and passes no reference.
    [TestMethod]
    public void AUnaryPlusBeforeAnArgumentInParentheses_LeavesTheParenthesesTheArgumentsOwn()
    {
        var call = MemberOf(Parse("Foo +(x)")).Children.OfType<CallStatementNode>().Single();

        var parentheses = Assert.IsInstanceOfType<VBUnaryOperatorExpressionNode>(call.Arguments.Single());
        Assert.AreEqual(OperatorSymbolNames.UnaryLetCoerceOp, parentheses.Token);
    }
}
