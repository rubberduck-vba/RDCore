using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.6.8</strong> New Expressions — <c>New &lt;type-expression&gt;</c>, instantiating
/// a new object of the class referenced by <see cref="TypeExpression"/> and yielding that object.
/// </summary>
/// <remarks>
/// The grammar admits any <c>expression</c> as the operand (matching <c>ctNewExpr</c>'s
/// <c>complexType</c> counterpart used by <c>Dim x As New ClassName</c>); static semantics narrow it
/// to a reference naming an instantiable class.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="TypeExpression">The expression naming the class being instantiated.</param>
public record class NewExpressionNode(SyntaxNodeId Identity, SourceLocation Location, ExpressionNode TypeExpression)
    : ExpressionNode(Identity, Location, [TypeExpression]);
