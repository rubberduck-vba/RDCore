using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using System.Text.RegularExpressions;

namespace RDCore.Parsing;

/// <summary>
/// What is wrong with the conditional-compilation directives of a module (<strong>MS-VBAL §3.4</strong>), as MS-VBA says it: a <c>#Else</c>, <c>#ElseIf</c>
/// or <c>#End If</c> that belongs to no <c>#If</c>, a second <c>#Else</c> in one block, a block that is never ended, and a directive that is not well formed.
/// </summary>
/// <remarks>
/// <para>
/// The directive pass reads a line it cannot place as text. A directive that is not well formed is no directive for the structure of the blocks: an
/// <c>#If</c> with no <c>Then</c>, one whose expression is no expression, one with text after its <c>Then</c>, an unknown keyword (<c>#Iff</c>), an
/// <c>#End If</c> followed by text. The <c>#End If</c> after such an <c>#If</c> belongs to no block. <c>#EndIf</c>, written as one word, is an
/// <c>#End If</c>.
/// </para>
/// <para>
/// Each directive is checked against the grammar's own rule for it, on its logical line, and a line that is not well formed is a syntax error of its own,
/// at the token that is not what the rule expects: "Expected: Then", "Expected: expression", "Expected: =", "Expected: identifier", "Expected: end of
/// statement", or the list of the directives' keywords. MS-VBA stops at the first error it finds, and finds the structure's before the lines'; every one of
/// them is reported here.
/// </para>
/// </remarks>
internal static partial class DirectiveStructureScanner
{
    /// <summary>A directive that is wrong, where it is, and what MS-VBA says of it.</summary>
    public readonly record struct Finding(SourceRange Range, VBCompileErrorId ErrorId, string Verbose);

    private enum Kind
    {
        If,
        ElseIf,
        Else,
        EndIf,
        Const,
    }

    private sealed record class Block(SourceRange Range)
    {
        public bool HasElse { get; set; }
    }

    // what is wrong with a directive's line: where, as offsets in its text, and what MS-VBA expected there.
    private readonly record struct LineError(int Start, int End, string Verbose);

    /// <summary>
    /// The directives of <paramref name="content"/> that are wrong, in source order.
    /// </summary>
    /// <param name="content">The module's source, its line endings <c>\n</c>.</param>
    /// <param name="directives">Where each directive's logical line is in <paramref name="content"/>, in source order.</param>
    public static IReadOnlyList<Finding> Scan(string content, IEnumerable<(int Index, int Length)> directives)
    {
        var lineStarts = LineStarts(content);
        var findings = new List<Finding>();
        var open = new Stack<Block>();
        foreach (var (index, length) in directives)
        {
            var text = content.Substring(index, length);
            if (KeywordPattern().Match(text) is not { Success: true } keyword)
            {
                // a '#' that starts no directive MS-VBA knows: at the word after it, or where one would be.
                var word = UnknownWordPattern().Match(text);
                findings.Add(new Finding(RangeOf(lineStarts, index + word.Index, index + word.Index + word.Length), VBCompileErrorId.SyntaxError,
                    SDK.Exceptions.VBCompileError_DirectiveExpectedKeyword_Verbose));
                continue;
            }

            var kind = KindOf(keyword.Groups["keyword"].Value);
            if (Check(text, kind) is { } error)
            {
                // a directive that is not well formed is no directive: what it would open, continue or end is not opened, continued or ended.
                findings.Add(new Finding(RangeOf(lineStarts, index + error.Start, index + error.End), VBCompileErrorId.SyntaxError, error.Verbose));
                continue;
            }

            var hash = index + text.IndexOf('#');
            var lineEnd = text.IndexOf('\n') is var newline and >= 0 ? index + newline : index + text.Length;
            var range = RangeOf(lineStarts, hash, lineEnd);
            switch (kind)
            {
                case Kind.If:
                    open.Push(new Block(range));
                    break;

                case Kind.ElseIf when open.Count == 0 || open.Peek().HasElse:
                    findings.Add(new Finding(range, VBCompileErrorId.ElseIfWithoutIf, SDK.Exceptions.VBCompileError_ElseIfWithoutIf_Verbose));
                    break;

                case Kind.Else when open.Count == 0 || open.Peek().HasElse:
                    findings.Add(new Finding(range, VBCompileErrorId.HashElseWithoutIf, SDK.Exceptions.VBCompileError_HashElseWithoutIf_Verbose));
                    break;

                case Kind.Else:
                    open.Peek().HasElse = true;
                    break;

                case Kind.EndIf when open.Count == 0:
                    findings.Add(new Finding(range, VBCompileErrorId.DirectiveWithoutIf, SDK.Exceptions.VBCompileError_DirectiveWithoutIf_Verbose));
                    break;

                case Kind.EndIf:
                    open.Pop();
                    break;
            }
        }

        // each block that is still open is never ended: at its #If, where MS-VBA puts the caret at the module's last line.
        findings.AddRange(open.Reverse().Select(block => new Finding(block.Range, VBCompileErrorId.HashIfWithoutEndIf, SDK.Exceptions.VBCompileError_HashIfWithoutEndIf_Verbose)));
        return [.. findings.OrderBy(finding => finding.Range.Start)];
    }

