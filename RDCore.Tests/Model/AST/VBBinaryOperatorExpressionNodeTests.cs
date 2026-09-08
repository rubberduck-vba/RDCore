using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.Tests.Model.AST;

[TestClass]
public sealed class VBBinaryOperatorExpressionNodeTests
{
    private static readonly SourceLocation Location = new(new Uri("file:///m.bas"), SourceRange.Empty);

    private static SyntaxNodeId Id() => new("m", []);

    private static ExpressionNode Literal(short value) => new LiteralExpressionNode(Id(), Location, new VBIntegerValue(value));

    private static VBBinaryOperatorExpressionNode Add(ExpressionNode left, ExpressionNode right)
        => new("+", Id(), Location, [left, right]);

    [TestMethod]
    public void LeftAndRight_AreTheOperandChildren()
    {
        var left = Literal(1);
        var right = Literal(2);

        var node = Add(left, right);

        Assert.AreSame(left, node.Left);
        Assert.AreSame(right, node.Right);
        Assert.AreSame(node.Children[0], node.Left);
        Assert.AreSame(node.Children[1], node.Right);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    public void Ctor_RejectsAWrongOperandCount(int count)
    {
        var children = Enumerable.Range(0, count).Select(i => (SyntaxNode)Literal((short)i)).ToImmutableArray();

        Assert.ThrowsExactly<ArgumentException>(() => _ = new VBBinaryOperatorExpressionNode("+", Id(), Location, children));
    }

    [TestMethod]
    public void ToString_OnADeepOperatorChain_StaysBounded()
    {
        // Left/Right are views onto Children; if the record printed all three, this 40-deep chain
        // would expand 2^40 times before returning. PrintMembers keeps it linear.
        ExpressionNode node = Literal(0);
        for (var i = 0; i < 40; i++)
        {
            node = Add(node, Literal(1));
        }

        var text = node.ToString();

        Assert.IsNotNull(text);
        Assert.IsLessThan(50_000, text!.Length);
        StringAssert.Contains(text, "Token = +");
    }
}
