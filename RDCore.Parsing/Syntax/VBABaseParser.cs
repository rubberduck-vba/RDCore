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
