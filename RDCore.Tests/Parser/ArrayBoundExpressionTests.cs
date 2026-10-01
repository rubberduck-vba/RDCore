using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.Tests.Parser;

/// <summary>
/// <c>LBound</c> and <c>UBound</c> (<strong>MS-VBAL §3.3.5.2</strong>) are keywords the grammar lets through as
/// identifiers; the parse listener recognizes the keyword and builds a node of its own instead of the call on a name
/// that the grammar says it is — the way the <c>TypeOf…Is</c> keyword pair is recognized.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.3.5.2 Special forms")]
public sealed class ArrayBoundExpressionTests
{
    private static ExpressionNode ValueOf(string expression)
    {
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), $"Sub S()\r\nx = {expression}\r\nEnd Sub\r\n");

        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single()
            .Children.OfType<AssignmentStatementNode>().Single().Value;
    }

    [TestMethod]
    [DataRow("LBound(a)", ArrayBoundKind.Lower)]
    [DataRow("UBound(a)", ArrayBoundKind.Upper)]
    [DataRow("ubound(a)", ArrayBoundKind.Upper, DisplayName = "keywords are case-insensitive")]
    public void TheKeyword_BuildsAnArrayBoundNode(string expression, ArrayBoundKind kind)
    {
        var node = Assert.IsInstanceOfType<ArrayBoundExpressionNode>(ValueOf(expression));

        Assert.AreEqual(kind, node.Kind);
        Assert.AreEqual("a", Assert.IsInstanceOfType<SimpleNameExpressionNode>(node.Array).IdentifierName);
        Assert.IsNull(node.Dimension);
    }

    [TestMethod]
    public void TheDimension_IsTheSecondOperand()
    {
        var node = Assert.IsInstanceOfType<ArrayBoundExpressionNode>(ValueOf("UBound(grid, 2)"));

        Assert.AreEqual("grid", Assert.IsInstanceOfType<SimpleNameExpressionNode>(node.Array).IdentifierName);
        Assert.IsInstanceOfType<LiteralExpressionNode>(node.Dimension);
        CollectionAssert.AreEqual(new ExpressionNode[] { node.Array, node.Dimension! }, node.Inputs.ToArray());
    }

    [TestMethod]
    public void TheOperands_AreAnyExpressions()
    {
        var node = Assert.IsInstanceOfType<ArrayBoundExpressionNode>(ValueOf("LBound(Holder.Items, n + 1)"));

        Assert.IsInstanceOfType<MemberAccessExpressionNode>(node.Array);
        Assert.IsInstanceOfType<VBBinaryOperatorExpressionNode>(node.Dimension);
    }

    [TestMethod]
    public void ABoundIsAValueInsideAnotherExpression()
    {
        var sum = Assert.IsInstanceOfType<VBBinaryOperatorExpressionNode>(ValueOf("UBound(a) - LBound(a) + 1"));

        Assert.HasCount(2, Descendants(sum).OfType<ArrayBoundExpressionNode>().ToList(), "both bounds are nodes of their own");
        Assert.IsEmpty(Descendants(sum).OfType<IndexExpressionNode>());
    }

    [TestMethod]
    [DataRow("UBound()", DisplayName = "no operand")]
    [DataRow("UBound(a, 1, 2)", DisplayName = "three operands")]
    [DataRow("UBound(a, Dimension:=1)", DisplayName = "a named dimension")]
    [DataRow("UBound(a, , 1)", DisplayName = "an omitted operand")]
    public void AnyOtherShape_IsNotThisConstruct(string expression)
    {
        // left as the call on a name the grammar says it is, for the rules about calls to say what is wrong with it.
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), $"Sub S()\r\nx = {expression}\r\nEnd Sub\r\n");
        var value = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single()
            .Children.OfType<AssignmentStatementNode>().SingleOrDefault()?.Value;

        Assert.IsNotInstanceOfType<ArrayBoundExpressionNode>(value);
    }

    [TestMethod]
    [DataRow("[UBound](a)", DisplayName = "an escaped name")]
    [DataRow("Holder.UBound(a)", DisplayName = "a member called that")]
    public void ANameThatIsNotTheKeyword_StaysACall(string expression)
    {
        var value = ValueOf(expression);

        Assert.IsInstanceOfType<IndexExpressionNode>(value);
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node)
        => node.Children.SelectMany(child => Descendants(child).Prepend(child));
}
