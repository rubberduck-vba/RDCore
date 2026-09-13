using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a statement that pops an offset from the local <em>return stack</em>, then moves the <em>current instruction</em> pointer to that instruction
/// (<strong>MS-VBAL §5.4.2.15</strong>).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public record class ReturnStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation)
    : StatementNode(Identity, SourceLocation, []);
