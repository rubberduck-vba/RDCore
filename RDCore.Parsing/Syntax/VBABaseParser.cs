using Antlr4.Runtime;
using System.Text.RegularExpressions;

namespace RDCore.Parsing.Syntax;

public abstract class VBABaseParser : Parser
{
    public VBABaseParser(ITokenStream input) : base(input) { }

    #region Semantic predicate helper methods
    protected int TokenTypeAtRelativePosition(int i)
    {
        return _input.La(i);
    }

    protected IToken TokenAtRelativePosition(int i)
    {
        return _input.Lt(i);
    }

    protected string TextOf(IToken token)
    {
        return token.Text;
    }

    protected bool MatchesRegex(string text, string pattern)
    {
        return Regex.Match(text,pattern).Success;
    }

    protected bool EqualsStringIgnoringCase(string actual, string expected)
    {
        return actual.Equals(expected,StringComparison.OrdinalIgnoreCase);
    }

    protected bool EqualsStringIgnoringCase(string actual, params string[] expectedOptions)
    {
        foreach (string expected in expectedOptions)
        {
            if (actual.Equals(expected,StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    protected bool EqualsString(string actual, string expected)
    {
        return actual.Equals(expected,StringComparison.Ordinal);
    }

    protected bool EqualsString(string actual, params string[] expectedOptions)
    {
        foreach (string expected in expectedOptions)
        {
            if (actual.Equals(expected,StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    // Whether a token begins a name: an identifier, a keyword (which a name may be, such as Name, Date or Open) or the bracket of a foreign name.
    // What a type hint is immediately followed by, with no whitespace between, is what decides whether it is a type hint at all.
    protected bool IsNameStart(IToken token)
    {
        return token.Text is { Length: > 0 } text && (char.IsLetter(text[0]) || text[0] == '_' || text[0] == '[');
    }

    // Whether the statement that begins with the next token is the Print of an object with an output list (`Debug.Print x = 0`): an output list is no part of an
    // l-expression, so that is never the target of a Let. Only the tokens before the first `=` outside parentheses and brackets can be the target. Outside them,
    // the lExpression rule has whitespace in two places only: a line continuation (mandatoryLineContinuation, and the whitespace whitespaceIndexExpr allows after
    // one), and the whitespace that begins the output list of objectPrintExpr. So whitespace after the `.Print` of an object, a line continuation included, begins
    // an output list - which is how objectPrintExpr reads `obj.Print _` with `(1)` on the next line - unless what follows it is the `=` of the Let or the end of the
    // statement. A `.Print` with nothing before it is the member of a With block's object (withMemberAccessExpr), which has no output list: with `(1) = 5` after a
    // line continuation, it is the Let it reads as. The scan stops at a colon, a comment or the end of the line, so in a single-line If it can go on past an Else,
    // which does no harm: the `=` of a Let comes before it.
    // Should lExpression have whitespace anywhere else outside parentheses, this has to know of it.
    protected bool IsObjectPrintWithOutputList()
    {
        // the tokens are read by their index in the stream, and not with La(i), which walks i tokens on each call and would make the scan quadratic in the length
        // of the statement. Get reads only the tokens the stream holds, so it is filled first, once: after that, Fill does nothing.
        (_input as BufferedTokenStream)?.Fill();
        var start = _input.Index;
        var depth = 0;
        for (var i = start; !IsStatementEnd(TokenTypeAt(i)); i++)
        {
            switch (TokenTypeAt(i))
            {
                case VBALexer.LPAREN:
                case VBALexer.L_SQUARE_BRACKET:
                    depth++;
                    break;
                case VBALexer.RPAREN:
                case VBALexer.R_SQUARE_BRACKET:
                    depth--;
                    break;
                case VBALexer.EQ when depth == 0:
                    return false;
                case VBALexer.DOT when depth == 0 && i > start && IsPrintWithOutputList(i + 1):
                    return true;
            }
        }
        return false;
    }

    // `i` is the index of the token after a dot.
    private bool IsPrintWithOutputList(int i)
    {
        if (TokenTypeAt(i) == VBALexer.LINE_CONTINUATION)
        {
            i++;
            while (TokenTypeAt(i) == VBALexer.WS)
            {
                i++;
            }
        }
        if (TokenTypeAt(i) != VBALexer.PRINT)
        {
            return false;
        }

        var whiteSpaceStart = ++i;
        while (TokenTypeAt(i) is VBALexer.WS or VBALexer.LINE_CONTINUATION)
        {
            i++;
        }
        var next = TokenTypeAt(i);
        return i > whiteSpaceStart && next != VBALexer.EQ && !IsStatementEnd(next);
    }

    // the type of the token at an index of the stream, and EOF past the end of what it holds.
    private int TokenTypeAt(int index)
        => index < _input.Size ? _input.Get(index).Type : TokenConstants.Eof;

    private static bool IsStatementEnd(int tokenType)
        => tokenType is -1 or VBALexer.NEWLINE or VBALexer.COLON or VBALexer.SINGLEQUOTE or VBALexer.REM;

    protected bool IsTokenType(int actual, params int[] expectedOptions)
    {
        foreach (int expected in expectedOptions)
        {
            if (actual == expected)
            {
                return true;
            }
        }
        return false;
    }

    // MS-VBAL §5.4.1.1: a statement label is defined at the LINE-START of a logical line - and whitespace is no token of the syntactic grammar (§3.3), so
    // a label indented by it is at the start of its line all the same, as MS-VBA has it (Excel-verified). The VBE moves a label to the first column; a
    // module written anywhere else may not.
    protected bool IsAtLineStart()
    {
        var offset = -1;
        while (TokenTypeAtRelativePosition(offset) == VBALexer.WS)
        {
            offset--;
        }

        return IsTokenType(TokenTypeAtRelativePosition(offset), VBALexer.NEWLINE, VBALexer.LINE_CONTINUATION);
    }

    // MS-VBAL §5.4.1.1: a name followed by a colon at the start of a line is a statement label, never a call of a procedure by that name (MS-VBA, Excel-
    // verified, oracle\findings-labels L20: `PrintGreeting:` at the start of a line calls nothing). Without this, a block took a label right before the line
    // that ends it (`Fin: End If`) for a call of `Fin`, since a label alone needs an end of statement after it, and the block's end is none.
    protected bool IsLabelAhead()
    {
        if (!IsAtLineStart())
        {
            return false;
        }

        var offset = 2;
        while (TokenTypeAtRelativePosition(offset) == VBALexer.WS)
        {
            offset++;
        }

        return TokenTypeAtRelativePosition(offset) == VBALexer.COLON;
    }
    #endregion

    #region Grammar-level recovery helpers
    // ANTLR's own follow-set-based recovery (DefaultErrorStrategy.GetErrorRecoverySet) is computed by
    // walking the current rule-invocation stack; for a rule whose own alternative-selection needs
    // unbounded lookahead to fully verify (e.g. disambiguating a multi-line If from a single-line one,
    // or matching an argument list's closing paren), a syntax error deep inside can make ANTLR's
    // adaptive prediction fail at the OUTER decision point instead of within the failing construct
    // itself - and the resulting exception then cascades through every enclosing rule's own recovery
    // attempt, each too narrow to find anything to resynchronize on, until it reaches the outermost
    // rule and consumes the rest of the file. A grammar-level `catch` clause on the specific rule where
    // this occurs intercepts it before ANTLR's default (too-narrow) recovery runs, bounding the damage
    // to a caller-appropriate scope instead. Both helpers defer entirely to the active BailErrorStrategy
    // when one is installed (ModuleParser's SLL fast-path) - returning false there tells the generated
    // catch-block to just rethrow, so that pass's bail-immediately contract is unaffected.

    // Bounds recovery to the rest of the current logical line (NEWLINE/COLON/EOF) - for a rule whose
    // failure can't corrupt anything beyond a single statement (e.g. mainBlockStmt's own alternatives).
    protected bool RecoverToStatementBoundary(RecognitionException re)
    {
        if (ErrorHandler is BailErrorStrategy)
        {
            return false;
        }
        ErrorHandler.ReportError(this, re);
        while (true)
        {
            int la = _input.La(1);
            if (la == -1 || la == VBALexer.NEWLINE || la == VBALexer.COLON)
            {
                break;
            }
            Consume();
        }
        return true;
    }

    // Bounds recovery to the next recognizable procedure boundary - for a rule whose body can span many
    // lines (Sub/Function/Property), where stopping at just the next newline would still leave the
    // parser trying to resume mid-procedure. Stops on a visibility/STATIC modifier too, so a qualified
    // declaration (`Public Sub Foo()`) isn't itself partially consumed as recovery fodder.
    protected bool RecoverToProcedureBoundary(RecognitionException re)
    {
        if (ErrorHandler is BailErrorStrategy)
        {
            return false;
        }
        ErrorHandler.ReportError(this, re);
        while (true)
        {
            int la = _input.La(1);
            if (la == -1
                || la == VBALexer.SUB || la == VBALexer.FUNCTION
                || la == VBALexer.PROPERTY_GET || la == VBALexer.PROPERTY_LET || la == VBALexer.PROPERTY_SET
                || la == VBALexer.PUBLIC || la == VBALexer.PRIVATE || la == VBALexer.FRIEND || la == VBALexer.STATIC)
            {
                break;
            }
            Consume();
        }
        return true;
    }
    #endregion
}
