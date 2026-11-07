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
/// with no node for the next statement. This strategy ends the statement with the error at its own end, and reads the next one on its own.
/// <para>
/// An error inside a statement stays in it too. The rest of the line after the error is read as the statement's, so the block goes on after it
/// (<c>Dim For As Long</c>, where the name is recovered from before <c>As</c>). A statement that begins with a word no statement begins with is read as a
/// statement no rule reads, to its end (<c>Mod = 1</c>). A word that ends a block or begins a procedure is still recovered from where it is.
/// </para>
/// A parse with no syntax error never enters recovery, so this strategy does not change it.
/// </remarks>
internal sealed class StatementBoundErrorStrategy : DefaultErrorStrategy
{
    // what ends a statement, as recovery sees it: the end of a statement, a comment's included, or the end of the module.
    internal static bool EndsAStatement(int type) => BeginsAnEndOfStatement(type) || type == TokenConstants.Eof;

    protected override IToken SingleTokenDeletion(Parser recognizer)
        => EndsAStatement(((ITokenStream)recognizer.InputStream).La(1)) ? null! : base.SingleTokenDeletion(recognizer);


    // whether the error the parse recovers from is one it reported: no error is reported until a token is read after the one it recovered from.
    private bool _reported;

    public override void ReportError(Parser recognizer, RecognitionException e)
    {
        _reported = !InErrorRecoveryMode(recognizer);
        Reporting(recognizer, e.OffendingToken);
        base.ReportError(recognizer, e);
    }

    protected override void ReportUnwantedToken(Parser recognizer)
    {
        Reporting(recognizer);
        base.ReportUnwantedToken(recognizer);
    }

    protected override void ReportMissingToken(Parser recognizer)
    {
        Reporting(recognizer);
        base.ReportMissingToken(recognizer);
    }

    // the index of the token the last error the parse reported is at; -1 before the first.
    private int _lastReportedAt = -1;

    // an error is reported at the token it is at (the next one, for a token missing or unwanted), unless the parse is still recovering from one, which it
    // reports nothing more of.
    private void Reporting(Parser recognizer, IToken? at = null)
    {
        if (!InErrorRecoveryMode(recognizer))
        {
            _lastReportedAt = at is { TokenIndex: >= 0 } ? at.TokenIndex : ((ITokenStream)recognizer.InputStream).Index;
        }
    }

    // whether the last error reported is in the statement whose end is read, on its one line: the error is at or after the statement's first token, and no end
    // of a statement is between that token and the next one. A block whose end is missing (Do While x, then End Sub where its Loop is) is a statement of
    // several lines, and the error at the end of it is no part of a line of its own: the token it is at is what follows the block.
    private bool InTheStatementItEnds(VBAParser parser, VBAParser.EndOfStatementContext end)
    {
        var stream = (ITokenStream)parser.InputStream;
        var start = StartOfTheStatementBefore(end);
        if (_lastReportedAt < 0 || start < 0 || _lastReportedAt < start || _lastReportedAt > stream.Index)
        {
            return false;
        }

        for (var index = start; index < stream.Index; index++)
        {
            if (EndsAStatement(stream.Get(index).Type))
            {
                return false;
            }
        }

        return true;
    }

