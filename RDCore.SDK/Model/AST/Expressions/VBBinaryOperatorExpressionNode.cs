using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;
namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// An <em>infix</em> <c>VBOperatorExpression</c> (bound) that accepts a <em>left</em> and a <em>right</em> operand on either of its sides.
/// </summary>
/// <remarks>
/// Unless specified otherwise in a derived node type, <strong>MS-VBAL 5.6.9 Operator Expressions</strong> defines the static and run-time semantics of this node.
/// </remarks>
public record class VBBinaryOperatorExpressionNode : VBOperatorExpression
{
    /// <param name="token">The operator token (e.g. <c>+</c>, <c>And</c>).</param>
    /// <param name="identity">A unique identifier for this specific syntax node.</param>
    /// <param name="location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
    /// <param name="children">The left and right operands, in that order.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="children"/> is not exactly two <see cref="ExpressionNode"/> operands — the
    /// grammar guarantees a binary operator has two, so this signals a parse-listener desync.
    /// </exception>
    public VBBinaryOperatorExpressionNode(string token, SyntaxNodeId identity, SourceLocation location, ImmutableArray<SyntaxNode> children)
        : base(identity, location, children)
    {
        if (children is not [ExpressionNode, ExpressionNode])
        {
            throw new ArgumentException(
                $"a binary operator expression requires two operand children; got {children.Length}.", nameof(children));
        }
        Token = token;
    }

    public string Token { get; }

    /// <summary>
    /// The left-hand side operand — <c>Children[0]</c>.
    /// </summary>
    [JsonIgnore]
    public ExpressionNode Left => (ExpressionNode)Children[0];

    /// <summary>
    /// The right-hand side operand — <c>Children[1]</c>.
    /// </summary>
    [JsonIgnore]
    public ExpressionNode Right => (ExpressionNode)Children[1];

    // Left and Right are views onto Children, which the base printer already emits; printing all
    // three recurses exponentially on a nested operator tree. Add only the operator token.
    protected override bool PrintMembers(StringBuilder builder)
    {
        base.PrintMembers(builder);
        builder.Append(", Token = ").Append(Token);
        return true;
    }
}
