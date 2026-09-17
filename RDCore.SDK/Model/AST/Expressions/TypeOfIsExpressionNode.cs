using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.6.9.4</strong> TypeOf...Is Expressions — <c>TypeOf &lt;expr&gt; Is &lt;type-expression&gt;</c>,
/// testing whether <see cref="Operand"/>'s run-time type is, or derives from, <see cref="TypeExpression"/>.
/// </summary>
/// <remarks>
/// The grammar splits this construct across two alternatives to stay SLL: <c>TypeOf &lt;expr&gt;</c> parses
/// as its own <c>typeofexpr</c> alternative, and the trailing <c>Is &lt;type&gt;</c> as an ordinary <c>IS</c>
/// relational operator around it. The parse listener recognizes that pairing (a <c>typeofexpr</c> as the
/// left operand of an <c>IS</c> relational op) and builds this node for both together, instead of misreading
/// the construct as a plain object-identity <c>Is</c> comparison.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Operand">The expression whose run-time type is being tested.</param>
/// <param name="TypeExpression">The expression naming the type being tested against.</param>
public record class TypeOfIsExpressionNode(SyntaxNodeId Identity, SourceLocation Location, ExpressionNode Operand, ExpressionNode TypeExpression)
    : ExpressionNode(Identity, Location, [Operand, TypeExpression]);
