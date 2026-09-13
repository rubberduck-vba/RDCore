using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.6.11</strong> <c>instance-expression</c> — the <c>Me</c> keyword, a self-reference
/// to the current object instance. Its own grammar alternative, distinct from
/// <see cref="SimpleNameExpressionNode"/>, because <c>Me</c> is a reserved keyword and never resolves
/// like a named symbol lookup.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
public sealed record class InstanceExpressionNode(SyntaxNodeId Identity, SourceLocation Location)
    : ExpressionNode(Identity, Location, []);
