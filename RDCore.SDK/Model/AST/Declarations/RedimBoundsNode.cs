using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Declarations;

/// <summary>
/// The <c>(&#8230;)</c> dimension clause of a <c>ReDim</c> statement — MS-VBAL &#167;5.4.3.3's
/// <c>dynamic-bounds-list</c>.
/// </summary>
/// <remarks>
/// Not an <see cref="ArrayBoundsNode"/>, though the two look alike in source. A <c>Dim</c>'s bounds are
/// <c>&lt;constant-expression&gt;</c>s that a later pass folds, so that node keeps them as verbatim text; a
/// <c>ReDim</c>'s are <em>ordinary run-time expressions</em> — <c>ReDim Grid(1 To n * 2)</c> is legal and
/// <c>n</c> is not knowable until the statement runs. Text cannot be evaluated without parsing it again, so
/// these are real expression nodes.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The source location of the dimension clause.</param>
/// <param name="Dimensions">One entry per dimension, outermost first.</param>
public record class RedimBoundsNode(SyntaxNodeId Identity, SourceLocation Location, ImmutableArray<RedimDimensionNode> Dimensions)
    : SyntaxNode(Identity, Location, [.. Dimensions])
{
    /// <summary>
    /// The number of dimensions the statement gives the array (MS-VBAL array <em>rank</em>).
    /// </summary>
    public int Rank => Dimensions.IsDefault ? 0 : Dimensions.Length;
}

/// <summary>
/// One dimension of a <see cref="RedimBoundsNode"/> — MS-VBAL &#167;5.4.3.3's <c>dynamic-dim-spec</c>,
/// <c>[&lt;dynamic-lower-bound&gt; To] &lt;dynamic-upper-bound&gt;</c>.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The source location of the dimension.</param>
/// <param name="LowerBound">
/// The lower-bound expression, or <c>null</c> when the <c>To</c> clause is omitted — in which case the
/// effective lower bound follows <c>Option Base</c>. Absent rather than synthesized, because the statement
/// did not write one.
/// </param>
/// <param name="UpperBound">The upper-bound expression.</param>
public record class RedimDimensionNode(SyntaxNodeId Identity, SourceLocation Location, ExpressionNode? LowerBound, ExpressionNode UpperBound)
    : SyntaxNode(Identity, Location, LowerBound is null ? [UpperBound] : [LowerBound, UpperBound]);
