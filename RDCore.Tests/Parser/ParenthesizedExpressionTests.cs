using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols.Operators;

namespace RDCore.Tests.Parser;

/// <summary>
/// A parenthesized expression (MS-VBAL §5.6.6) is a value expression, and its parentheses are an
/// operator — <c>__c()_op</c>, the unary let-coercion.
/// </summary>
/// <remarks>
/// The parser used to erase them, leaking the enclosed expression up as though it had been written
/// without them: <c>Inc (x)</c> and <c>Inc x</c> built identical trees, so an argument written to be
/// passed by value was aliased to a <c>ByRef</c> parameter and mutated the caller's variable. Same class
/// of defect as the erased <c>New</c> and <c>TypeOf…Is</c> keywords (see
/// <see cref="UnbuiltExpressionTests"/>), and the reason the AST keeps a spelling distinction even where
/// one carries no consequence of its own.
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §5.6.6 Parenthesized Expressions")]
public sealed class ParenthesizedExpressionTests
{
    private static ExpressionNode ValueOf(string expression)
    {
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), $"Sub S()\r\nx = {expression}\r\nEnd Sub\r\n");

        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single()
            .Children.OfType<AssignmentStatementNode>().Single().Value;
    }

    [TestMethod]
    public void AParenthesizedExpression_BuildsTheLetCoerceOperator()
    {
        var value = Assert.IsInstanceOfType<VBUnaryOperatorExpressionNode>(ValueOf("(y)"));

        Assert.AreEqual(OperatorSymbolNames.UnaryLetCoerceOp, value.Token);
        Assert.IsInstanceOfType<SimpleNameExpressionNode>(value.Operand);
    }

    [TestMethod]
    public void NestedParentheses_BuildOneOperatorEach()
    {
        var outer = Assert.IsInstanceOfType<VBUnaryOperatorExpressionNode>(ValueOf("((y))"));
        var inner = Assert.IsInstanceOfType<VBUnaryOperatorExpressionNode>(outer.Operand);

        Assert.AreEqual(OperatorSymbolNames.UnaryLetCoerceOp, inner.Token);
        Assert.IsInstanceOfType<SimpleNameExpressionNode>(inner.Operand);
    }

    [TestMethod]
    public void AnUnparenthesizedExpression_BuildsNoSuchOperator()
        // the control: the operator is the parentheses, not something every expression acquires.
        => Assert.IsInstanceOfType<SimpleNameExpressionNode>(ValueOf("y"));

    [TestMethod]
    public void GroupingParentheses_AreTheSameOperator()
    {
        // MS-VBAL 5.6.6 draws no distinction by position — a parenthesized expression is a value
        // expression wherever it appears, and grouping is what that already means for an operand that
        // was never a variable. So `(a + b) * c` carries the operator too, over the addition.
        var product = Assert.IsInstanceOfType<VBBinaryOperatorExpressionNode>(ValueOf("(a + b) * c"));
        var left = Assert.IsInstanceOfType<VBUnaryOperatorExpressionNode>(product.Left);

        Assert.AreEqual(OperatorSymbolNames.UnaryLetCoerceOp, left.Token);
        Assert.IsInstanceOfType<VBBinaryOperatorExpressionNode>(left.Operand);
    }
}
