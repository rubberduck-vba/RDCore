using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Precompiler;

namespace RDCore.Tests.Semantics.Precompiler;

/// <summary>
/// <see cref="PrecompilerConstantExpressionEvaluator"/> — MS-VBAL §5.6.16.2: folds a conditional-
/// compilation expression to its constant value, resolving named constants without a runtime session.
/// </summary>
[TestClass]
public sealed class PrecompilerConstantExpressionEvaluatorTests
{
    private static readonly SyntaxNodeId NodeId = new("file://rdcore-test", [1]);
    private static readonly SourceLocation Location = new(new Uri("file://rdcore-test"), SourceRange.Empty);

    private static ISymbolResolver Resolver(params PrecompilerConstantSymbol[] constants)
        => new ScopeTreeSymbolResolver(ScopeTreeBuilder.Build([.. constants]));

    private static ExpressionNode Literal(RDCore.SDK.Model.Values.Abstract.VBTypedValue value) => new LiteralExpressionNode(NodeId, Location, value);
    private static ExpressionNode Name(string name) => new PrecompilerNameExpressionNode(NodeId, Location, name);
    private static ExpressionNode Binary(string token, ExpressionNode left, ExpressionNode right) => new VBBinaryOperatorExpressionNode(token, NodeId, Location, left, right);
    private static ExpressionNode Unary(string token, ExpressionNode operand) => new VBUnaryOperatorExpressionNode(token, NodeId, Location, [operand]);

    [TestMethod]
    public void Literal_EvaluatesToItself()
    {
        Assert.IsTrue(PrecompilerConstantExpressionEvaluator.TryEvaluateBoolean(Resolver(), Literal(VBBooleanValue.True), out var value));
        Assert.IsTrue(value);
    }

    [TestMethod]
    public void DefinedConstant_ResolvesToItsValue()
    {
        var resolver = Resolver(new PrecompilerConstantSymbol("DEBUGMODE", new VBIntegerValue(1)));

        Assert.IsTrue(PrecompilerConstantExpressionEvaluator.TryEvaluateBoolean(resolver, Name("DEBUGMODE"), out var value));
        Assert.IsTrue(value); // non-zero coerces True
    }

    [TestMethod]
    public void AnUndefinedConstant_IsIndeterminate()
        => Assert.IsFalse(PrecompilerConstantExpressionEvaluator.TryEvaluateBoolean(Resolver(), Name("Nowhere"), out _));

    [TestMethod]
    public void EqualityComparison_OfTwoConstants()
    {
        var resolver = Resolver(new PrecompilerConstantSymbol("A", new VBIntegerValue(1)));
        var expression = Binary(RDCore.SDK.Model.Tokens.CompareEqualOp, Name("A"), Literal(new VBIntegerValue(1)));

        Assert.IsTrue(PrecompilerConstantExpressionEvaluator.TryEvaluateBoolean(resolver, expression, out var value));
        Assert.IsTrue(value);
    }

    [TestMethod]
    public void StringEquality_ComparesTheStringValues()
    {
        var expression = Binary(RDCore.SDK.Model.Tokens.CompareEqualOp, Literal(new VBStringValue("x")), Literal(new VBStringValue("y")));

        Assert.IsTrue(PrecompilerConstantExpressionEvaluator.TryEvaluateBoolean(Resolver(), expression, out var value));
        Assert.IsFalse(value);
    }

    [TestMethod]
    public void LogicalAnd_OfTwoConstants()
    {
        var resolver = Resolver(
            new PrecompilerConstantSymbol("Win64", VBBooleanValue.True),
            new PrecompilerConstantSymbol("VBA7", VBBooleanValue.True));
        var expression = Binary(RDCore.SDK.Model.Tokens.LogicalAndOp, Name("Win64"), Name("VBA7"));

        Assert.IsTrue(PrecompilerConstantExpressionEvaluator.TryEvaluateBoolean(resolver, expression, out var value));
        Assert.IsTrue(value);
    }

    [TestMethod]
    public void Not_NegatesItsOperand()
    {
        var expression = Unary(RDCore.SDK.Model.Tokens.LogicalNotOp, Literal(VBBooleanValue.False));

        Assert.IsTrue(PrecompilerConstantExpressionEvaluator.TryEvaluateBoolean(Resolver(), expression, out var value));
        Assert.IsTrue(value);
    }

    [TestMethod]
    public void Arithmetic_FeedsIntoAComparison()
        // #If A + 1 = 2 Then
    {
        var resolver = Resolver(new PrecompilerConstantSymbol("A", new VBIntegerValue(1)));
        var sum = Binary(RDCore.SDK.Model.Tokens.AdditionOp, Name("A"), Literal(new VBIntegerValue(1)));
        var expression = Binary(RDCore.SDK.Model.Tokens.CompareEqualOp, sum, Literal(new VBIntegerValue(2)));

        Assert.IsTrue(PrecompilerConstantExpressionEvaluator.TryEvaluateBoolean(resolver, expression, out var value));
        Assert.IsTrue(value);
    }

    [TestMethod]
    public void AnIndeterminateOperand_MakesTheWholeExpressionIndeterminate()
    {
        var expression = Binary(RDCore.SDK.Model.Tokens.LogicalAndOp, Name("Nowhere"), Literal(VBBooleanValue.True));

        Assert.IsFalse(PrecompilerConstantExpressionEvaluator.TryEvaluateBoolean(Resolver(), expression, out _));
    }

    [TestMethod]
    public void AnUnsupportedOperator_IsIndeterminate_NotMisevaluated()
        // string concatenation (&) is deliberately not implemented - see the type's own xmldoc.
    {
        var expression = Binary(RDCore.SDK.Model.Tokens.ConcatOp, Literal(new VBStringValue("a")), Literal(new VBStringValue("b")));

        Assert.IsFalse(PrecompilerConstantExpressionEvaluator.TryEvaluateBoolean(Resolver(), expression, out _));
    }
}
