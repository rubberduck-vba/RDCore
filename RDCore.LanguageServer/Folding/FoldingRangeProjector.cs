using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.LanguageServer.Folding;

/// <summary>
/// Projects a parsed module's member declarations, and the blocks of their structured statements, into LSP <see cref="FoldingRange"/> values.
/// </summary>
/// <remarks>
/// Kept apart from the handler and free of protocol plumbing so the projection can be tested against a
/// parse result directly.
/// </remarks>
internal static class FoldingRangeProjector
{
    /// <summary>
    /// One folding range per member declaration that spans more than one line, and one per structured statement of its body that does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two filters, and they do different jobs. The type filter decides what counts as a foldable
    /// construct: <see cref="MemberDeclarationNode"/> covers procedures, functions, property accessors,
    /// <c>Type</c>, <c>Enum</c>, <c>Event</c> and <c>Declare</c>. Fields, constants and directives are
    /// other node types, so they never reach the span test at all.
    /// </para>
    /// <para>
    /// The span test then decides which of those members are worth folding. A single-line member — a
    /// <c>Declare</c>, most commonly — would collapse nothing, so it is skipped; the same <c>Declare</c>
    /// broken over a line continuation spans two lines and folds. Deciding by span rather than by a list
    /// of <see cref="MemberKind"/> values means a member kind added later needs no change here.
    /// </para>
    /// <para>
    /// The fold covers the whole declaration including its <c>End</c> statement, so a collapsed procedure
    /// shows only its signature.
    /// </para>
    /// <para>
    /// A block - <c>If</c>, <c>Select Case</c>, a <c>For</c>, <c>For Each</c>, <c>Do</c> or <c>While</c> loop, <c>With</c> - folds from its first line to its
    /// last, however deep it is in another; its branches (<c>ElseIf</c>, <c>Else</c>, <c>Case</c>) fold with it, not apart. A member's range is a
    /// <see cref="FoldingRangeKind.Region"/>, and a block's has no kind: a client tells one from the other by it, as the interactive shell does, which folds
    /// blocks only.
    /// </para>
    /// <para>
    /// The module itself gets no range: <c>ModuleNode</c> is constructed with an empty source range, so
    /// there is nothing to project one from.
    /// </para>
    /// </remarks>
    public static IEnumerable<FoldingRange> Project(ModuleNode module)
    {
        foreach (var member in module.Children.OfType<MemberDeclarationNode>())
        {
            var range = member.SourceLocation.Range;

            if (range.End.Line <= range.Start.Line)
            {
                continue;
            }

            yield return new FoldingRange
            {
                StartLine = range.Start.Line,
                EndLine = range.End.Line,
                Kind = FoldingRangeKind.Region,
            };

            foreach (var block in member.Descendants().Where(IsBlock))
            {
                var blockRange = block.SourceLocation.Range;
                if (blockRange.End.Line > blockRange.Start.Line)
                {
                    yield return new FoldingRange { StartLine = blockRange.Start.Line, EndLine = blockRange.End.Line };
                }
            }
        }
    }

    // a structured statement, with the blocks of statements it guards; not one of its branches, which fold with it, and not a single-line If.
    private static bool IsBlock(SyntaxNode node) => node is IfBlockStatementNode
        or SelectCaseStatementNode
        or ForStatementNode
        or ForEachStatementNode
        or DoLoopStatementNode
        or DoWhileLoopStatementNode
        or DoUntilLoopStatementNode
        or DoLoopWhileStatementNode
        or DoLoopUntilStatementNode
        or WhileWendStatementNode
        or WithStatementNode;
}
