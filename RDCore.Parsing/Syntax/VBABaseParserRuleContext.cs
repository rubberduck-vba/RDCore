using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
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
    public SourceRange SourceRange => RangeThrough(_through ?? Stop);

    // a token after the rule's own last one that its range runs through: the binary operator no rule read, at the end of the statement the rule ends with.
    private IToken? _through;

    /// <summary>
    /// Makes the rule's range run through <paramref name="token"/>, a token written after its own last one that is part of what it says: the binary operator no
    /// rule read at the end of the statement the rule ends with (<c>a = 1 -</c>), which the unbuilt operation of the rule's last expression holds.
    /// </summary>
    internal void ExtendThrough(IToken token) => _through = token;

    /// <summary>
    /// The binary operator written right after this expression, at the end of its statement, of which the parse read no operation, and the text of the
    /// expression through it: the operation, its right operand missing, is kept as an unbuilt expression of that text.
    /// </summary>
    internal (IToken Operator, string Text)? DanglingOperator { get; set; }

    /// <summary>
    /// The location of the rule's text through <paramref name="stop"/>, a token after its own last one.
    /// </summary>
    internal SourceLocation GetSourceLocation(Uri documentUri, IToken stop) => new(documentUri, RangeThrough(stop));

    /// <summary>
    /// The range of one token of the rule that holds no line terminator, anchored as <see cref="SourceRange"/> is.
    /// </summary>
    internal SourceRange RangeOf(IToken token)
    {
        var start = new SourcePosition(token.Line - 1, token.Column);
        return new(start.AnchoredAt(_offset), (start with { Character = start.Character + (token.Text?.Length ?? 0) }).AnchoredAt(_offset));
    }

    /// <summary>
    /// The range of a part of one token of the rule, which may span line terminators, anchored as <see cref="SourceRange"/> is.
    /// </summary>
    /// <param name="token">A token of the rule.</param>
    /// <param name="offset">Where the part starts, in characters from the start of the token's text.</param>
    /// <param name="length">The number of characters in the part.</param>
    internal SourceRange RangeWithin(IToken token, int offset, int length)
    {
        var text = token.Text ?? string.Empty;
        SourcePosition At(int index)
        {
            var breaks = 0;
            var lineStart = -1;
            for (var character = 0; character < index; character++)
            {
                if (text[character] == '\n')
                {
                    breaks++;
                    lineStart = character;
                }
            }

            return breaks == 0 ? new SourcePosition(token.Line - 1, token.Column + index) : new SourcePosition(token.Line - 1 + breaks, index - lineStart - 1);
        }

        return new(At(offset).AnchoredAt(_offset), At(offset + length).AnchoredAt(_offset));
    }

    private SourceRange RangeThrough(IToken? stop)
    {
        var start = new SourcePosition(Start?.Line - 1 ?? 0, Start?.Column ?? 0);
        return new(start.AnchoredAt(_offset), EndOf(start, stop).AnchoredAt(_offset));
    }

    // ANTLR's own type for the token that is the end of the input.
    private const int EndOfFileTokenType = -1;

    // The range ends where its last token (`stop`, the rule's own unless a later one is asked for) does - after it - which is the position of the next character:
    // an LSP range is end-exclusive, and a rule of one token would otherwise be a range of nothing. A rule that matched no token, or whose last token ANTLR made up
    // to recover from an error, ends where it begins.
    private SourcePosition EndOf(SourcePosition start, IToken? stop)
    {
        if (Start is null || stop is null || stop.TokenIndex < Start.TokenIndex || stop.Type == EndOfFileTokenType)
        {
            // the end of the file is a position and no characters.
            return stop is { Type: EndOfFileTokenType } eof ? new SourcePosition(eof.Line - 1, eof.Column) : start;
        }

        var line = stop.Line - 1;
        var column = stop.Column;
        if (stop.StopIndex < stop.StartIndex || stop.Text is not { Length: > 0 } text)
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

    /// <summary>
    /// The location and the text of the rule as it is written: through its last token that is neither a blank (a whitespace, a line continuation) nor a
    /// token ANTLR made up to recover from an error. What the parse read after it, for what it found missing (the blanks after <c>a = (1 +</c>, the <c>)</c> of
    /// <c>Foo(-</c>), is no part of it.
    /// </summary>
    internal (SourceLocation Location, string Text) Written(Uri documentUri)
        => LastWrittenToken(this) is { } last && Start is { StartIndex: >= 0 }
            ? (GetSourceLocation(documentUri, last), Start.InputStream.GetText(Interval.Of(Start.StartIndex, last.StopIndex)))
            : (GetSourceLocation(documentUri), GetText());

    /// <summary>
    /// The location of a clause of a block statement (<c>ElseIf</c>, <c>Else</c>, <c>Case</c>, <c>Case Else</c>), through its last written token. What
    /// follows its last statement (the comment after it, the line ends and the indentation before the keyword that closes the clause) is no part of it: a clause
    /// ends where its last statement does, or with its own header when it has none.
    /// </summary>
    internal SourceLocation GetClauseLocation(Uri documentUri)
        => LastWrittenToken(this, outsideEndsOfStatement: true) is { } last ? GetSourceLocation(documentUri, last) : GetSourceLocation(documentUri);

    private static IToken? LastWrittenToken(IParseTree node, bool outsideEndsOfStatement = false) => node switch
    {
        VBAParser.EndOfStatementContext when outsideEndsOfStatement => null,
        ITerminalNode { Symbol: var token } => token.TokenIndex < 0 || token.Type is VBAParser.WS or VBAParser.LINE_CONTINUATION or TokenConstants.Eof ? null : token,
        _ => Enumerable.Range(0, node.ChildCount).Reverse().Select(i => LastWrittenToken(node.GetChild(i), outsideEndsOfStatement)).FirstOrDefault(token => token is not null),
    };
}