    private static Kind KindOf(string keyword)
        => keyword.StartsWith("ElseIf", StringComparison.OrdinalIgnoreCase) ? Kind.ElseIf
            : keyword.StartsWith("Else", StringComparison.OrdinalIgnoreCase) ? Kind.Else
            : keyword.StartsWith("End", StringComparison.OrdinalIgnoreCase) ? Kind.EndIf
            : keyword.StartsWith("Const", StringComparison.OrdinalIgnoreCase) ? Kind.Const
            : Kind.If;

    // what is wrong with the directive's line, read by the grammar's rule for it, alone and to its end; null when nothing is.
    private static LineError? Check(string text, Kind kind)
    {
        var tokens = new CommonTokenStream(new VBALexer(new AntlrInputStream(text + "\n")));
        var parser = new VBAConditionalCompilationParser(tokens) { ErrorHandler = new BailErrorStrategy() };
        parser.RemoveErrorListeners();
        parser.Interpreter.PredictionMode = PredictionMode.Ll;
        IToken offending;
        try
        {
            _ = kind switch
            {
                Kind.If => (ParserRuleContext)parser.ccIf(),
                Kind.ElseIf => parser.ccElseIf(),
                Kind.Else => parser.ccElse(),
                Kind.EndIf => parser.ccEndIf(),
                _ => parser.ccConst(),
            };

            if (tokens.La(1) == TokenConstants.Eof)
            {
                return null;
            }

            // the rule is read, and more is written on the line.
            offending = tokens.Lt(1);
            return At(offending, text, SDK.Exceptions.VBCompileError_DirectiveExpectedEndOfStatement_Verbose);
        }
        catch (ParseCanceledException exception) when (exception.InnerException is RecognitionException { OffendingToken: { } token })
        {
            offending = token;
        }

        tokens.Fill();
        var all = tokens.GetTokens();
        var before = all.Take(offending.TokenIndex).LastOrDefault(token => token.Type is not (VBALexer.WS or VBALexer.LINE_CONTINUATION));
        return kind switch
        {
            Kind.If or Kind.ElseIf => ExpectedInCondition(all, offending, before, text),
            Kind.Const => At(offending, text, ExpectedInConst(all, offending, before)),
            _ => At(offending, text, SDK.Exceptions.VBCompileError_DirectiveExpectedEndOfStatement_Verbose),
        };
    }

