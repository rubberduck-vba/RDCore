using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// An executable statement node that represents a <c>Select Case...End Select</c> block.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="ControlExpression">The control expression whose evaluation result each <c>Case</c> range clause gets compared to.</param>
/// <param name="CaseExpressionBlocks">The <c>Case</c> blocks, in source order.</param>
/// <param name="CaseElseBlock">The <c>Case Else</c> block, or <c>null</c> when the statement declares none.</param>
public record class SelectCaseStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode ControlExpression,
    ImmutableArray<CaseExpressionStatementNode> CaseExpressionBlocks, CaseElseClauseStatementNode? CaseElseBlock)
    : StatementNode(Identity, SourceLocation, [ControlExpression]);

/// <summary>
/// An executable statement node that represents a <c>Case</c> block within a <c>Select Case</c> block statement.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="RangeClauses">The comma-separated range clauses on this <c>Case</c> line, in source order (MS-VBAL 5.4.2.10).</param>
/// <param name="Block">The body of the <c>Case</c> block.</param>
public record class CaseExpressionStatementNode(SyntaxNodeId Identity, SourceLocation Location, ImmutableArray<CaseRangeClauseNode> RangeClauses, StatementBlock Block)
    : StatementNode(Identity, Location, []);

/// <summary>
/// The <c>Case Else</c> branch of a <c>Select Case</c> block statement.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Body">The executable statements in the <c>Case Else</c> branch's body.</param>
public record class CaseElseClauseStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, StatementBlock Body)
    : StatementNode(Identity, SourceLocation, []);

/// <summary>
/// A <c>Case</c> range clause — one of the comma-separated conditions on a <c>Case</c> line
/// (MS-VBAL 5.4.2.10), each independently a single value, a comparison, or a <c>To</c> range.
/// </summary>
public abstract record class CaseRangeClauseNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Inputs)
    : StatementNode(Identity, SourceLocation, Inputs);

/// <summary>
/// A <c>Case</c> range clause matching a single value (e.g. <c>Case 5</c>).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Value">The value this clause matches against the <c>Select</c> control expression.</param>
public sealed record class CaseValueRangeClauseNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode Value)
    : CaseRangeClauseNode(Identity, SourceLocation, [Value]);

/// <summary>
/// A <c>Case</c> range clause matching by comparison (e.g. <c>Case Is &gt; 5</c>).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="ComparisonOperator">The comparison operator token (see <c>Tokens</c>).</param>
/// <param name="Value">The value the control expression is compared against.</param>
public sealed record class CaseComparisonRangeClauseNode(SyntaxNodeId Identity, SourceLocation SourceLocation, string ComparisonOperator, ExpressionNode Value)
    : CaseRangeClauseNode(Identity, SourceLocation, [Value]);

/// <summary>
/// A <c>Case</c> range clause matching an inclusive range (e.g. <c>Case 1 To 10</c>).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Start">The range's lower bound.</param>
/// <param name="End">The range's upper bound.</param>
public sealed record class CaseToRangeClauseNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode Start, ExpressionNode End)
    : CaseRangeClauseNode(Identity, SourceLocation, [Start, End]);
