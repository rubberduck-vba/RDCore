using Antlr4.Runtime;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Formatting;

namespace RDCore.Tests.Formatting;

/// <summary>
/// What of a module no formatting edit may change, as the VBA lexer reads it: what is inside a string literal or a date literal, and the text of a comment -
/// from its apostrophe or its <c>Rem</c> to the end of each of its lines, what it is continued on included.
/// </summary>
internal static class LiteralText
{
    private readonly record struct Span(int Line, int Start, int End);

    /// <summary>Fails when an edit replaces, or inserts into, what a string literal, a date literal or a comment says.</summary>
    public static void AssertNoEditIn(string text, IReadOnlyList<IndentationEdit> edits)
    {
        var lines = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        foreach (var span in SpansOf(text, lines))
        {
            foreach (var edit in edits.Where(edit => edit.Line <= span.Line && span.Line <= edit.EndLine))
            {
                var from = edit.Line == span.Line ? edit.Character : 0;
                var to = edit.EndLine == span.Line ? edit.EndCharacter : int.MaxValue;
                var touches = from == to ? span.Start < from && from < span.End : from < span.End && to > span.Start;
                Assert.IsFalse(touches, $"an edit ({edit}) changes '{lines[span.Line][span.Start..span.End]}', on line {span.Line + 1}.");
            }
        }
    }

    private static IEnumerable<Span> SpansOf(string text, string[] lines)
    {
        var lexer = new VBALexer(new AntlrInputStream(text));
        lexer.RemoveErrorListeners();
        var inComment = false;
        foreach (var token in lexer.GetAllTokens())
        {
            var line = token.Line - 1;
            if (inComment)
            {
                inComment = token.Type != VBALexer.NEWLINE;
                if (token.Type == VBALexer.LINE_CONTINUATION && line + 1 < lines.Length && lines[line + 1].Trim().Length > 0)
                {
                    // what a comment is continued on is its text, from where it starts on its line.
                    var next = lines[line + 1];
                    yield return new Span(line + 1, next.Length - next.TrimStart().Length, next.TrimEnd().Length);
                }

                continue;
            }

            switch (token.Type)
            {
                case VBALexer.STRINGLITERAL or VBALexer.DATELITERAL when token.Text.IndexOfAny(['\r', '\n']) < 0:
                    yield return new Span(line, token.Column + 1, token.Column + token.Text.Length - 1);
                    break;
                case VBALexer.SINGLEQUOTE or VBALexer.REM:
                    inComment = true;
                    yield return new Span(line, token.Column, lines[line].TrimEnd().Length);
                    break;
            }
        }
    }
}
