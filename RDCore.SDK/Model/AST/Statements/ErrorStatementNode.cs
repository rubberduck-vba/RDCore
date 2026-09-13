using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a (legacy) statement that raises a run-time error (<strong>MS-VBAL §5.4.4.3</strong>).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="NumberExpression">The number/code of the run-time error to raise.</param>
public record class ErrorStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode NumberExpression)
    : StatementNode(Identity, SourceLocation, [NumberExpression]);
