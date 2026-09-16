using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents an <c>ElseIf</c> branch of an <c>If</c> conditional block.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="ConditionExpression">This branch's condition; coerced to <c>Boolean</c> to select this branch.</param>
/// <param name="Body">The executable statements in this branch's body.</param>
public record class ElseIfBlockStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode ConditionExpression, StatementBlock Body)
    : StatementNode(Identity, SourceLocation, [ConditionExpression]);

/// <summary>
/// Represents the <c>Else</c> branch of an <c>If</c> conditional block.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Body">The executable statements in the <c>Else</c> branch's body.</param>
public record class ElseBlockStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, StatementBlock Body)
    : StatementNode(Identity, SourceLocation, []);


/// <summary>
/// Represents a conditional executable statement block that is part of an <c>#If...#ElseIf</c> precompiler conditional branch.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public record class PrecompilerElseIfBlockStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children)
    : StatementNode(Identity, SourceLocation, Children);

/// <summary>
/// Represents a conditional executable statement block that is part of an <c>#If...#Else</c> precompiler conditional branch.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public record class PrecompilerElseBlockStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children)
    : StatementNode(Identity, SourceLocation, Children);
