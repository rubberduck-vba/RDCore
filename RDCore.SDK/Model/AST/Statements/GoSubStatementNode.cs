using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a statement that pushes the next <em>instruction offset</em> to the local <em>return stack</em>, then moves the <em>current instruction</em> pointer to a specified label
/// (<strong>MS-VBAL §5.4.2.14</strong>).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="LabelExpression">An expression that resolves to the local label this statement jumps to.</param>
public record class GoSubStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode LabelExpression)
    : StatementNode(Identity, SourceLocation, [LabelExpression]);
