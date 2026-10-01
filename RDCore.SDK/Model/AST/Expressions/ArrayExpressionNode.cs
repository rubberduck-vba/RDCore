using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// The <c>Array</c> keyword, written without a qualifier: <c>Array(&lt;element&gt;, ...)</c>, which yields a <c>Variant</c> holding an array of
/// its arguments.
/// </summary>
/// <remarks>
/// The language has two of it. The keyword's array begins at the <c>Option Base</c> of the module that says it - the lower
/// bound of <c>Array(1, 2, 3)</c> is <c>1</c> under <c>Option Base 1</c> - and the member of the library's hidden module, which
/// is what <c>VBA.Array(1, 2, 3)</c> names, always begins at <c>0</c>. Telling them apart is the syntax's to do, because
/// what decides is whether the name was qualified: this node is the unqualified one. The qualified call stays an
/// <see cref="IndexExpressionNode"/> on a member access, and resolves to the library's member like any other call of one.
/// <para>
/// The grammar lets the keyword through as an identifier, so <c>Array(1, 2)</c> reaches the parse listener as an index
/// expression on a name, which the listener recognizes by the token, as it does <c>LBound</c> and <c>UBound</c>
/// (<see cref="ArrayBoundExpressionNode"/>). Arguments that are named or omitted are not elements, and leave the call an
/// index expression for the rules about calls to say what is wrong with it.
/// </para>
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the whole construct.</param>
/// <param name="Elements">The expressions the elements of the array are given, in order; none for <c>Array()</c>.</param>
public sealed record class ArrayExpressionNode(SyntaxNodeId Identity, SourceLocation Location, ImmutableArray<ExpressionNode> Elements)
    : ExpressionNode(Identity, Location, [.. Elements]);
