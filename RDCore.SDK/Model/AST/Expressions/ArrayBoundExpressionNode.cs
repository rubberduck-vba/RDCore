using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// Which end of an array dimension an <see cref="ArrayBoundExpressionNode"/> reads.
/// </summary>
public enum ArrayBoundKind
{
    /// <summary>The smallest available subscript: <c>LBound</c>.</summary>
    Lower,

    /// <summary>The largest available subscript: <c>UBound</c>.</summary>
    Upper,
}

/// <summary>
/// <strong>MS-VBAL 3.3.5.2</strong> The <c>LBound</c> and <c>UBound</c> special forms —
/// <c>LBound(&lt;array&gt;[, &lt;dimension&gt;])</c> — which read the bound of one dimension of an array.
/// </summary>
/// <remarks>
/// MS-VBAL reserves the names as <c>special-form</c> identifiers, "used in an expression as if [they were] a
/// program defined procedure name but which has special syntactic rules for its argument", and defines no
/// semantics for them. They are keywords, not members of the standard library, and so are a node of their own the
/// way the <c>ReDim</c> statement is: the argument is an array the construct reads the dimensions of and leaves as
/// it is, which is a rule about the construct's own syntax and not about how a procedure's arguments are passed.
/// <para>
/// The grammar lets the keyword through as an identifier, so <c>UBound(a)</c> reaches the parse listener as an
/// index expression on a name. The listener recognizes the keyword token followed by one or two positional
/// arguments and builds this node instead; any other shape stays an index expression.
/// </para>
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the whole construct.</param>
/// <param name="Kind">Whether the lower or the upper bound is read.</param>
/// <param name="Array">The expression yielding the array whose bound is read.</param>
/// <param name="Dimension">
/// The expression naming the dimension, counted from 1; <see langword="null"/> when it is omitted, which means 1.
/// </param>
public sealed record class ArrayBoundExpressionNode(
    SyntaxNodeId Identity, SourceLocation Location, ArrayBoundKind Kind, ExpressionNode Array, ExpressionNode? Dimension = default)
    : ExpressionNode(Identity, Location, Dimension is null ? [Array] : [Array, Dimension]);
