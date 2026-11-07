using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;

namespace RDCore.Parsing.Syntax;

/// <summary>
/// ANTLR's recovery from a syntax error, which never reads past the end of the statement the error is in: it deletes no line end, colon, comment or end of
/// the module, and stops at one where it skips tokens to resynchronize.
/// </summary>
/// <remarks>
/// After <c>a = 1 +</c>, the parse expects an operand, and finds the line end. ANTLR's default recovery deletes it as an extraneous token when the token after
/// it is one the parse expects, and reads the next line as the rest of the expression: <c>a = 1 +</c> then <c>b = 2</c> would parse as <c>a = (1 + b) = 2</c>,
/// with no node for the next statement. This strategy ends the statement with the error at its own end, and reads the next one on its own. A parse with no
/// syntax error never enters recovery, so this strategy does not change it.
/// <para>
/// The header of a <c>For</c> or a <c>For Each</c> that ends with a binary operator the parse leaves unread (<c>For i = 1 To 2 -</c>) is reported at the
/// operator, which is a token no rule reads, and the loop goes on to its body and its <c>Next</c>.
/// </para>
/// </remarks>
internal sealed class StatementBoundErrorStrategy : DefaultErrorStrategy
{
    // what ends a statement, as recovery sees it: the end of a statement, a comment's included, or the end of the module.
    private static bool EndsAStatement(int type) => BeginsAnEndOfStatement(type) || type == TokenConstants.Eof;

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

    // a For's header that ends with a binary operator the parse leaves unread (For i = 1 To 2 -): the decision after its last expression looks past the
    // operator to the end of the line, and fails, and the For would end there, with no body and no Next: the procedure's recovery would then skip to the
    // next procedure. The operator is a token no rule reads, reported where it is, and the For goes on to its body.
    public override void Sync(Parser recognizer)
    {
        if (recognizer is VBAParser { Context: VBAParser.ForNextStmtContext or VBAParser.ForEachStmtContext } parser && !InErrorRecoveryMode(parser)
            && DanglingOperatorAhead(parser) is { } through)
        {
            var stream = (ITokenStream)parser.InputStream;
            var @operator = stream.Lt(through);
            BeginErrorCondition(parser);
            parser.NotifyErrorListeners(@operator,
                $"extraneous input {GetTokenErrorDisplay(@operator)} expecting {GetExpectedTokens(parser).ToString(parser.Vocabulary)}", null);
            for (var i = 0; i < through; i++)
            {
                parser.Consume();
            }

            EndErrorCondition(parser);
        }

        base.Sync(recognizer);
    }

    // how far ahead, past the blanks, a binary operator is that the parse does not expect here, when the end of the statement follows it; none otherwise.
    private static int? DanglingOperatorAhead(Parser parser)
    {
        var stream = (ITokenStream)parser.InputStream;
        var at = PastTheBlanks(stream, 1);
        if (!ModuleParser.BinaryOperators.Contains(stream.La(at)) || parser.GetExpectedTokens().Contains(stream.La(at)))
        {
            return null;
        }

        return EndsAStatement(stream.La(PastTheBlanks(stream, at + 1))) ? at : null;
    }

    private static int PastTheBlanks(ITokenStream stream, int at)
    {
        while (stream.La(at) is VBALexer.WS or VBALexer.LINE_CONTINUATION)
        {
            at++;
        }

        return at;
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
