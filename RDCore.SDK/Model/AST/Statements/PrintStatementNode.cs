using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// <strong>MS-VBAL 5.4.5.8-9</strong> a <c>Print</c> or <c>Write</c> statement, and the
/// object-relative bare form (<c>Print "x"</c> invoking the enclosing form/report's own
/// <c>Print</c> member — MS-VBAL's own grammar comment calls this an
/// <c>unqualifiedObjectPrintStmt</c>).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Token">The statement's keyword — <c>Print</c> or <c>Write</c> (see <c>Tokens</c>).</param>
/// <param name="FileNumber">The output file channel, or <c>null</c> for the object-relative bare form.</param>
/// <param name="Items">The output list, in source order.</param>
public record class PrintStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, string Token, ExpressionNode? FileNumber, ImmutableArray<PrintOutputItemNode> Items)
    : StatementNode(Identity, SourceLocation, FileNumber is null ? [.. Items] : [FileNumber, .. Items]);
