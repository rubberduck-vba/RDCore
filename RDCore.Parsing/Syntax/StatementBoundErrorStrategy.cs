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
/// statement no rule reads, to its end (<c>Mod = 1</c>), in a block and in the body of a <c>For</c>, and its label is read as a label
/// (<c>L1: Mod = 1</c>). A word that ends a block or begins a procedure is still recovered from where it is.
/// </para>
/// <para>
/// The header of a <c>For</c> or a <c>For Each</c> that ends with a binary operator the parse leaves unread (<c>For i = 1 To 2 -</c>) is reported at the
/// operator, which is a token no rule reads, and the loop goes on to its body and its <c>Next</c>.
/// </para>
/// <para>
/// A parse with no syntax error never enters recovery, so this strategy does not change it.
/// </para>
/// </remarks>
internal sealed class StatementBoundErrorStrategy : DefaultErrorStrategy
{
    // what ends a statement, as recovery sees it: the end of a statement, a comment's included, or the end of the module.
    internal static bool EndsAStatement(int type) => BeginsAnEndOfStatement(type) || type == TokenConstants.Eof;

    // a procedure's header is never one token too many either: where End Sub is missing, deleting `Sub` would read the procedure after it as the rest of
    // this one.
    protected override IToken SingleTokenDeletion(Parser recognizer)
        => EndsAStatement(((ITokenStream)recognizer.InputStream).La(1)) || VBABaseParser.BeginsAProcedureHeader((ITokenStream)recognizer.InputStream)
            ? null!
            : base.SingleTokenDeletion(recognizer);

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

