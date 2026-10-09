using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a statement that disables error handling (<c>On Error Resume Next</c>,
/// <strong>MS-VBAL §5.4.4.1</strong>).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="IsLocal">
/// <c>true</c> when the statement is written <c>On Local Error Resume Next</c>, the archaic form (<strong>MS-VBAL §5.4.4.1</strong>): every error is local to the
/// procedure it is handled in, so the keyword says nothing, and the two statements are the same.
/// </param>
public record class OnErrorResumeStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, bool IsLocal = false)
    : StatementNode(Identity, SourceLocation, []);
