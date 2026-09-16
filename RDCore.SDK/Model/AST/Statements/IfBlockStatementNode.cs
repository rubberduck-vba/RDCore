using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// An executable statement node that represents an <c>If...Then...ElseIf...Else...End If</c> block.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="ConditionExpression">The <c>If</c> branch's condition; coerced to <c>Boolean</c> to select this branch.</param>
/// <param name="Body">The executable statements in the <c>If</c> branch's body.</param>
/// <param name="ElseIfBlocks">The <c>ElseIf</c> branches, in source order.</param>
/// <param name="ElseBlock">The <c>Else</c> branch, or <c>null</c> when the block declares none.</param>
public record class IfBlockStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode ConditionExpression, StatementBlock Body,
    ImmutableArray<ElseIfBlockStatementNode> ElseIfBlocks, ElseBlockStatementNode? ElseBlock)
    : StatementNode(Identity, SourceLocation, [ConditionExpression]);

/// <summary>
/// Represents a conditional executable statement block.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public record class PrecompilerIfBlockStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children)
    : StatementNode(Identity, SourceLocation, Children);

