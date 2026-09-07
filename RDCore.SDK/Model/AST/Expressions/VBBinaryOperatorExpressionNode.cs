using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using System.Collections.Immutable;
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
    /// <param name="Identity">A unique identifier for this specific syntax node.</param>
    /// <param name="Symbol">The <c>OperatorSymbol</c> associated with this <em>operator expression</em>.</param>
    /// <param name="ResultSymbol">The <see cref="OperatorExpressionValueSymbol"/> associated with the <em>result</em> of this <em>operator expression</em>.</param>
    /// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
    /// <param name="Left">The left-hand side (LHS) operand of this <em>binary operator expression</em></param>
    /// <param name="Right">The right-hand side (RHS) operand of this <em>binary operator expression</em></param>
    public VBBinaryOperatorExpressionNode(string token, SyntaxNodeId identity, SourceLocation location, ImmutableArray<SyntaxNode> children)
        : base(identity, location, children)
    {
        Token = token;
        Left = (ExpressionNode)children[0];
        Right = (ExpressionNode)children[1];
    }

    public string Token { get; }

    /// <summary>The left operand — <c>Children[0]</c>, reconstructed from <c>Children</c> on deserialization.</summary>
    [JsonIgnore]
    public ExpressionNode Left { get; }

    /// <summary>The right operand — <c>Children[1]</c>, reconstructed from <c>Children</c> on deserialization.</summary>
    [JsonIgnore]
    public ExpressionNode Right { get; }
}
