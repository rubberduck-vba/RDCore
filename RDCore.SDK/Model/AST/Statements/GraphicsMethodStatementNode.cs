using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// <strong>RD-VBAL</strong> the graphics methods that are written as statements, with a syntax of their own: <c>Circle</c>, <c>Line</c>, <c>PSet</c> and <c>Scale</c>.
/// </summary>
/// <remarks>
/// MS-VBAL reserves <c>Circle</c> and <c>Scale</c> as special forms (§3.3.5.2) and <c>PSet</c> as a reserved name, and gives none of them a syntax: they are the
/// methods of the objects that can be drawn on (a form, a picture box, a printer), and what a method of such an object takes is not a list of arguments but
/// coordinate pairs, <c>Step</c> and <c>-</c>:
/// <code>
/// [object.]Circle [Step] (x, y), radius [, color [, start [, end [, aspect]]]]
/// [object.]Line [[Step] (x1, y1)] - [Step] (x2, y2) [, [color] [, B | BF]]
/// [object.]PSet [Step] (x, y) [, color]
/// [object.]Scale [(x1, y1) - (x2, y2)]
/// </code>
/// <para>
/// Every part of that is kept where the source wrote it. An omitted argument stays omitted (<see langword="null"/> in <see cref="Arguments"/>): <c>Circle (1, 1), 2, , 3</c>
/// has a color that is not there, which is not the same as a color that is zero.
/// </para>
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the statement.</param>
/// <param name="Token">Which of the four statements this is: <c>Circle</c>, <c>Line</c>, <c>PSet</c> or <c>Scale</c> (see <c>Tokens</c>).</param>
/// <param name="Target">The object before the dot, or <see langword="null"/> when the statement is written without one - or, for <c>Line</c> in a <c>With</c> block, with a dot and
/// nothing before it (see <see cref="IsWithRelative"/>).</param>
/// <param name="IsWithRelative">Whether the statement is written <c>.Line</c>, which is the <c>Line</c> member of the object of the enclosing <c>With</c> block.</param>
/// <param name="From">The first point: the center of a <c>Circle</c>, the point of a <c>PSet</c>, the start of a <c>Line</c> (<see langword="null"/> when the line starts where the last one ended), or the
/// first corner of a <c>Scale</c>.</param>
/// <param name="To">The second point of a <c>Line</c> or a <c>Scale</c>, which have one; <see langword="null"/> for the others.</param>
/// <param name="Arguments">The expressions that follow the points, in order, <see langword="null"/> where one is omitted: the radius, color, start, end and aspect of a <c>Circle</c>; the color
/// of a <c>Line</c> or a <c>PSet</c>. Empty for a <c>Scale</c>.</param>
/// <param name="LineOption">The <c>B</c> or <c>BF</c> that makes a <c>Line</c> a box, as it was written, or <see langword="null"/> when there is none.</param>
public record class GraphicsMethodStatementNode(
    SyntaxNodeId Identity,
    SourceLocation SourceLocation,
    string Token,
    ExpressionNode? Target,
    bool IsWithRelative,
    GraphicsPointNode? From,
    GraphicsPointNode? To,
    ImmutableArray<ExpressionNode?> Arguments,
    string? LineOption)
    : StatementNode(Identity, SourceLocation, Descendants(Target, From, To, Arguments))
{
    // whichever parts the statement has, in source order - a walk over the tree sees them, and the node itself is what says which part each one is.
    private static ImmutableArray<SyntaxNode> Descendants(
        ExpressionNode? target, GraphicsPointNode? from, GraphicsPointNode? to, ImmutableArray<ExpressionNode?> arguments)
        => [.. new SyntaxNode?[] { target, from, to }.Concat(arguments.IsDefault ? [] : arguments).OfType<SyntaxNode>()];
}
