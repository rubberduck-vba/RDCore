using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a statement that resumes error handling, optionally at a specified label
/// (<strong>MS-VBAL §5.4.4.2</strong>).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="LabelExpression">
/// Whatever expression followed <c>Resume</c>, or <c>null</c> for a bare <c>Resume</c>. MS-VBAL
/// §5.4.4.2 carves out the line-number-label <c>0</c> the same way it does for <c>On Error GoTo 0</c>
/// (see <see cref="OnErrorGoToStatementNode"/>) — a sentinel, not a label to resolve. A future consumer
/// resolving this against the module's labels must special-case it before treating it as a real label
/// reference.
/// </param>
/// <remarks>
/// This statement is only legal with an active error state.
/// </remarks>
public record class ResumeStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode? LabelExpression)
    : StatementNode(Identity, SourceLocation, LabelExpression is null ? [] : [LabelExpression]);
