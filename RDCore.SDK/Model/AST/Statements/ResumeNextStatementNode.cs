using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a statement that resumes error handling at the instruction following the instruction that tripped the last <c>On Error</c> jump
/// (<c>Resume Next</c>, <strong>MS-VBAL §5.4.4.2</strong>).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <remarks>
/// This statement is only legal with an active error state.
/// </remarks>
public record class ResumeNextStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation)
    : StatementNode(Identity, SourceLocation, []);