    // the first token of what the end of a statement ends: the rule read before it, in the rule that reads both.
    private static int StartOfTheStatementBefore(VBAParser.EndOfStatementContext end)
    {
        if (end.Parent is not ParserRuleContext parent)
        {
            return -1;
        }

        var at = parent.children?.IndexOf(end) ?? -1;
        return at > 0 && parent.children![at - 1] is ParserRuleContext { Start.TokenIndex: >= 0 } before ? before.Start.TokenIndex : parent.Start?.TokenIndex ?? -1;
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
        if (recognizer is not VBAParser { Context: VBAParser.EndOfStatementContext end } parser)
        {
            return;
        }

        // only an error of the statement's own: one the parse reports, after a statement read to its end, or one it does not report after an error of the same
        // statement, on its line, that it recovered from (Dim For As Long: the name is recovered from before As, and the statement ends there). An error it does
        // not report after an error of an earlier statement - in a block it left at the error, in the procedure before the next one - is followed by no rest of
        // a statement's line.
        var stream = (ITokenStream)parser.InputStream;
        if (!reported && !InTheStatementItEnds(parser, end))
        {
            return;
        }

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

    // where a statement of a block begins with a token no statement begins with, and that ends no block (Mod = 1), the block would end there, and what
    // follows the block would be expected at that token: the end of the procedure, after which the rest of the procedure would be skipped. The statement is
    // read as one no rule reads (a = 1-), to its end, and the block goes on after it.
    public override void Sync(Parser recognizer)
    {
        if (recognizer is VBAParser { Context: VBAParser.BlockContext block } parser && !InErrorRecoveryMode(parser)
            && BeginsAStatementOfTheBlock(parser.Interpreter.atn.states[parser.State]) && BeginsNoStatement(parser))
        {
            ReadAStatementNoRuleReads(parser, block);
            return;
        }

        base.Sync(recognizer);
    }

    // the states where the block decides whether a statement of it begins: before its first, and after each.
    private static bool BeginsAStatementOfTheBlock(ATNState state)
        => state.ruleIndex == VBAParser.RULE_block && state is StarLoopEntryState or StarLoopbackState;

    // whether the next token begins no statement, ends no block, begins no procedure and ends no statement, in this block or any other: a token that ends
    // the block a statement is missing from (End If after a For with no Next) or begins the procedure after one with no end (Public Sub after a missing
    // End Sub) is recovered from where it is, as before.
    private static bool BeginsNoStatement(Parser parser)
    {
        var next = ((ITokenStream)parser.InputStream).La(1);
        return !EndsAStatement(next) && !BeginsOrEndsAStatementOrABlock.Contains(next) && !parser.GetExpectedTokens().Contains(next);
    }

    // what a statement of a block begins with, what follows a block in any rule, and what begins a procedure (VBABaseParser.RecoverToProcedureBoundary).
    private static readonly IntervalSet BeginsOrEndsAStatementOrABlock = TokensAroundAStatementOfABlock();

    private static IntervalSet TokensAroundAStatementOfABlock()
    {
        var atn = VBAParser._ATN;
        var tokens = new IntervalSet(VBALexer.SUB, VBALexer.FUNCTION, VBALexer.PROPERTY_GET, VBALexer.PROPERTY_LET, VBALexer.PROPERTY_SET, VBALexer.PUBLIC,
            VBALexer.PRIVATE, VBALexer.FRIEND, VBALexer.STATIC);
        var block = atn.ruleToStartState[VBAParser.RULE_block];
        foreach (var state in atn.states)
        {
            if (state.ruleIndex == VBAParser.RULE_block && state is StarLoopEntryState)
            {
                tokens.AddAll(atn.NextTokens(state));
            }

            for (var i = 0; i < state.NumberOfTransitions; i++)
            {
                if (state.Transition(i) is RuleTransition transition && transition.target == block)
                {
                    tokens.AddAll(atn.NextTokens(transition.followState));
                }
            }
        }

        return tokens;
    }

    // the statement is a statement of the block, a mainBlockStmt of the tokens no rule reads - the node of a statement no rule reads - and its end is read
    // after it, as the block reads the end of each of its statements.
    private void ReadAStatementNoRuleReads(VBAParser parser, VBAParser.BlockContext block)
    {
        var decision = parser.State;
        var stream = (ITokenStream)parser.InputStream;

        // reported at its first token, and the tokens up to its end are read as the error's, until the end of the statement ends it.
        // each context is invoked from the state that calls its rule, as the rule's own code invokes it: a full-context prediction goes back through it.
        ReportUnwantedToken(parser);
        var statement = new VBAParser.BlockStmtContext(block, ReadsAStatementOfTheBlock);
        parser.EnterRule(statement, ReadsAStatementOfTheBlock, VBAParser.RULE_blockStmt);
        parser.EnterRule(new VBAParser.MainBlockStmtContext(statement, ReadsTheStatement), ReadsTheStatement, VBAParser.RULE_mainBlockStmt);
        while (!EndsAStatement(stream.La(1)))
        {
            parser.Consume();
        }

        parser.ExitRule();
        parser.ExitRule();

        if (BeginsAnEndOfStatement(stream.La(1)))
        {
            parser.EnterRule(new VBAParser.EndOfStatementContext(block, ReadsTheEndOfAStatementOfTheBlock), ReadsAnEndOfStatement, VBAParser.RULE_endOfStatement);
            while (BeginsAnEndOfStatement(stream.La(1)))
            {
                parser.State = ReadsAnEndOfStatement;
                parser.individualNonEOFEndOfStatement();
            }

            parser.ExitRule();
        }

        parser.State = decision;
    }

    // the states of block whose transitions are the rules that read a statement of it and the end of that statement, and the state of blockStmt whose
    // transition is the rule that reads the statement itself.
    private static readonly int ReadsAStatementOfTheBlock = StateOfTheRuleTransition(VBAParser.RULE_block, VBAParser.RULE_blockStmt);
    private static readonly int ReadsTheEndOfAStatementOfTheBlock = StateOfTheRuleTransition(VBAParser.RULE_block, VBAParser.RULE_endOfStatement);
    private static readonly int ReadsTheStatement = StateOfTheRuleTransition(VBAParser.RULE_blockStmt, VBAParser.RULE_mainBlockStmt);

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
