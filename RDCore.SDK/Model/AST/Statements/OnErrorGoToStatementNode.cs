using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a statement that defines a locally-scoped error handler label
/// (<strong>MS-VBAL §5.4.4.1</strong>).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="LabelExpression">An expression that resolves to a <em>label</em> denoting the error-handling subroutine.</param>
public record class OnErrorGoToStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode LabelExpression)
    : StatementNode(Identity, SourceLocation, [LabelExpression]);
