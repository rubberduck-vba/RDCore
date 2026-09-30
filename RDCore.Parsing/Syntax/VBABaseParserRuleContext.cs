using Antlr4.Runtime;
using RDCore.SDK.Model.Source;

namespace RDCore.Parsing.Syntax;

public abstract class VBABaseParserRuleContext : ParserRuleContext
{
    public VBABaseParserRuleContext() : base() => AnchorAt(SourcePosition.Zero);

    public VBABaseParserRuleContext(ParserRuleContext parent, int invokingStateNumber)
        : base(parent, invokingStateNumber) => AnchorAt(SourcePosition.Zero);

    public VBABaseParserRuleContext(ParserRuleContext parent, int invokingStateNumber, SourcePosition anchor)
    : base(parent, invokingStateNumber) => AnchorAt(anchor);

    /// <summary>
    /// Sets the <em>anchor offset</em> that determines where in the document the source "start token" actually begins.
    /// </summary>
    /// <param name="offset">The line/character position offset in the source document</param>
    /// <remarks>
    /// The offset's Line is added to both the <c>Start</c> and <c>Stop</c> Line positions. Its Character
    /// is added only to a position on the fragment's <em>first</em> line, which is the only line the
    /// fragment shares with whatever precedes it in the document: every later line of the fragment begins
    /// at document column 0, exactly where it begins in the fragment.
    /// </remarks>
    public void AnchorAt(SourcePosition offset) => _offset = offset;
    private SourcePosition _offset;

    /// <summary>
    /// Gets a <see cref="SourceRange"/> representing the line/character start and end positions in the source document,
    /// anchored at an explicitly specified offset; the range is implicitly anchored at <c>L0C0</c> otherwise.
    /// </summary>
    /// <remarks>
    /// 👉 This is the only member that provides a correctly offset/anchored position in the source document
    /// for a <em>source code fragment</em> or partial document; other positional members' values 
    /// would only be safe to surface in the context of a full-document parse.
    /// </remarks>
    // SourcePosition.AnchoredAt, not a plain addition: the anchor's column belongs to the fragment's own
    // first line alone. Adding it to every line pushed every line after the first that many columns to
    // the right - with an anchor of L5C4, a member ending at the start of the fragment's second line
    // reported 7:4 instead of 7:0.
    public SourceRange SourceRange => new(
            new SourcePosition(Start?.Line - 1 ?? 0, Start?.Column ?? 0).AnchoredAt(_offset),
            new SourcePosition(Stop?.Line - 1 ?? 0, Stop?.Column ?? 0).AnchoredAt(_offset));
    public SourceLocation GetSourceLocation(Uri documentUri) => new(documentUri, SourceRange);
}
