using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// <strong>MS-VBAL 5.4.5.4/5.4.5.5</strong> the <c>Lock</c> and <c>Unlock</c> statements.
/// </summary>
/// <remarks>
/// Their <c>record-range</c> is <c>start-record-number / ([start-record-number] "To" end-record-number)</c> —
/// three shapes, and a flat list of expressions can only tell two of them apart: <c>Lock #1, 5</c> locks
/// record 5 alone and <c>Lock #1, To 5</c> locks records 1 through 5, and both carry one expression. So which
/// end of the range an expression <em>is</em> has to be part of the node rather than inferred from its
/// position, which is the same reason <see cref="OpenStatementNode"/> has a shape of its own.
/// <para>
/// 👉 The absent <c>start-record-number</c> of the <c>To</c>-only form stays absent here. The specification's
/// "the effect is as if <c>start-record-number</c> consisted of the integer number token 1" is a runtime
/// semantic, and a node that carried a synthesized <c>1</c> would claim the program had written one.
/// </para>
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the statement.</param>
/// <param name="Token">Which of the two statements this is — <c>Lock</c> or <c>Unlock</c>.</param>
/// <param name="FileNumber">The file number whose file is locked or unlocked.</param>
/// <param name="StartRecord">The <c>start-record-number</c>, or <c>null</c> when the statement declares none.</param>
/// <param name="EndRecord">The <c>end-record-number</c> after <c>To</c>, or <c>null</c> when there is no
/// <c>To</c> clause — in which case <see cref="StartRecord"/> is the whole range, one record or byte wide.</param>
public record class FileLockStatementNode(
    SyntaxNodeId Identity,
    SourceLocation SourceLocation,
    string Token,
    ExpressionNode FileNumber,
    ExpressionNode? StartRecord,
    ExpressionNode? EndRecord)
    : StatementNode(Identity, SourceLocation, Descendants(FileNumber, StartRecord, EndRecord))
{
    /// <summary>
    /// Whether the statement declared no <c>record-range</c> at all, which applies it to the entire file.
    /// </summary>
    public bool IsEntireFile => StartRecord is null && EndRecord is null;

    // whichever ends of the range the statement declared, in source order - a walk over the tree sees the
    // expressions it has, and the node itself is what says which end each one is.
    private static ImmutableArray<SyntaxNode> Descendants(
        ExpressionNode fileNumber, ExpressionNode? startRecord, ExpressionNode? endRecord)
        => [fileNumber, .. new[] { startRecord, endRecord }.OfType<SyntaxNode>()];
}
