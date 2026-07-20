using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// A <c>BoundNode</c> that can be statically evaluated to a <c>VBType</c>, and with runtime semantics to a <c>VBTypedValue</c>.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public abstract record class BoundExpression(Uri SemanticId, SourceLocation Location) : BoundNode(SemanticId, Location), IExecutableNode
{
    /// <summary>
    /// The <c>string</c> <em>token</em> of the executable node; expressions have no statement token.
    /// </summary>
    public string Token => string.Empty;
    /// <summary>
    /// The <em>inputs</em> of the executable node; expressions have no inputs.
    /// </summary>
    public ImmutableArray<BoundExpression> Inputs => [];
}
