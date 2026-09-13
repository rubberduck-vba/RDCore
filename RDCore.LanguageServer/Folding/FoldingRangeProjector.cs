using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;

namespace RDCore.LanguageServer.Folding;

/// <summary>
/// Projects a parsed module's member declarations into LSP <see cref="FoldingRange"/> values.
/// </summary>
/// <remarks>
/// Kept apart from the handler and free of protocol plumbing so the projection can be tested against a
/// parse result directly.
/// </remarks>
internal static class FoldingRangeProjector
{
    /// <summary>
    /// One folding range per member declaration that spans more than one line.
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
        }
    }
}
