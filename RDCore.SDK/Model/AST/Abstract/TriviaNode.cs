using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Abstract;

public record class PrecompilerTriviaNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children, string Source)
    : SyntaxNode(Identity, SourceLocation, Children);

public record class CommentTriviaNode(SyntaxNodeId Identity, SourceLocation SourceLocation, string Value)
    : SyntaxNode(Identity, SourceLocation, []);

public record class AnnotationTriviaNode(SyntaxNodeId Identity, SourceLocation SourceLocation, string Name, ImmutableArray<SyntaxNode> Children)
    : SyntaxNode(Identity, SourceLocation, Children);

/// <summary>
/// Preserves the raw source text of an <c>expression</c> alternative the grammar recognizes but that
/// has no dedicated AST node yet (e.g. <c>New &lt;class&gt;</c>, <c>TypeOf &lt;expr&gt; Is &lt;type&gt;</c>).
/// </summary>
/// <remarks>
/// The AST must stay a faithful, lossless representation of the source even where a proper semantic
/// node doesn't exist yet — silently building a <em>different</em>, wrong-meaning node in its place
/// (or building nothing at all) would misrepresent the source rather than merely under-represent it.
/// <see cref="Inputs"/> keeps whatever the grammar's own sub-expression already built (so nothing the
/// walk already parsed is thrown away), and <see cref="Source"/> is the exact original text — together
/// they let a consumer that needs to reconstruct the source (e.g. a formatter) do so verbatim, while a
/// consumer that only cares about semantics can tell at a glance that this position wasn't modeled.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the unbuilt expression.</param>
/// <param name="Source">The exact original source text of the unbuilt expression.</param>
/// <param name="Inputs">Whatever sub-expression(s) the grammar's own walk already built underneath it.</param>
public sealed record class UnbuiltExpressionTriviaNode(SyntaxNodeId Identity, SourceLocation Location, string Source, ImmutableArray<SyntaxNode> Inputs)
    : ExpressionNode(Identity, Location, Inputs);

/// <summary>
/// Preserves the raw source text of a <em>required</em> statement-level construct that ANTLR's own
/// recovery left incomplete (a mandatory sub-expression/identifier missing) — the statement-position
/// counterpart of <see cref="UnbuiltExpressionTriviaNode"/>.
/// </summary>
/// <remarks>
/// Applies only where the grammar guarantees the missing piece is <em>mandatory</em> (a bare
/// <c>RaiseEvent</c> with no event name, a <c>GoTo</c> with no label, an <c>On Error Resume</c> missing
/// its <c>Next</c>) — never where a piece is legitimately optional and its absence is valid, unrecovered
/// input (e.g. an omitted optional argument). See <see cref="UnbuiltExpressionTriviaNode"/>'s remarks for
/// why silently building nothing there would still lose reconstructable source text.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the unbuilt statement.</param>
/// <param name="Source">The exact original source text of the unbuilt statement.</param>
/// <param name="Inputs">Whatever sub-expression(s) the grammar's own walk already built underneath it.</param>
public sealed record class UnbuiltStatementTriviaNode(SyntaxNodeId Identity, SourceLocation SourceLocation, string Source, ImmutableArray<SyntaxNode> Inputs)
    : StatementNode(Identity, SourceLocation, Inputs);
