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
    public SourceRange SourceRange
    {
        get
        {
            var start = new SourcePosition(Start?.Line - 1 ?? 0, Start?.Column ?? 0);
            return new(start.AnchoredAt(_offset), EndOf(start).AnchoredAt(_offset));
        }
    }

    // ANTLR's own type for the token that is the end of the input.
    private const int EndOfFileTokenType = -1;

    // The range ends where the last token of the rule does - after it - which is the position of the next character: an LSP range is end-exclusive, and a rule of
    // one token (`Stop`) would otherwise be a range of nothing. A rule that matched no token, or whose last token ANTLR made up to recover from an error, ends where it begins.
    private SourcePosition EndOf(SourcePosition start)
    {
        if (Start is null || Stop is null || Stop.TokenIndex < Start.TokenIndex || Stop.Type == EndOfFileTokenType)
        {
            // the end of the file is a position and no characters.
            return Stop is { Type: EndOfFileTokenType } eof ? new SourcePosition(eof.Line - 1, eof.Column) : start;
        }

        var line = Stop.Line - 1;
        var column = Stop.Column;
        if (Stop.StopIndex < Stop.StartIndex || Stop.Text is not { Length: > 0 } text)
        {
            return new SourcePosition(line, column);
        }

        // a token that holds a line terminator (the end of a line, a line continuation) ends on a later line, at the characters after the last one.
        var lastBreak = text.LastIndexOf('\n');
        if (lastBreak < 0)
        {
            return new SourcePosition(line, column + text.Length);
        }

        return new SourcePosition(line + text.Count(character => character == '\n'), text.Length - lastBreak - 1);
    }

    public SourceLocation GetSourceLocation(Uri documentUri) => new(documentUri, SourceRange);
}
