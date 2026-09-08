using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;
namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// An <em>infix</em> <c>VBOperatorExpression</c> (bound) with a <see cref="Left"/> and a
/// <see cref="Right"/> operand — convenience views onto <c>Children[0]</c> and <c>Children[1]</c>.
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

    /// <summary>The left operand — <c>Children[0]</c>.</summary>
    [JsonIgnore]
    public ExpressionNode Left => (ExpressionNode)Children[0];

    /// <summary>The right operand — <c>Children[1]</c>.</summary>
    [JsonIgnore]
    public ExpressionNode Right => (ExpressionNode)Children[1];

    /// <summary>
    /// Replaces the compiler-generated record printer. <see cref="Left"/> and <see cref="Right"/> are
    /// the same nodes the base already prints under <c>Children</c>; letting the record print all
    /// three expands a nested operator tree (<c>a + b + c + …</c>) exponentially. Only the operator
    /// token is added on top of the base members.
    /// </summary>
    protected override bool PrintMembers(StringBuilder builder)
    {
        base.PrintMembers(builder);
        builder.Append(", ").Append(nameof(Token)).Append(" = ").Append(Token);
        return true;
    }
}
