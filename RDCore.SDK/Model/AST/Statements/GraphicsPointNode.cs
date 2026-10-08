using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// A coordinate pair of a <see cref="GraphicsMethodStatementNode"/>: <c>(x, y)</c>, or <c>Step (x, y)</c> when the pair is an offset from the last point drawn.
/// </summary>
/// <remarks>
/// Not an expression: the parenthesis is no grouping, and <c>(1, 2)</c> has no value to evaluate. It is a node of its own so that the <c>Step</c> keyword,
/// which is written before the parenthesis and is not part of either coordinate, has somewhere to be.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the parenthesized pair. The <c>Step</c> keyword before it, when there is one, is in the statement's range and in <paramref name="IsRelative"/>.</param>
/// <param name="IsRelative">Whether the pair is written with <c>Step</c>, which makes it relative to the current position rather than absolute.</param>
/// <param name="X">The horizontal coordinate.</param>
/// <param name="Y">The vertical coordinate.</param>
public record class GraphicsPointNode(SyntaxNodeId Identity, SourceLocation SourceLocation, bool IsRelative, ExpressionNode X, ExpressionNode Y)
    : SyntaxNode(Identity, SourceLocation, [X, Y]);