    // the first token of what the end of a statement ends: the rule read before it, in the rule that reads both. The label before a statement is not part of
    // it, and its colon ends no statement (L1: Dim For As Long).
    private static int StartOfTheStatementBefore(VBAParser.EndOfStatementContext end)
    {
        if (end.Parent is not ParserRuleContext parent)
        {
            return -1;
        }

        var at = parent.children?.IndexOf(end) ?? -1;
        var before = at > 0 ? parent.children![at - 1] as ParserRuleContext : null;
        if (before is VBAParser.BlockStmtContext { } labelled && labelled.statementLabelDefinition() is { Stop: { } label })
        {
            return labelled.mainBlockStmt() is { Start.TokenIndex: >= 0 } statement ? statement.Start.TokenIndex : label.TokenIndex + 1;
        }

        return before is { Start.TokenIndex: >= 0 } ? before.Start.TokenIndex : parent.Start?.TokenIndex ?? -1;
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

        // a second error at the token of the first, in a rule recovered from there before, makes ANTLR consume the token, so that a loop cannot fail at it
        // forever. A procedure's header and its End Sub are never that token: the rules the parse returns to read them, the procedure's End Sub and the
        // module's next procedure, and consuming one would make the procedure run into the next.
        var stream = (ITokenStream)recognizer.InputStream;
        if (VBABaseParser.BeginsAProcedureHeader(stream) || stream.La(1) is VBALexer.END_SUB or VBALexer.END_FUNCTION or VBALexer.END_PROPERTY)
        {
            lastErrorIndex = -1;
        }

        base.Recover(recognizer, e);
        if (recognizer is not VBAParser { Context: VBAParser.EndOfStatementContext end } parser)
        {
            return;
        }

        // only an error of the statement's own: one the parse reports, after a statement read to its end, or one it does not report after an error of the same
        // statement, on its line, that it recovered from (Dim For As Long: the name is recovered from before As, and the statement ends there). An error it does
        // not report after an error of an earlier statement - in a block it left at the error, in the procedure before the next one - is followed by no rest of
        // a statement's line, and neither is the next procedure's header.
        if (!reported && !InTheStatementItEnds(parser, end) || VBABaseParser.BeginsAProcedureHeader(stream))
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
    // read as one no rule reads (a = 1-), to its end, and the block goes on after it. So is each such statement that follows it: the block decides on the
    // next statement without calling this again.
    public override void Sync(Parser recognizer)
    {
        if (recognizer is VBAParser { Context: VBAParser.BlockContext block } parser && !InErrorRecoveryMode(parser)
            && BeginsAStatementOfTheBlock(parser.Interpreter.atn.states[parser.State]) && BeginsNoStatement(parser))
        {
            do
            {
                ReadAStatementNoRuleReads(parser, block);
            }
            while (!InErrorRecoveryMode(parser) && BeginsNoStatement(parser));

            return;
        }

        // the rest of the line of a statement of a For, after an error inside it that the parse recovers from (Dim For As Long): the statement's, as Recover
        // reads it in a block. The For would otherwise decide on it, and end there. A procedure's header is no statement of the For: it ends the procedure.
        if (recognizer is VBAParser { Context: VBAParser.ForNextStmtContext or VBAParser.ForEachStmtContext } recovering && InErrorRecoveryMode(recovering)
            && BeginsAStatementOfTheFor(recovering.Interpreter.atn.states[recovering.State]) && recovering.Context.children?[^1] is VBAParser.BlockStmtContext
            && TheLastErrorIsOnThisLine((ITokenStream)recovering.InputStream) && !VBABaseParser.BeginsAProcedureHeader((ITokenStream)recovering.InputStream))
        {
            var stream = (ITokenStream)recovering.InputStream;
            while (!EndsAStatement(stream.La(1)))
            {
                recovering.Consume();
            }
        }

        if (recognizer is VBAParser { Context: VBAParser.ForNextStmtContext or VBAParser.ForEachStmtContext } loop && !InErrorRecoveryMode(loop))
        {
            if (DanglingOperatorAhead(loop) is { } through)
            {
                ReadADanglingOperatorOfTheHeader(loop, through);
            }

            // a For's statements are no block: the loop decides on the end of a statement and the statement after it together, and decides on neither when
            // that statement begins with a token no statement begins with. Each such statement is read after its end, as the For reads a statement of it.
            if (BeginsAStatementOfTheFor(loop.Interpreter.atn.states[loop.State]))
            {
                var decision = loop.State;
                while (BeginsNoStatementAfterTheEnd(loop))
                {
                    ReadAStatementNoRuleReadsOfTheFor(loop, (ParserRuleContext)loop.Context);
                    loop.State = decision;
                }
            }
        }

        base.Sync(recognizer);
    }

    // whether the last error reported is on the line the parse is at: no end of a statement is between the token it is at and the next one.
    private bool TheLastErrorIsOnThisLine(ITokenStream stream)
    {
        if (_lastReportedAt < 0 || _lastReportedAt > stream.Index)
        {
            return false;
        }

        for (var index = _lastReportedAt; index < stream.Index; index++)
        {
            if (EndsAStatement(stream.Get(index).Type))
            {
                return false;
            }
        }

        return true;
    }

    // the states where a For decides whether a statement of it begins: before its first, and after each.
    private static bool BeginsAStatementOfTheFor(ATNState state)
        => state.ruleIndex is VBAParser.RULE_forNextStmt or VBAParser.RULE_forEachStmt && state is StarLoopEntryState or StarLoopbackState;

    // whether the end of a statement is next, and the token after it begins no statement, ends no block, begins no procedure and goes on with no For.
    private static bool BeginsNoStatementAfterTheEnd(Parser parser)
    {
        var stream = (ITokenStream)parser.InputStream;
        var at = PastTheBlanks(stream, 1);
        if (!BeginsAnEndOfStatement(stream.La(at)))
        {
            return false;
        }

        while (BeginsAnEndOfStatement(stream.La(at)))
        {
            // a comment is the rest of its line.
            if (stream.La(at) is VBALexer.SINGLEQUOTE or VBALexer.REM)
            {
                while (stream.La(at) is not (VBALexer.NEWLINE or TokenConstants.Eof))
                {
                    at++;
                }
            }

            at = PastTheBlanks(stream, at + 1);
        }

        var next = stream.La(PastALabel(stream, at));
        return next != TokenConstants.Eof && !BeginsOrEndsAStatementOrABlock.Contains(next) && !GoesOnWithAFor.Contains(next);
    }

    // what a For reads after the end of a statement of it: a statement, the label before its Next, its Next.
    private static readonly IntervalSet GoesOnWithAFor = TokensAfterAnEndOfStatementOfAFor();

    private static IntervalSet TokensAfterAnEndOfStatementOfAFor()
    {
        var atn = VBAParser._ATN;
        var end = atn.ruleToStartState[VBAParser.RULE_endOfStatement];
        var tokens = new IntervalSet();
        foreach (var state in atn.states)
        {
            if (state.ruleIndex is not (VBAParser.RULE_forNextStmt or VBAParser.RULE_forEachStmt))
            {
                continue;
            }

            for (var i = 0; i < state.NumberOfTransitions; i++)
            {
                if (state.Transition(i) is RuleTransition transition && transition.target == end)
                {
                    tokens.AddAll(atn.NextTokens(transition.followState));
                }
            }
        }

        return tokens;
    }

    // the end of the statement before is read, then the statement, a mainBlockStmt of the tokens no rule reads, as a statement of the For, reported at its first
    // token; the end of the statement after it is the For's to read.
    private void ReadAStatementNoRuleReadsOfTheFor(VBAParser parser, ParserRuleContext loop)
    {
        var stream = (ITokenStream)parser.InputStream;
        var (readsTheEnd, readsTheStatement) = loop is VBAParser.ForNextStmtContext
            ? (ForNextReadsTheEndOfAStatement, ForNextReadsAStatement)
            : (ForEachReadsTheEndOfAStatement, ForEachReadsAStatement);

        parser.EnterRule(new VBAParser.EndOfStatementContext(loop, readsTheEnd), readsTheEnd, VBAParser.RULE_endOfStatement);
        while (BeginsAnEndOfStatement(stream.La(PastTheBlanks(stream, 1))))
        {
            parser.State = ReadsAnEndOfStatement;
            parser.individualNonEOFEndOfStatement();
        }

        parser.ExitRule();

        parser.State = readsTheStatement;
        ReadAStatementNoRuleReads(parser, loop, readsTheStatement);
    }

    // a blockStmt of the rule, its label read as the rule reads one, and a mainBlockStmt of the tokens no rule reads: reported at its first token, and the
    // tokens up to its end are read as the error's, until the end of the statement ends it.
    private void ReadAStatementNoRuleReads(VBAParser parser, ParserRuleContext rule, int readsTheStatement)
    {
        var stream = (ITokenStream)parser.InputStream;
        var labelled = PastALabel(stream, 1) > 1;
        if (!labelled)
        {
            ReportUnwantedToken(parser);
        }

        var statement = new VBAParser.BlockStmtContext(rule, readsTheStatement);
        parser.EnterRule(statement, readsTheStatement, VBAParser.RULE_blockStmt);
        var readsTheMainStatement = ReadsTheStatement;
        if (labelled)
        {
            parser.State = ReadsTheLabel;
            parser.statementLabelDefinition();
            if (stream.La(1) is VBALexer.WS or VBALexer.LINE_CONTINUATION)
            {
                parser.State = ReadsTheBlanksAfterTheLabel;
                parser.whiteSpace();
            }

            readsTheMainStatement = ReadsTheStatementAfterTheLabel;
            parser.State = readsTheMainStatement;
            ReportUnwantedToken(parser);
        }

        // the statement ends at its last token: the blanks after it are the end of the statement's, which reads them before a comment (Mod = 1 ' c).
        parser.EnterRule(new VBAParser.MainBlockStmtContext(statement, readsTheMainStatement), readsTheMainStatement, VBAParser.RULE_mainBlockStmt);
        while (!EndsAStatement(stream.La(1)) && !BeginsAnEndOfStatement(stream.La(PastTheBlanks(stream, 1))))
        {
            parser.Consume();
        }

        parser.ExitRule();
        parser.ExitRule();
    }

    // the states of blockStmt whose transitions are the rules that read a label, the blanks after it, and the statement after them.
    private static readonly int ReadsTheLabel = StateOfTheRuleTransition(VBAParser.RULE_blockStmt, VBAParser.RULE_statementLabelDefinition);
    private static readonly int ReadsTheBlanksAfterTheLabel = StateOfTheRuleTransition(VBAParser.RULE_blockStmt, VBAParser.RULE_whiteSpace);
    private static readonly int ReadsTheStatementAfterTheLabel
        = StateOfTheRuleTransitionAfter(VBAParser.RULE_blockStmt, VBAParser.RULE_statementLabelDefinition, VBAParser.RULE_mainBlockStmt);

    // the states of a For whose transitions are the rules that read a statement of it and the end of the statement before it.
    private static readonly int ForNextReadsAStatement = StateOfTheRuleTransition(VBAParser.RULE_forNextStmt, VBAParser.RULE_blockStmt);
    private static readonly int ForEachReadsAStatement = StateOfTheRuleTransition(VBAParser.RULE_forEachStmt, VBAParser.RULE_blockStmt);
    private static readonly int ForNextReadsTheEndOfAStatement
        = StateOfTheRuleTransitionBefore(VBAParser.RULE_forNextStmt, VBAParser.RULE_endOfStatement, VBAParser.RULE_blockStmt);
    private static readonly int ForEachReadsTheEndOfAStatement
        = StateOfTheRuleTransitionBefore(VBAParser.RULE_forEachStmt, VBAParser.RULE_endOfStatement, VBAParser.RULE_blockStmt);

    // the state of the rule whose transition is the rule called, when the rule called next is the other.
    private static int StateOfTheRuleTransitionBefore(int rule, int called, int next)
    {
        var then = VBAParser._ATN.ruleToStartState[next];
        foreach (var call in RuleTransitions(rule, called))
        {
            if (CallingState(call.Transition.followState, then) >= 0)
            {
                return call.State;
            }
        }

        throw new InvalidOperationException($"No state of rule {rule} calls rule {called}, then rule {next}.");
    }

    // the state of the rule whose transition is the rule called, when the rule called before it is the other.
    private static int StateOfTheRuleTransitionAfter(int rule, int before, int called)
    {
        var start = VBAParser._ATN.ruleToStartState[called];
        foreach (var call in RuleTransitions(rule, before))
        {
            if (CallingState(call.Transition.followState, start) is >= 0 and var state)
            {
                return state;
            }
        }

        throw new InvalidOperationException($"No state of rule {rule} calls rule {called} after rule {before}.");
    }

    private static IEnumerable<(int State, RuleTransition Transition)> RuleTransitions(int rule, int called)
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
                    yield return (state.stateNumber, transition);
                }
            }
        }
    }

    // the state whose transition calls the rule, past the epsilon transitions from the state; -1 when there is none.
    private static int CallingState(ATNState state, ATNState start, HashSet<int>? seen = null)
    {
        seen ??= [];
        if (!seen.Add(state.stateNumber))
        {
            return -1;
        }

        for (var i = 0; i < state.NumberOfTransitions; i++)
        {
            switch (state.Transition(i))
            {
                case RuleTransition call when call.target == start:
                    return state.stateNumber;
                case EpsilonTransition epsilon when CallingState(epsilon.target, start, seen) is >= 0 and var calling:
                    return calling;
            }
        }

        return -1;
    }

    private static int PastTheBlanks(ITokenStream stream, int at)
    {
        while (stream.La(at) is VBALexer.WS or VBALexer.LINE_CONTINUATION)
        {
            at++;
        }

        return at;
    }

    // the states where the block decides whether a statement of it begins: before its first, and after each.
    private static bool BeginsAStatementOfTheBlock(ATNState state)
        => state.ruleIndex == VBAParser.RULE_block && state is StarLoopEntryState or StarLoopbackState;

    // whether the next token begins no statement, begins or ends no procedure and ends no statement, and ends no block this one is in: a token that begins the
    // procedure after one with no end (Public Sub after a missing End Sub) is recovered from where it is, and so is one that ends a block around this one,
    // whose end this block is missing (End With after a Do with no Loop, a Loop in an If with no End If inside a Do). One that ends no block around it (a Loop
    // with no Do, in a With) is a statement no rule reads: the block would otherwise end there, and the procedure skip past its End Sub to the next one.
    private bool BeginsNoStatement(Parser parser)
    {
        var stream = (ITokenStream)parser.InputStream;
        var at = PastALabel(stream, 1);
        var next = stream.La(at);
        if (EndsAStatement(next) || BeginsAStatementOfABlock.Contains(next) || BeginsOrEndsAProcedure.Contains(next))
        {
            return false;
        }

        return EndsABlock.Contains(next) ? !GetErrorRecoverySet(parser).Contains(next) : at > 1 || !parser.GetExpectedTokens().Contains(next);
    }

    // where the statement at the token begins, past the label at the start of its line, its blanks included (L1: Mod = 1, 20 Mod = 2); at the token when it
    // begins with none.
    private static int PastALabel(ITokenStream stream, int at)
    {
        if (!IsAtLineStart(stream, at))
        {
            return at;
        }

        var number = stream.La(at) == VBALexer.MINUS ? at + 1 : at;
        if (stream.La(number) is VBALexer.HEXLITERAL or VBALexer.OCTLITERAL or VBALexer.FLOATLITERAL or VBALexer.INTEGERLITERAL)
        {
            var after = PastTheBlanks(stream, number + 1);
            if (stream.La(after) == VBALexer.COLON)
            {
                return PastTheBlanks(stream, after + 1);
            }

            // a line number, then blanks, a name and its colon.
            return after > number + 1 ? PastANameAndItsColon(stream, after) : after;
        }

        return PastANameAndItsColon(stream, at);
    }

    // past a name, its colon and the blanks after it, when they are next; at the token otherwise.
    private static int PastANameAndItsColon(ITokenStream stream, int at)
    {
        if (!BeginsALabel.Contains(stream.La(at)) || NoLabelName.Contains(stream.La(at)))
        {
            return at;
        }

        var colon = PastTheBlanks(stream, at + 1);
        return stream.La(colon) == VBALexer.COLON ? PastTheBlanks(stream, colon + 1) : at;
    }

    // what a label's name begins with, and the words that are no label's name: what the predicate of legalLabelIdentifier refuses.
    private static readonly IntervalSet BeginsALabel = VBAParser._ATN.NextTokens(VBAParser._ATN.ruleToStartState[VBAParser.RULE_legalLabelIdentifier]);
    private static readonly IntervalSet NoLabelName = new(VBALexer.DOEVENTS, VBALexer.END, VBALexer.CLOSE, VBALexer.ELSE, VBALexer.LOOP, VBALexer.NEXT,
        VBALexer.RANDOMIZE, VBALexer.REM, VBALexer.RESUME, VBALexer.RETURN, VBALexer.STOP, VBALexer.WEND);

    // whether the token is the first of its line, but for the blanks before it: where a label is, as statementLabelDefinition has it.
    private static bool IsAtLineStart(ITokenStream stream, int at)
    {
        // the token before it, as an index of the stream: the next token's is the stream's.
        var before = stream.Index + at - 2;
        while (before >= 0 && stream.Get(before).Type == VBALexer.WS)
        {
            before--;
        }

        return before >= 0 && stream.Get(before).Type is VBALexer.NEWLINE or VBALexer.LINE_CONTINUATION;
    }

    // the words a procedure's header begins with, and the ends of a procedure: a block in a procedure never reads them.
    private static readonly IntervalSet BeginsOrEndsAProcedure = new(VBALexer.SUB, VBALexer.FUNCTION, VBALexer.PROPERTY_GET, VBALexer.PROPERTY_LET,
        VBALexer.PROPERTY_SET, VBALexer.PUBLIC, VBALexer.PRIVATE, VBALexer.FRIEND, VBALexer.STATIC, VBALexer.END_SUB, VBALexer.END_FUNCTION, VBALexer.END_PROPERTY);

    private static readonly IntervalSet BeginsAStatementOfABlock = VBAParser._ATN.states
        .Where(state => state.ruleIndex == VBAParser.RULE_block && state is StarLoopEntryState)
        .Aggregate(new IntervalSet(), (tokens, state) => { tokens.AddAll(VBAParser._ATN.NextTokens(state)); return tokens; });

    // what follows a block in any rule: the words that end one (End If, Loop, Case, End With...).
    private static readonly IntervalSet EndsABlock = TokensAfterABlock();

    private static IntervalSet TokensAfterABlock()
    {
        var atn = VBAParser._ATN;
        var tokens = new IntervalSet();
        var block = atn.ruleToStartState[VBAParser.RULE_block];
        foreach (var state in atn.states)
        {
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

    // what a statement of a block begins with, what follows a block in any rule, and what begins or ends a procedure: after the sets it is made of, which are
    // initialized in the order they are written.
    private static readonly IntervalSet BeginsOrEndsAStatementOrABlock = new IntervalSet().AddAll(BeginsOrEndsAProcedure).AddAll(BeginsAStatementOfABlock)
        .AddAll(EndsABlock);

    // the statement is a statement of the block, a mainBlockStmt of the tokens no rule reads - the node of a statement no rule reads - and its end is read
    // after it, as the block reads the end of each of its statements.
    private void ReadAStatementNoRuleReads(VBAParser parser, VBAParser.BlockContext block)
    {
        var decision = parser.State;
        var stream = (ITokenStream)parser.InputStream;

        // each context is invoked from the state that calls its rule, as the rule's own code invokes it: a full-context prediction goes back through it.
        ReadAStatementNoRuleReads(parser, block, ReadsAStatementOfTheBlock);

        if (BeginsAnEndOfStatement(stream.La(PastTheBlanks(stream, 1))))
        {
            parser.EnterRule(new VBAParser.EndOfStatementContext(block, ReadsTheEndOfAStatementOfTheBlock), ReadsAnEndOfStatement, VBAParser.RULE_endOfStatement);
            while (BeginsAnEndOfStatement(stream.La(PastTheBlanks(stream, 1))))
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
    private static readonly int ReadsTheStatement = RuleTransitions(VBAParser.RULE_blockStmt, VBAParser.RULE_mainBlockStmt).Select(call => call.State)
        .First(state => state != StateOfTheRuleTransitionAfter(VBAParser.RULE_blockStmt, VBAParser.RULE_statementLabelDefinition, VBAParser.RULE_mainBlockStmt));

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
    private void ReadADanglingOperatorOfTheHeader(VBAParser parser, int through)
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

    // nor a procedure's header, which begins the next procedure.
    protected override void ConsumeUntil(Parser recognizer, IntervalSet set)
    {
        var stream = (ITokenStream)recognizer.InputStream;
        for (var type = stream.La(1); !EndsAStatement(type) && !set.Contains(type) && !VBABaseParser.BeginsAProcedureHeader(stream); type = stream.La(1))
        {
            recognizer.Consume();
        }
    }
}
