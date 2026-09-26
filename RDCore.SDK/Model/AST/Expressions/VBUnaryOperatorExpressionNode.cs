using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// A <em>prefix</em> <c>VBOperatorExpression</c> that accepts a single operand.
/// </summary>
/// <remarks>
/// Unless specified otherwise in a derived node type, <strong>MS-VBAL 5.6.9 Operator Expressions</strong> defines the static and run-time semantics of this node.
/// </remarks>
/// <param name="Token">The operator token associated with this <em>operator expression</em>.</param>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
public record class VBUnaryOperatorExpressionNode(string Token, SyntaxNodeId Identity, SourceLocation Location, ImmutableArray<SyntaxNode> Children)
    : VBOperatorExpression(Identity, Location, Children)
{
    /// <summary>
    /// Deserialization path: <c>Operand</c> is what the JSON actually carries now (see
    /// <c>SyntaxNodeJson</c>) — <c>Children</c> is redundant with it and gets omitted on write, so it
    /// can't be relied on to arrive from the wire.
    /// </summary>
    [JsonConstructor]
    public VBUnaryOperatorExpressionNode(string Token, SyntaxNodeId Identity, SourceLocation Location, ExpressionNode Operand)
        : this(Token, Identity, Location, (ImmutableArray<SyntaxNode>)[Operand]) { }

    /// <summary>
    /// The operand — <c>Children[0]</c>.
    /// </summary>
    public ExpressionNode Operand => (ExpressionNode)Children[0];
}
