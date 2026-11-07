using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;

namespace RDCore.Parsing.Syntax;

/// <summary>
/// ANTLR's recovery from a syntax error, which never reads past the end of the statement the error is in: it deletes no line end, colon or end of the module,
/// and stops at one where it skips tokens to resynchronize.
/// </summary>
/// <remarks>
/// After <c>a = 1 +</c>, the parse expects an operand, and finds the line end. ANTLR's recovery deletes it as an extraneous token when the token after it is one
/// the parse expects, and reads the next line as the rest of the expression: <c>a = 1 +</c> then <c>b = 2</c> was <c>a = (1 + b) = 2</c>, and the next
/// statement was lost to the tree. Here the statement with the error ends at its own end, and the next one is read on its own. A parse with no syntax error
/// never recovers, and is the parse it was.
/// </remarks>
internal sealed class StatementBoundErrorStrategy : DefaultErrorStrategy
{
    private static bool EndsAStatement(int type) => type is VBALexer.NEWLINE or VBALexer.COLON or TokenConstants.Eof;

    protected override IToken SingleTokenDeletion(Parser recognizer)
        => EndsAStatement(((ITokenStream)recognizer.InputStream).La(1)) ? null! : base.SingleTokenDeletion(recognizer);

    // whether the error the parse recovers from is one it reported: no error is reported until a token is read after the one it recovered from.
    private bool _reported;

    public override void ReportError(Parser recognizer, RecognitionException e)
    {
        _reported = !InErrorRecoveryMode(recognizer);
        base.ReportError(recognizer, e);
    }

    // what a statement's end begins with, but the end of the module: a line end, a colon, a comment.
    private static bool BeginsAnEndOfStatement(int type) => type is VBALexer.NEWLINE or VBALexer.COLON or VBALexer.SINGLEQUOTE or VBALexer.REM;

    // where the end of a statement was expected and something else is written (a = 1 -: the parse reads a = 1, and the - is no end of it), what is left of
    // the line is tokens no rule reads, and the end of the statement is read after them, as it is written, its comment included. Without it, the statements
    // of the block would end there, and what follows the block would be expected on the next line: the end of the procedure, after which the rest of the
    // procedure would be skipped.
    public override void Recover(Parser recognizer, RecognitionException e)
    {
        var reported = _reported;
        base.Recover(recognizer, e);
        if (recognizer is not VBAParser { Context: VBAParser.EndOfStatementContext } parser)
        {
            return;
        }

        // only an error of the statement's own, after a statement read to its end: one the parse does not report follows an error it recovered from before it -
        // in a block it left at the error, in the procedure before the next one - and what follows is not the rest of the statement's line.
        if (!reported)
        {
            return;
        }

        var stream = (ITokenStream)parser.InputStream;

        while (stream.La(1) != TokenConstants.Eof && !BeginsAnEndOfStatement(stream.La(1)))
        {
            parser.Consume();
        }

        // read from where endOfStatement reads it, as the rule's own code does: a full-context prediction in it goes back to the rule through that state.
        while (BeginsAnEndOfStatement(stream.La(1)))
        {
            parser.State = ReadsAnEndOfStatement;
            parser.individualNonEOFEndOfStatement();
        }
    }

    // the state of endOfStatement whose transition is the rule that reads one end of a statement.
    private static readonly int ReadsAnEndOfStatement = StateOfTheRuleTransition(VBAParser.RULE_endOfStatement, VBAParser.RULE_individualNonEOFEndOfStatement);

    private static int StateOfTheRuleTransition(int rule, int called)
    {
        var atn = VBAParser._ATN;
        var start = atn.ruleToStartState[called];
        foreach (var state in atn.states)
        {
            if (state.ruleIndex != rule)
            {
                continue;
            }

            for (var i = 0; i < state.NumberOfTransitions; i++)
            {
                if (state.Transition(i) is RuleTransition transition && transition.target == start)
                {
                    return state.stateNumber;
                }
            }
        }

        throw new InvalidOperationException($"No state of rule {rule} calls rule {called}.");
    }

    protected override void ConsumeUntil(Parser recognizer, IntervalSet set)
    {
        var stream = (ITokenStream)recognizer.InputStream;
        for (var type = stream.La(1); !EndsAStatement(type) && !set.Contains(type); type = stream.La(1))
        {
            recognizer.Consume();
        }
    }
}
