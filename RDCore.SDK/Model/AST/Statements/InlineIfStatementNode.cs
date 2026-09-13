using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a single-line <c>If</c> statement (<strong>MS-VBAL §5.4.2.9</strong>) — same condition
/// shape as <see cref="IfBlockStatementNode"/>, but both branches sit on one source line and may hold
/// several colon-separated statements instead of a full <c>block</c>.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="ConditionExpression">The condition; coerced to <c>Boolean</c> to select the <c>Then</c> branch.</param>
/// <param name="ThenBody">
/// The statements on the <c>Then</c> side. A bare line-number target (<c>If x Then 100</c>) is
/// represented as its MS-VBAL-specified equivalent — a synthesized <see cref="GoToStatementNode"/> — as
/// the first (and typically only) statement, rather than as a distinct label-reference shape.
/// </param>
/// <param name="ElseBody">The statements on the <c>Else</c> side, or <c>null</c> when the statement declares none.</param>
public record class InlineIfStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode ConditionExpression, StatementBlock ThenBody, StatementBlock? ElseBody)
    : StatementNode(Identity, SourceLocation, [ConditionExpression]);

/// <summary>
/// Represents a conditional executable statement.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public record class PrecompilerInlineIfStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children)
    : StatementNode(Identity, SourceLocation, Children);
