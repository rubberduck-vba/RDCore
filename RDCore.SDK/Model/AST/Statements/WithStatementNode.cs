using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a <c>With...End With</c> block.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="WithExpression">The object or UDT expression member accesses inside the block are implicitly qualified with.</param>
/// <param name="Body">The executable statements in the body of the block.</param>
public record class WithStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode WithExpression, StatementBlock Body)
    : StatementNode(Identity, SourceLocation, [WithExpression]);
