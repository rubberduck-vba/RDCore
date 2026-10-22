using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a <c>For Each...Next</c> loop construct.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="ControlExpression">An object or variant expression that resolves to the loop control variable.</param>
/// <param name="CollectionExpression">An object expression that resolves to an enumerable object (or array).</param>
/// <param name="Body">The executable statements in the body of the loop.</param>
public record class ForEachStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode ControlExpression, ExpressionNode CollectionExpression, StatementBlock Body)
    : StatementNode(Identity, SourceLocation, [ControlExpression, CollectionExpression])
{
    /// <summary>
    /// The variable named by the <c>Next</c> statement that ends the loop - <c>Next i</c>, or the name after a comma of the <c>Next</c> of a loop nested in
    /// it (<c>Next j, i</c>) - or <see langword="null"/> when it names none. It is the loop's control variable (MS-VBA: "Invalid Next control variable
    /// reference" when it is not).
    /// </summary>
    public ExpressionNode? NextExpression { get; init; }
}

