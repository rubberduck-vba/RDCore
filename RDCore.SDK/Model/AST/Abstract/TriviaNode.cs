using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Abstract;

public record class PrecompilerTriviaNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children, string Source)
    : SyntaxNode(Identity, SourceLocation, Children);

public record class CommentTriviaNode(SyntaxNodeId Identity, SourceLocation SourceLocation, string Value)
    : SyntaxNode(Identity, SourceLocation, []);

/// <summary>
/// Preserves a part of a class, form or document module's <em>file header</em>: the <c>VERSION 1.0 CLASS</c> line, the <c>Object = "{GUID}#..."</c> references,
/// or the top-level <c>BEGIN ... END</c> configuration block.
/// </summary>
/// <remarks>
/// None of it is code, but it is source: a faithful tree has to account for it so that a formatter or an exporter can write the module back out as it was read.
/// Whatever the grammar recognized inside the header is kept as <see cref="Source"/> only, since no consumer binds anything to it.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the header part.</param>
/// <param name="Source">The exact original source text of the header part.</param>
public sealed record class ModuleHeaderTriviaNode(SyntaxNodeId Identity, SourceLocation SourceLocation, string Source)
    : SyntaxNode(Identity, SourceLocation, []);

public record class AnnotationTriviaNode(SyntaxNodeId Identity, SourceLocation SourceLocation, string Name, ImmutableArray<SyntaxNode> Children)
    : SyntaxNode(Identity, SourceLocation, Children);

/// <summary>
/// Preserves a unary <c>+</c> written before an expression (<strong>RD-VBAL §5.6.9.3</strong>): <c>x = +1</c>, <c>Foo +x</c>.
/// </summary>
/// <remarks>
/// A unary <c>+</c> is no code. MS-VBAL's grammar has no unary <c>+</c>, and the VBE erases one from the code, whether it is typed or imported:
/// <c>x = +1</c> is <c>x = 1</c>, and <c>+"1"</c> is the String <c>"1"</c>. So no node of the syntax tree stands for it. The expression after it is the
/// node it would be without it, of its own type: <c>Foo +x</c> passes <c>x</c> by reference, and <c>Set o = +obj</c> assigns the object.
/// <para>
/// It is still source, kept as the module's trivia is, so that a formatter or an exporter can write the module back out as it was read, and a diagnostic can
/// say where it is.
/// </para>
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the <c>+</c>.</param>
public sealed record class ErasedPlusTriviaNode(SyntaxNodeId Identity, SourceLocation SourceLocation)
    : SyntaxNode(Identity, SourceLocation, []);

/// <summary>
/// Preserves the raw source text of an <c>expression</c> that the tree does not model: an alternative the grammar recognizes but that has no dedicated AST
/// node yet (e.g. <c>New &lt;class&gt;</c>, <c>TypeOf &lt;expr&gt; Is &lt;type&gt;</c>), or an expression nested too deeply to be analyzed (<c>VBC00060</c>,
/// <strong>RD-VBAL §2.6.1</strong>).
/// </summary>
/// <remarks>
/// The AST must stay a faithful, lossless representation of the source even where no semantic node stands for it: silently building a <em>different</em>,
/// wrong-meaning node in its place (or building nothing at all) would misrepresent the source rather than merely under-represent it. <see cref="Source"/> is
/// the exact original text, which lets a consumer that needs to reconstruct the source (e.g. a formatter) do so verbatim, while a consumer that only cares
/// about semantics can tell at a glance that this position wasn't modeled.
/// <para>
/// For an alternative that has no node, <see cref="ExpressionNode.Inputs"/> keeps whatever the grammar's own sub-expression already built, so that nothing the walk already
/// parsed is thrown away. An expression nested in more than 2,500 others (<c>VBC00060</c>) is the other way around: what it holds is thrown away on purpose
/// and <see cref="ExpressionNode.Inputs"/> is empty, so that no stage after the parser is handed a tree deeper than the limit. A name written in its text is not analyzed -
/// it is neither bound nor counted as a use - and a rename of what it could refer to is refused
/// (<see cref="Semantics.Refactoring.RenameRefusalKind.Unresolved"/>). Where each such name is written is in <see cref="Names"/>.
/// </para>
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the unbuilt expression.</param>
/// <param name="Source">The exact original source text of the unbuilt expression.</param>
/// <param name="Inputs">
/// Whatever sub-expression(s) the grammar's own walk already built underneath it; none for an expression nested too deeply to be analyzed.
/// </param>
public sealed record class UnbuiltExpressionTriviaNode(SyntaxNodeId Identity, SourceLocation Location, string Source, ImmutableArray<SyntaxNode> Inputs)
    : ExpressionNode(Identity, Location, Inputs)
{
    /// <summary>
    /// The names written in <see cref="Source"/> that no node of <see cref="ExpressionNode.Inputs"/> stands for, in the order they are written, as the VBA lexer reads them.
    /// </summary>
    /// <remarks>
    /// A name is a token of its own: never a part of a string, date or numeric literal (<c>1#</c> is the number <c>1</c>, <c>&amp;HFF</c> a number too), nor
    /// of a comment. The key of a dictionary access (<c>b</c> in <c>a!b</c>) is the text of a string, and is no name. Empty for an expression whose every name
    /// is in what the walk built of it.
    /// </remarks>
    public ImmutableArray<UnbuiltName> Names { get; init; } = [];
}

/// <summary>
/// A name written in the text of an <see cref="UnbuiltExpressionTriviaNode"/>.
/// </summary>
/// <param name="Name">The name as it is written, without its type character (<c>b</c> of <c>b%</c>) or the brackets of a foreign name (<c>a b</c> of <c>[a b]</c>).</param>
/// <param name="Range">Where <paramref name="Name"/> is written: inside the brackets of a foreign name.</param>
/// <param name="IsMember">Whether the name is written after a <c>.</c>: it names a member of what is before the <c>.</c>, not what is in scope.</param>
public readonly record struct UnbuiltName(string Name, SourceRange Range, bool IsMember);

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
