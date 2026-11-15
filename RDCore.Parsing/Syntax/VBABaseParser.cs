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
    // the lExpression rule has whitespace in three places: a line continuation (mandatoryLineContinuation, and the whitespace whitespaceIndexExpr allows after
    // one), the blanks before an argument list (whitespaceIndexExpr), and the whitespace that begins the output list of objectPrintExpr. After the `.Print` of an
    // object, objectPrintExpr is the earlier alternative, so whitespace there, a line continuation included, begins an output list - which is how
    // objectPrintExpr reads `obj.Print _` with `(1)` on the next line, and `obj.Print (1) = 2` as the Print of a comparison - unless what follows it is the `=` of
    // the Let or the end of the statement. A `.Print` with nothing before it is the member of a With block's object (withMemberAccessExpr), which has no output
    // list: with `(1) = 5` after a line continuation, it is the Let it reads as. The scan stops at a colon, a comment or the end of the line, so in a single-line
    // If it can go on past an Else, which does no harm: the `=` of a Let comes before it.
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
        // a line continuation is the blank right before its underscore: the blanks written before that one are whitespace tokens ahead of it.
        var continuation = i;
        while (TokenTypeAt(continuation) == VBALexer.WS)
        {
            continuation++;
        }
        if (TokenTypeAt(continuation) == VBALexer.LINE_CONTINUATION)
        {
            i = continuation + 1;
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

    // MS-VBAL §5.4.1.1: a name followed by a colon at the start of a line is a statement label, never a call of a procedure by that name (MS-VBA, observed in Excel:
    // `PrintGreeting:` at the start of a line calls nothing). Without this, a block took a label right before the line
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

    // MS-VBAL §5.3.1: a procedure's header is no statement of the procedure before it, which is missing its End Sub: the statements of that procedure end
    // there. Read as a statement, `Public Sub B()` would be a declaration (`Public` declares variables), refused, and the procedure would go on with B's
    // statements.
    protected bool IsProcedureHeaderAhead() => BeginsAProcedureHeader(_input);

    // a procedure's header, next in the stream: at the start of a line, its modifiers (Public, Private, Friend, Global, then Static), then Sub, Function or
    // Property Get, Let or Set.
    internal static bool BeginsAProcedureHeader(ITokenStream stream)
    {
        var at = 1;
        if (stream.La(at) is VBALexer.PRIVATE or VBALexer.PUBLIC or VBALexer.FRIEND or VBALexer.GLOBAL)
        {
            at = PastTheBlanks(stream, at + 1);
        }

        if (stream.La(at) == VBALexer.STATIC)
        {
            at = PastTheBlanks(stream, at + 1);
        }

        if (stream.La(at) is not (VBALexer.SUB or VBALexer.FUNCTION or VBALexer.PROPERTY_GET or VBALexer.PROPERTY_LET or VBALexer.PROPERTY_SET))
        {
            return false;
        }

        // the token before it, as an index of the stream: the next token's is the stream's.
        var before = stream.Index - 1;
        while (before >= 0 && stream.Get(before).Type == VBALexer.WS)
        {
            before--;
        }

        return before < 0 || stream.Get(before).Type is VBALexer.NEWLINE or VBALexer.LINE_CONTINUATION;
    }

    private static int PastTheBlanks(ITokenStream stream, int at)
    {
        while (stream.La(at) is VBALexer.WS or VBALexer.LINE_CONTINUATION)
        {
            at++;
        }

        return at;
    }

    // MS-VBAL §5.4.5.8.1: at the start of an output clause, `Tab` is a tab clause and `Spc` followed by `(` a spc clause, never an expression that calls a name
    // Spc or Tab. The VBE writes `Spc(1)(2)` as `Spc(1); (2)`. Without this, the argument list of a name read `Spc(1)(2)` as one
    // expression, an index of `Spc(1)`, when what follows the clause was no output item it could be.
    protected bool IsSpcOrTabClauseAhead()
    {
        switch (TokenTypeAtRelativePosition(1))
        {
            case VBALexer.TAB:
                return true;
            case VBALexer.SPC:
                var offset = 2;
                while (TokenTypeAtRelativePosition(offset) is VBALexer.WS or VBALexer.LINE_CONTINUATION)
                {
                    offset++;
                }
                return TokenTypeAtRelativePosition(offset) == VBALexer.LPAREN;
            default:
                return false;
        }
    }

    #endregion

    #region Precedence of an l-expression
    // MS-VBAL §3.3: a token takes the white space before it, so blanks between an l-expression and `(` leave the parentheses its argument list, and
    // `x = F (2)` calls F with 2, as MS-VBA reads it. In a list to print, the blanks are no separator of two output items: `Debug.Print F (2)` calls F too.
    // They are read otherwise right after the callee of a call statement, where `F (2)` is F called with the argument (2), and after the object of a Line
    // statement, where `Line (0, 0) - (1, 1)` begins with its first point.
    // That reading is lExpression's last alternative, of this precedence, the lowest. As such a callee, lExpression is entered above it, and the alternative
    // is left out of each of its predictions before a token is read: a predicate of its own would be evaluated only once the prediction had read the
    // statement to its end, and `Foo (Bar(1))` would be read to its end to tell a call statement from an index.
    private const int BlankArgumentListPrecedence = 1;

    public override void EnterRecursionRule(ParserRuleContext localctx, int state, int ruleIndex, int precedence)
        => base.EnterRecursionRule(localctx, state, ruleIndex,
            ruleIndex == VBAParser.RULE_lExpression && IsCallee(localctx) ? Math.Max(precedence, BlankArgumentListPrecedence + 1) : precedence);

    // whether an l-expression is what a call statement without Call calls, or the object and the word of a Line statement: with Call, `Call F (2)` calls F
    // with 2.
    private static bool IsCallee(RuleContext context)
        => context.Parent is VBAParser.CallStmtContext call && call.CALL() is null || context.Parent is VBAParser.LineSpecialFormContext;
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

    // Bounds recovery to the rest of the current logical line (NEWLINE/COLON/EOF), or to a comment on
    // it, which is the statement's end and read as one - for a rule whose failure can't corrupt
    // anything beyond a single statement (e.g. mainBlockStmt's own alternatives).
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
            if (la is -1 or VBALexer.NEWLINE or VBALexer.COLON or VBALexer.SINGLEQUOTE or VBALexer.REM)
            {
                break;
            }
            Consume();
        }
        return true;
    }

    // Bounds recovery to the procedure's boundary, for a rule whose body can span many lines (Sub/Function/Property), where stopping at just the next newline
    // would still leave the parser trying to resume mid-procedure. The procedure ends at its End Sub (End Function, End Property), which is read as its own,
    // or before the next procedure's header, its modifiers included (`Public Sub Foo()`), when it has none: the procedure after it is read on its own.
    protected bool RecoverToProcedureBoundary(RecognitionException re)
    {
        if (ErrorHandler is BailErrorStrategy)
        {
            return false;
        }
        ErrorHandler.ReportError(this, re);
        while (_input.La(1) != TokenConstants.Eof && !BeginsAProcedureHeader(_input))
        {
            var ends = _input.La(1) is VBALexer.END_SUB or VBALexer.END_FUNCTION or VBALexer.END_PROPERTY;
            Consume();
            if (ends)
            {
                break;
            }
        }
        return true;
    }
    #endregion
}
