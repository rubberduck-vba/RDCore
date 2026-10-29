using Antlr4.Runtime;
using RDCore.Parsing.Syntax;

namespace RDCore.Parsing;

/// <summary>
/// The parts of a module that can be parsed each on its own and stitched into the tree a parse of the whole module gives: its declarations section, then
/// each of its members (procedures and properties), each from its header to its <c>End</c>. Found from the module's tokens, before any parse.
/// </summary>
/// <remarks>
/// <para>
/// The lines between two members - blank, or comments - are the second member's: a segment runs from the line after the previous one's <c>End</c> to its
/// own <c>End</c>, and the lines after the last <c>End</c> are the last member's. Every line of the module is in exactly one segment, in order.
/// </para>
/// <para>
/// There are no segments - the module is parsed whole - when the members don't pair as a parse of the whole module would find them: a header in a member,
/// an <c>End</c> outside one, a header or an <c>End</c> that is not the first statement of its line, code between two members or after the last (an
/// attribute of a member, written after its <c>End</c>, included), a member with no <c>End</c>, or a module with no member.
/// </para>
/// </remarks>
internal static class ModuleSegmentScanner
{
    /// <summary>A part of a module, as lines of it: from <see cref="StartLine"/>, up to and not including <see cref="EndLine"/>.</summary>
    public readonly record struct Segment(int StartLine, int EndLine);

    /// <summary>
    /// The segments of <paramref name="source"/>, its declarations section first (when it has any line); none when it must be parsed whole.
    /// </summary>
    /// <param name="source">The module, its lines ending with <c>\n</c>, its directives blank, as the main parse reads it.</param>
    public static IReadOnlyList<Segment>? Scan(string source) => Scan(source, out _);

    /// <summary>
    /// The segments of <paramref name="source"/>, its declarations section first; none when it must be parsed whole, and then <paramref name="giveUp"/> says
    /// why - unless it has no member, which is one segment.
    /// </summary>
    public static IReadOnlyList<Segment>? Scan(string source, out SegmentationStatistics.Path? giveUp)
    {
        var lexer = new VBALexer(new AntlrInputStream(source));
        lexer.RemoveErrorListeners();
        var lineCount = source.Count(character => character == '\n') + 1;

        var headers = new List<int>();
        var ends = new List<int>();
        var inMember = false;
        giveUp = null;

        foreach (var (start, last, tokens) in LogicalLines(lexer.GetAllTokens()))
        {
            switch (KindOf(tokens))
            {
                case Kind.Header when !inMember:
                    headers.Add(start);
                    inMember = true;
                    break;
                case Kind.End when inMember:
                    ends.Add(last);
                    inMember = false;
                    break;
                case Kind.Header:
                    giveUp = SegmentationStatistics.Path.HeaderInAMember;
                    return null;
                case Kind.End:
                    giveUp = SegmentationStatistics.Path.EndOutsideAMember;
                    return null;
                case Kind.Misplaced:
                    giveUp = SegmentationStatistics.Path.Misplaced;
                    return null;

                // code between members, or after the last one: a member's attribute, written after its End, or the module's error, which a parse of the
                // whole module reports. Neither is a segment's.
                case Kind.Code when !inMember && headers.Count > 0:
                    giveUp = SegmentationStatistics.Path.CodeBetweenMembers;
                    return null;
            }
        }

        if (inMember)
        {
            giveUp = SegmentationStatistics.Path.MemberNotEnded;
            return null;
        }

        if (headers.Count == 0)
        {
            return null;
        }

        var segments = new List<Segment>();
        if (headers[0] > 0)
        {
            segments.Add(new Segment(0, headers[0]));
        }

        var from = headers[0];
        for (var member = 0; member < ends.Count; member++)
        {
            var to = member == ends.Count - 1 ? lineCount : ends[member] + 1;
            segments.Add(new Segment(from, to));
            from = to;
        }

        return segments;
    }

    private enum Kind { Blank, Code, Header, End, Misplaced }

    // what a logical line is: nothing but a comment, a member's header, a member's End, other code - or a line that has a header or an End anywhere but as its
    // first statement, which is no line a segment can start or end at.
    private static Kind KindOf(List<IToken> tokens)
    {
        var significant = tokens.Where(token => token.Type is not (VBALexer.WS or VBALexer.LINE_CONTINUATION)).ToList();
        var code = new List<IToken>();
        foreach (var token in significant)
        {
            if (token.Type == VBALexer.SINGLEQUOTE || (token.Type == VBALexer.REM && code.Count == 0))
            {
                break;
            }

            code.Add(token);
        }

        if (code.Count == 0)
        {
            return Kind.Blank;
        }

        var first = code.FindIndex(token => token.Type is not (VBALexer.PUBLIC or VBALexer.PRIVATE or VBALexer.FRIEND or VBALexer.GLOBAL or VBALexer.STATIC));

        // a Declare names the Sub or Function it declares, and is a declaration of the module's.
        if (first >= 0 && code[first].Type == VBALexer.DECLARE)
        {
            return Kind.Code;
        }

        var kind = first < 0 ? Kind.Code
            : code[first].Type is VBALexer.SUB or VBALexer.FUNCTION or VBALexer.PROPERTY_GET or VBALexer.PROPERTY_LET or VBALexer.PROPERTY_SET ? Kind.Header
            : code[first].Type is VBALexer.END_SUB or VBALexer.END_FUNCTION or VBALexer.END_PROPERTY ? Kind.End
            : Kind.Code;

        var elsewhere = code.Where((token, index) => index != first).Any(token => token.Type is VBALexer.SUB or VBALexer.FUNCTION or VBALexer.PROPERTY_GET
            or VBALexer.PROPERTY_LET or VBALexer.PROPERTY_SET or VBALexer.END_SUB or VBALexer.END_FUNCTION or VBALexer.END_PROPERTY);
        return elsewhere ? Kind.Misplaced : kind;
    }

    // the logical lines of the module - its physical lines, those a line-continuation joins as one - each with its first and last physical line, from 0.
    private static IEnumerable<(int Start, int Last, List<IToken> Tokens)> LogicalLines(IList<IToken> tokens)
    {
        var line = new List<IToken>();
        int? start = null;
        foreach (var token in tokens)
        {
            if (token.Type == VBALexer.NEWLINE)
            {
                yield return (start ?? token.Line - 1, token.Line - 1, line);
                (line, start) = ([], null);
                continue;
            }

            start ??= token.Line - 1;
            line.Add(token);
        }

        if (line.Count > 0)
        {
            yield return (start ?? 0, line[^1].Line - 1, line);
        }
    }
}
