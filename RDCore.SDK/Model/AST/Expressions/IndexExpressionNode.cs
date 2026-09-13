using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.6.13</strong> <c>index-expression</c> (<c>callee(arguments)</c>) — represents
/// both a procedure/function call and array/collection indexing; which one it is is a semantic
/// question the resolver answers from the callee's declared kind, not something this node encodes.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
/// <param name="Callee">The expression being called or indexed.</param>
/// <param name="Arguments">
/// The argument list, in source order. Each element is a bare expression for a positional argument,
/// or a <see cref="NamedArgumentNode"/>, <see cref="MissingArgumentNode"/>, or
/// <see cref="AddressOfExpressionNode"/> for the other <strong>MS-VBAL 5.6.13.1</strong> argument
/// forms.
/// </param>
public sealed record class IndexExpressionNode(SyntaxNodeId Identity, SourceLocation Location, ExpressionNode Callee, ImmutableArray<ExpressionNode> Arguments)
    : ExpressionNode(Identity, Location, [Callee, .. Arguments]);

/// <summary>
/// <strong>MS-VBAL 5.6.13.1</strong> a named argument (<c>name:=value</c>) in an
/// <see cref="IndexExpressionNode"/>'s argument list.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
/// <param name="Name">The parameter name.</param>
/// <param name="Value">The argument's value.</param>
public sealed record class NamedArgumentNode(SyntaxNodeId Identity, SourceLocation Location, string Name, ExpressionNode Value)
    : ExpressionNode(Identity, Location, [Value]);

/// <summary>
/// <strong>MS-VBAL 5.6.13.1</strong> a skipped positional argument (e.g. the middle position in
/// <c>Foo(1, , 3)</c>) in an <see cref="IndexExpressionNode"/>'s argument list — a placeholder that
/// preserves the position for binding against the callee's parameter list, not a value-producing
/// expression.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
public sealed record class MissingArgumentNode(SyntaxNodeId Identity, SourceLocation Location)
    : ExpressionNode(Identity, Location, []);

/// <summary>
/// <strong>MS-VBAL 5.6.16.8</strong> an <c>AddressOf</c> expression — legal only as an
/// <see cref="IndexExpressionNode"/> argument, yielding a procedure pointer rather than evaluating a
/// call.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
/// <param name="Target">The procedure or function being referenced.</param>
public sealed record class AddressOfExpressionNode(SyntaxNodeId Identity, SourceLocation Location, ExpressionNode Target)
    : ExpressionNode(Identity, Location, [Target]);
