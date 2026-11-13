using System.Collections.Concurrent;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;

namespace RDCore.Parsing.Syntax;

/// <summary>
/// The parse's prediction, which reads a statement that begins with <c>If</c> or <c>ElseIf</c> as the If its line is where no alternative is viable: a
/// block If when <c>Then</c> ends the line, a single-line If otherwise.
/// </summary>
/// <remarks>
/// An If whose condition the parse cannot read (<c>If x - Then</c>) is no statement any alternative reads to its end: the prediction fails at the statement,
/// and the recovery would read the line as tokens no rule reads, the If's body as statements of the block around it, and its <c>End If</c> as a statement no
/// rule reads. Read as the If it is, the error is inside its condition, and its body, its other branches and its <c>End If</c> are its own. A prediction
/// that does not fail is not changed, so valid code is read as it was.
/// </remarks>
internal sealed class IfLinePredictionSimulator(Parser parser, ATN atn) : ParserATNSimulator(parser, atn)
{
    public override int AdaptivePredict(ITokenStream input, int decision, ParserRuleContext outerContext)
    {
        // no exception filter: a filter runs before the prediction's own finally puts the input back where the decision began.
        try
        {
            return base.AdaptivePredict(input, decision, outerContext);
        }
        catch (NoViableAltException)
        {
            if (IfOfTheLine(input, decision) is { } alternative)
            {
                return alternative;
            }

            throw;
        }
    }

    // the decision of elseIfBlock, whose first alternative's Then ends its line (VBAParser.g4), and its second, whose Then the branch's statements follow on
    // the line.
    private static readonly int ElseIfDecision = DecisionOf(VBAParser.RULE_elseIfBlock);
    private const int ElseIfThenEndsTheLine = 1;
    private const int ElseIfOnTheLine = 2;

    // where If or ElseIf is next, the alternative that reads the If of the line: a decision whose prediction fails at the If may be the block's, which decides
    // on a statement as far as its end, the statement's, or the If's own.
    private static int? IfOfTheLine(ITokenStream input, int decision)
    {
        if (input.La(1) == VBALexer.IF)
        {
            return AlternativeCalling(decision, ThenEndsTheLine(input) ? VBAParser.RULE_ifStmt : VBAParser.RULE_singleLineIfStmt);
        }

        if (input.La(1) == VBALexer.ELSEIF)
        {
            return decision == ElseIfDecision
                ? ThenEndsTheLine(input) ? ElseIfThenEndsTheLine : ElseIfOnTheLine
                : AlternativeCalling(decision, VBAParser.RULE_elseIfBlock);
        }

        return null;
    }

    // whether the last token of the line, before its comment, but for the blanks, is Then.
    private static bool ThenEndsTheLine(ITokenStream input)
    {
        var last = TokenConstants.InvalidType;
        for (var at = 1; input.La(at) is not (VBALexer.NEWLINE or VBALexer.SINGLEQUOTE or VBALexer.REM or TokenConstants.Eof); at++)
        {
            if (input.La(at) is not (VBALexer.WS or VBALexer.LINE_CONTINUATION))
            {
                last = input.La(at);
            }
        }

        return last == VBALexer.THEN;
    }

    // the decision between the rule's alternatives: the state its start goes to.
    private static int DecisionOf(int rule)
        => VBAParser._ATN.ruleToStartState[rule].Transition(0).target is DecisionState { decision: >= 0 } state && state.NumberOfTransitions > 1
            ? state.decision
            : throw new InvalidOperationException($"Rule {rule} begins with no decision between its alternatives.");

    // the alternative of the decision whose path calls the rule before it reads a token, into the rules it calls on the way; none when no alternative does.
    private static readonly ConcurrentDictionary<(int Decision, int Rule), int?> Alternatives = new();

    private static int? AlternativeCalling(int decision, int rule) => Alternatives.GetOrAdd((decision, rule), key =>
    {
        var state = VBAParser._ATN.decisionToState[key.Decision];
        for (var i = 0; i < state.NumberOfTransitions; i++)
        {
            if (Calls(state.Transition(i).target, key.Rule, []))
            {
                return i + 1;
            }
        }

        return null;
    });

    // a rule's end is not followed: its return goes back to where the rule was called from, which the path did not come from.
    private static bool Calls(ATNState state, int rule, HashSet<int> seen)
    {
        if (state is RuleStopState || !seen.Add(state.stateNumber))
        {
            return false;
        }

        for (var i = 0; i < state.NumberOfTransitions; i++)
        {
            var transition = state.Transition(i);
            if (transition is RuleTransition call && call.target.ruleIndex == rule)
            {
                return true;
            }

            if (transition.IsEpsilon && Calls(transition.target, rule, seen))
            {
                return true;
            }
        }

        return false;
    }
}
