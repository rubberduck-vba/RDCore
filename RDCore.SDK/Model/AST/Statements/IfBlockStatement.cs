using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a conditional executable statement block.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Condition">A <em>Boolean expression</em> that determines whether execution branches into the <em>conditional statement</em> or not.</param>
/// <param name="ConditionalBlock">A block of <em>executable nodes</em> that is executed if the <em>Condition</em> expression evaluates to <c>True</c>.</param>
/// <param name="ElseIfBlocks">The <c>Else</c> conditional blocks, if any. <strong>Optional</strong></param>
/// <param name="ElseBlock">A block of <em>executable nodes</em> that is executed if the <em>Condition</em> expression evaluates to <c>False</c>. <strong>Optional</strong></param>
public record class IfBlockStatement(Uri SemanticId, SourceLocation Location, BoundExpression Condition, StatementBlock ConditionalBlock, ImmutableArray<ElseIfBlockStatement>? ElseIfBlocks = default, StatementBlock? ElseBlock = default)
    : BoundStatement(SemanticId, Location, Tokens.If, [Condition]);
