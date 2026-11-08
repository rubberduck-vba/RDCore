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
    /// Where in the document the operator token is written: the <c>Not</c> of <c>Not a</c>, where <see cref="ExpressionNode.Location"/> is the whole operation.
    /// </summary>
    /// <remarks>
    /// A diagnostic about the operator is located here. A run-time error of the operation is located at the operation itself. <see langword="null"/> for
    /// parentheses, which are two tokens, for an operation the code does not write, and for a node created without one.
    /// </remarks>
    public SourceLocation? OperatorLocation { get; init; }

    /// <summary>
    /// The operand: <c>Children[0]</c>.
    /// </summary>
    public ExpressionNode Operand => (ExpressionNode)Children[0];
}
