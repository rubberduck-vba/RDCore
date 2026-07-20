using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// An executable statement node that represents a <c>Select Case...End Select</c> block.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="ControlExpression">The control expression whose evaluation result each <c>Case</c> expression gets compared to.</param>
/// <param name="CaseExpressionBlocks">The <c>Case</c> sub-expressions.</param>
public record class SelectCaseStatement(Uri SemanticId, Location Location, BoundExpression ControlExpression, ImmutableArray<CaseExpressionStatement> CaseExpressionBlocks)
    : BoundStatement(SemanticId, Location, Tokens.Select, [ControlExpression]);

/// <summary>
/// An executable statement node that represents a <c>Case</c> block within a <c>Select Case</c> block statement.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Expression">The expression whose evaluation result is to be compared to the <c>Select</c> control expression.</param>
/// <param name="Block">The body of the <c>Case</c> block.</param>
public record class CaseExpressionStatement(Uri SemanticId, Location Location, BoundExpression Expression, StatementBlock Block)
    : BoundStatement(SemanticId, Location, Tokens.Case, [Expression]);
