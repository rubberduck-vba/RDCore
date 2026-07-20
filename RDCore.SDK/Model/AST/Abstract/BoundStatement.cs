using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// A <c>BoundNode</c> representing an <em>executable statement</em>.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Token">The <c>string</c> <em>token</em> of the statement, e.g. <c>Open</c>, <c>Input</c>, <c>Print</c>, <c>Assert</c>, etc..</param>
/// <param name="Inputs">The <em>inputs</em> of the executable statement; expressions evaluated immediately before the call.</param>
public abstract record class BoundStatement(Uri SemanticId, SourceLocation Location, string Token, ImmutableArray<BoundExpression> Inputs)
    : BoundNode(SemanticId, Location), IExecutableNode;
