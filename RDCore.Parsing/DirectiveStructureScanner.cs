using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using System.Text.RegularExpressions;

namespace RDCore.Parsing;

/// <summary>
/// What is wrong with the conditional-compilation directives of a module (<strong>MS-VBAL §3.4</strong>): a <c>#Else</c>, <c>#ElseIf</c> or <c>#End If</c> that
/// belongs to no <c>#If</c>, a second <c>#Else</c> in one block, a block that is never ended, and a <c>#Const</c> with no name or no <c>=</c>.
/// </summary>
/// <remarks>
/// <para>
/// The directive pass reads a line it cannot place as text, so a directive that is not well formed is read as no directive at all - as MS-VBA reads it:
/// <c>#If VBA7</c> with no <c>Then</c>, an <c>#If</c> whose expression is no expression, text after its <c>Then</c>, an unknown keyword (<c>#Iff</c>),
/// and an <c>#End If</c> followed by text are no directives, so the <c>#End If</c> after such an <c>#If</c> belongs to none. <c>#EndIf</c>, written as one
/// word, is an <c>#End If</c>.
/// </para>
/// <para>
/// Each directive is checked against the grammar's own rule for it, on its logical line: what is well formed is what the directive pass reads.
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

    /// <summary>
    /// The directives of <paramref name="content"/> that are wrong, in source order.
    /// </summary>
    /// <param name="content">The module's source, its line endings <c>\n</c>.</param>
    /// <param name="directives">Where each directive's logical line is in <paramref name="content"/>.</param>
    public static IReadOnlyList<Finding> Scan(string content, IEnumerable<(int Index, int Length)> directives)
    {
        var findings = new List<Finding>();
        var open = new Stack<Block>();
        foreach (var (index, length) in directives)
        {
            var text = content.Substring(index, length);
            if (KeywordPattern().Match(text) is not { Success: true } keyword)
            {
                continue;
            }

            var kind = KindOf(keyword.Groups["keyword"].Value);
            var range = RangeOf(content, index, text);
            if (kind is Kind.Const)
            {
                if (ConstError(text) is { } constError)
                {
                    findings.Add(new Finding(range, VBCompileErrorId.SyntaxError, constError));
                }

                continue;
            }

            // a directive that is not well formed is no directive: what it would open, continue or end is not opened, continued or ended.
            if (!IsWellFormed(text, kind))
            {
                continue;
            }

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

        // each block that is still open is never ended: at its #If.
        findings.AddRange(open.Reverse().Select(block => new Finding(block.Range, VBCompileErrorId.HashIfWithoutEndIf, SDK.Exceptions.VBCompileError_HashIfWithoutEndIf_Verbose)));
        return [.. findings.OrderBy(finding => finding.Range.Start)];
    }

    private static Kind KindOf(string keyword)
        => keyword.StartsWith("ElseIf", StringComparison.OrdinalIgnoreCase) ? Kind.ElseIf
            : keyword.StartsWith("Else", StringComparison.OrdinalIgnoreCase) ? Kind.Else
            : keyword.StartsWith("End", StringComparison.OrdinalIgnoreCase) ? Kind.EndIf
            : keyword.StartsWith("Const", StringComparison.OrdinalIgnoreCase) ? Kind.Const
            : Kind.If;

    // a #Const is "#Const name = expression": with no '=' MS-VBA expects one, and with no name before it a name.
    private static string? ConstError(string text)
    {
        if (IsWellFormed(text, Kind.Const))
        {
            return null;
        }

        var equals = text.IndexOf('=');
        return equals < 0 ? SDK.Exceptions.VBCompileError_ConstDirectiveExpectedEquals_Verbose
            : ConstNamePattern().IsMatch(text[..equals]) ? SDK.Exceptions.VBCompileError_ConstDirectiveExpression_Verbose
            : SDK.Exceptions.VBCompileError_ConstDirectiveExpectedName_Verbose;
    }

    // whether the directive is what the grammar's rule for it reads, alone on its logical line, to its end.
    private static bool IsWellFormed(string text, Kind kind)
    {
        var tokens = new CommonTokenStream(new VBALexer(new AntlrInputStream(text + "\n")));
        var parser = new VBAConditionalCompilationParser(tokens) { ErrorHandler = new BailErrorStrategy() };
        parser.RemoveErrorListeners();
        parser.Interpreter.PredictionMode = PredictionMode.Ll;
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
            return tokens.La(1) == TokenConstants.Eof;
        }
        catch (ParseCanceledException)
        {
            return false;
        }
    }

    // the directive's first physical line, from its '#'.
    private static SourceRange RangeOf(string content, int index, string text)
    {
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < index; i++)
        {
            if (content[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        var hash = index + text.IndexOf('#');
        var end = text.IndexOf('\n') is var newline and >= 0 ? index + newline : index + text.Length;
        return new SourceRange(new SourcePosition(line, hash - lineStart), new SourcePosition(line, end - lineStart));
    }

    // the '#' may stand apart from its keyword, and End from If, over spaces and line-continuations.
    [GeneratedRegex(@"^[ \t]*#(?:[ \t]|_[ \t]*\n)*(?<keyword>ElseIf|Else|End(?:[ \t]|_[ \t]*\n)*If|Const|If)\b", RegexOptions.IgnoreCase)]
    private static partial Regex KeywordPattern();

    [GeneratedRegex(@"#[ \t]*Const[ \t]+[A-Za-z_\[]", RegexOptions.IgnoreCase)]
    private static partial Regex ConstNamePattern();
}
