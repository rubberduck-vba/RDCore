using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// <strong>MS-VBAL §5.4.2.13</strong> On...GoTo Statement — evaluates <see cref="Selector"/>, Let-coerces
/// it to <c>Integer</c>, and moves the <em>current instruction</em> pointer to the corresponding label
/// in <see cref="Labels"/> (1-based; out of range completes the statement without branching).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Selector">The expression evaluated to select a label from <see cref="Labels"/>.</param>
/// <param name="Labels">The ordered list of label expressions <see cref="Selector"/> indexes into.</param>
public record class OnGoToStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode Selector, ImmutableArray<ExpressionNode> Labels)
    : StatementNode(Identity, SourceLocation, [Selector, .. Labels]);
