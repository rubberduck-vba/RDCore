using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Declarations;

/// <summary>
/// The <c>(&#8230;)</c> array-dimension clause of a variable declaration (MS-VBAL &#167;5.2.3.1.3,
/// <em>Array Dim</em>). An empty clause (<c>Dim x()</c>) declares a <em>dynamic array</em>
/// (see <see cref="IsResizable"/>); one or more <see cref="Bounds"/> declare a <em>fixed-size array</em>.
/// </summary>
/// <remarks>
/// The declaration pass keeps each bound as verbatim expression text and does <strong>not</strong>
/// evaluate it: a bound is a <c>&lt;constant-expression&gt;</c> that may reference module or local
/// <c>Const</c>s, and an omitted lower bound resolves against <c>Option Base</c> — both are a later
/// semantic pass's concern.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The source location of the array-dimension clause.</param>
/// <param name="Bounds">One entry per declared dimension, outermost first; empty for a dynamic array.</param>
public record class ArrayBoundsNode(SyntaxNodeId Identity, SourceLocation Location, ImmutableArray<ArrayDimensionBound> Bounds)
    : SyntaxNode(Identity, Location, [])
{
    /// <summary>
    /// <c>true</c> when the clause declares a dynamic array — an empty <c>()</c> with no bounds,
    /// which must be given dimensions by a <c>ReDim</c> statement before use.
    /// </summary>
    public bool IsResizable => Bounds.IsDefaultOrEmpty;

    /// <summary>
    /// The number of declared dimensions (MS-VBAL array <em>rank</em>); <c>0</c> for a dynamic array.
    /// </summary>
    public int Rank => Bounds.IsDefault ? 0 : Bounds.Length;
}

/// <summary>
/// The bounds of a single array dimension within an <see cref="ArrayBoundsNode"/> — one MS-VBAL
/// <em>dim-spec</em> (<c>[&lt;lower-bound&gt; To] &lt;upper-bound&gt;</c>).
/// </summary>
/// <param name="LowerBound">
/// The lower-bound expression text, or <c>null</c> when the <c>To</c> clause is omitted (the
/// effective lower bound then follows <c>Option Base</c>).
/// </param>
/// <param name="UpperBound">The upper-bound expression text.</param>
/// <param name="LowerExpression">The lower-bound expression, parsed, or <c>null</c> when there is no lower bound.</param>
/// <param name="UpperExpression">
/// The upper-bound expression, parsed, or <c>null</c> when it could not be (a syntax error left it incomplete). A bound is a
/// constant expression (<strong>MS-VBAL §5.2.3.1.3</strong>) the runtime reduces when the array's storage is allocated, which is
/// what these are for: the text above is what the source said, and these are what it means.
/// </param>
public readonly record struct ArrayDimensionBound(
    string? LowerBound, string UpperBound, ExpressionNode? LowerExpression = null, ExpressionNode? UpperExpression = null);