    // an #If's or an #ElseIf's: where an operand is due - at a Then, after an operator - an expression; before its Then, the Then; past it, the end of the
    // line. A Then the expression read as a name, which the line ends after, is where the expression is due.
    private static LineError ExpectedInCondition(IList<IToken> tokens, IToken offending, IToken? before, string text)
    {
        if (before?.Type == VBALexer.THEN && offending.Type is VBALexer.NEWLINE or TokenConstants.Eof)
        {
            return At(before, text, SDK.Exceptions.VBCompileError_DirectiveExpectedExpression_Verbose);
        }

        var then = tokens.FirstOrDefault(token => token.Type == VBALexer.THEN);
        if (then is not null && offending.TokenIndex > then.TokenIndex)
        {
            return At(offending, text, SDK.Exceptions.VBCompileError_DirectiveExpectedEndOfStatement_Verbose);
        }

        return At(offending, text, offending.Type == VBALexer.THEN || before is null || ExpectsAnOperand(before.Type)
            ? SDK.Exceptions.VBCompileError_DirectiveExpectedExpression_Verbose
            : SDK.Exceptions.VBCompileError_DirectiveExpectedThen_Verbose);
    }

    // a #Const's: right after Const, its name; before its '=', the '='; after it, an expression where an operand is due, and the end of the line after one.
    private static string ExpectedInConst(IList<IToken> tokens, IToken offending, IToken? before)
    {
        var equals = tokens.FirstOrDefault(token => token.Type == VBALexer.EQ);
        if (equals is not null && offending.TokenIndex > equals.TokenIndex)
        {
            return before is null || ExpectsAnOperand(before.Type)
                ? SDK.Exceptions.VBCompileError_ConstDirectiveExpression_Verbose
                : SDK.Exceptions.VBCompileError_DirectiveExpectedEndOfStatement_Verbose;
        }

        return before?.Type == VBALexer.CONST
            ? SDK.Exceptions.VBCompileError_ConstDirectiveExpectedName_Verbose
            : SDK.Exceptions.VBCompileError_ConstDirectiveExpectedEquals_Verbose;
    }

    // what an expression goes on after: an operator, an opening parenthesis, the keyword that opens the condition.
    private static bool ExpectsAnOperand(int type)
        => type is VBALexer.IF or VBALexer.ELSEIF or VBALexer.LPAREN or VBALexer.PLUS or VBALexer.MINUS or VBALexer.MULT or VBALexer.DIV or VBALexer.INTDIV
            or VBALexer.POW or VBALexer.MOD or VBALexer.AMPERSAND or VBALexer.EQ or VBALexer.NEQ or VBALexer.LT or VBALexer.GT or VBALexer.LEQ or VBALexer.GEQ
            or VBALexer.LIKE or VBALexer.IS or VBALexer.NOT or VBALexer.AND or VBALexer.OR or VBALexer.XOR or VBALexer.EQV or VBALexer.IMP;

    // the token's span in the directive's text; the end of the line, empty, for the line's end.
    private static LineError At(IToken token, string text, string verbose)
        => token.Type is TokenConstants.Eof or VBALexer.NEWLINE || token.StartIndex >= text.Length
            ? new LineError(Math.Min(token.StartIndex, text.Length), Math.Min(token.StartIndex, text.Length), verbose)
            : new LineError(token.StartIndex, Math.Min(token.StopIndex + 1, text.Length), verbose);

    // where each line starts, once for the module.
    private static int[] LineStarts(string content)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }

    private static SourceRange RangeOf(int[] lineStarts, int start, int end) => new(PositionOf(lineStarts, start), PositionOf(lineStarts, end));

    private static SourcePosition PositionOf(int[] lineStarts, int offset)
    {
        var line = Array.BinarySearch(lineStarts, offset);
        line = line >= 0 ? line : ~line - 1;
        return new SourcePosition(line, offset - lineStarts[line]);
    }

    // the '#' may stand apart from its keyword, and End from If, over spaces and line-continuations.
    [GeneratedRegex(@"^[ \t]*#(?:[ \t]|_[ \t]*\n)*(?<keyword>ElseIf|Else|End(?:[ \t]|_[ \t]*\n)*If|Const|If)\b", RegexOptions.IgnoreCase)]
    private static partial Regex KeywordPattern();

    // the word after a '#' that starts no known directive, or nothing, where it would be.
    [GeneratedRegex(@"(?<=^[ \t]*#(?:[ \t]|_[ \t]*\n)*)\w*")]
    private static partial Regex UnknownWordPattern();
}
