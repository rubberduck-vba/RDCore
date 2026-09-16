using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a statement that defines a locally-scoped error handler label
/// (<strong>MS-VBAL §5.4.4.1</strong>).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="LabelExpression">
/// Whatever expression followed <c>GoTo</c> — usually a real label, but not always: MS-VBAL §5.4.4.1
/// carves out the line-number-label <c>0</c> as a sentinel meaning "error handling disabled", not a
/// label to resolve. Real-world VBA also treats <c>-1</c> as a sentinel ("clear the current error, so a
/// later <c>On Error</c> can re-arm"), though MS-VBAL does not document that form. A future consumer
/// resolving this against the module's labels must special-case both before treating it as a real
/// label reference.
/// </param>
public record class OnErrorGoToStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode LabelExpression)
    : StatementNode(Identity, SourceLocation, [LabelExpression]);
