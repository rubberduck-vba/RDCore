using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using RDCore.SDK.Model.Source;

namespace RDCore.Parsing.Syntax;

/// <summary>
/// What MS-VBA says of a syntax error the grammar reports, and where it puts the caret (RD-VBAL 2.6.1): in the body of a procedure, <c>Syntax error</c>,
/// over the whole line; outside it, in a procedure's header or a declaration of the module, <c>Expected: …</c>, at the token where what it expects is
/// missing.
/// </summary>
/// <remarks>
/// MS-VBA compiles a procedure's body line by line, and says of a line it cannot read only that it is a syntax error: from the start of the line, its
/// indentation, label or line number, to its last character, its comment and the lines it continues on included. A declaration and a procedure's header
/// it reads token by token, and says what it expected where it found something else: an identifier, a type name, an expression, the end of the statement,
/// and so on. Where the grammar does not tell which of those it expected, the error is <c>Syntax error</c> at the token.
/// </remarks>
internal static class VBEWording
{
    /// <summary>
    /// What MS-VBA says of the syntax error the parse found at a token, and where, in the coordinates of the parse's own text.
    /// </summary>
    /// <param name="parser">The parser that found the error, as it is when it reports it.</param>
    /// <param name="offending">The token the parse failed at.</param>
    /// <param name="exception">The exception the parse raised for the error, if it raised one.</param>
    /// <returns>The range MS-VBA marks, and its message.</returns>
    public static (SourcePosition Start, SourcePosition End, string Message) Of(Parser parser, IToken offending, RecognitionException? exception)
    {
        var tokens = (ITokenStream)parser.InputStream;
        var context = exception?.Context as ParserRuleContext ?? parser.Context;
        if (InAProcedureBody(context, offending))
        {
            var (start, end) = LineOf(tokens, offending);
            return (start, end, Exceptions.VBCompileError_SyntaxError_Verbose);
        }

        var at = new SourcePosition(offending.Line - 1, offending.Column);
        var to = EndsAStatement(offending.Type) ? at : EndOf(offending);
        return (at, to, Expected(parser, tokens, offending, context, exception) ?? Exceptions.VBCompileError_SyntaxError_Verbose);
    }

    // whether the token is in the body of a procedure: in its block, or after its header, where the parse failed to read the block's end.
    private static bool InAProcedureBody(RuleContext? context, IToken offending)
    {
        for (; context is not null; context = context.Parent)
        {
            switch (context)
            {
                case VBAParser.BlockContext:
                    return true;
                case VBAParser.SubStmtContext or VBAParser.FunctionStmtContext or VBAParser.PropertyGetStmtContext or VBAParser.PropertyLetStmtContext
                    or VBAParser.PropertySetStmtContext:
                    var header = ((ParserRuleContext)context).GetChild<VBAParser.EndOfStatementContext>(0);
                    return header?.Stop is { } end && offending.TokenIndex > end.TokenIndex;
            }
        }

        return false;
    }

    // the line the token is on, as MS-VBA marks it: from its start to its last character that is no blank, through the lines it continues on.
    private static (SourcePosition Start, SourcePosition End) LineOf(ITokenStream tokens, IToken offending)
    {
        var index = offending.TokenIndex >= 0 ? offending.TokenIndex : tokens.Index;
        var first = index;
        while (first > 0 && tokens.Get(first - 1).Type != VBALexer.NEWLINE)
        {
            first--;
        }

        var start = new SourcePosition(tokens.Get(first).Line - 1, 0);
        var last = -1;
        for (var at = first; at < tokens.Size && tokens.Get(at).Type is not (VBALexer.NEWLINE or TokenConstants.Eof); at++)
        {
            if (tokens.Get(at).Type != VBALexer.WS)
            {
                last = at;
            }
        }

        return (start, last < 0 ? start : EndOf(tokens.Get(last)));
    }

    // where a token ends: after its last character, on the line after it when that character ends a line (a line continuation).
    private static SourcePosition EndOf(IToken token)
    {
        var text = token.Text ?? string.Empty;
        var lastBreak = text.LastIndexOf('\n');
        return lastBreak < 0
            ? new SourcePosition(token.Line - 1, token.Column + text.Length)
            : new SourcePosition(token.Line - 1 + text.Count(character => character == '\n'), text.Length - lastBreak - 1);
    }

    private static bool EndsAStatement(int type) => StatementBoundErrorStrategy.EndsAStatement(type);

    // the rules that read a name, a type, an expression, and the end of a statement.
    private static readonly HashSet<int> Names =
    [
        VBAParser.RULE_identifier, VBAParser.RULE_untypedIdentifier, VBAParser.RULE_typedIdentifier, VBAParser.RULE_identifierValue,
        VBAParser.RULE_unrestrictedIdentifier, VBAParser.RULE_subroutineName, VBAParser.RULE_functionName, VBAParser.RULE_foreignName,
    ];

    private static readonly HashSet<int> Types = [VBAParser.RULE_type, VBAParser.RULE_baseType, VBAParser.RULE_complexType];

    private static readonly HashSet<int> EndsOfStatement =
        [VBAParser.RULE_endOfStatement, VBAParser.RULE_endOfLine, VBAParser.RULE_individualNonEOFEndOfStatement];

    // what MS-VBA says it expected where the parse failed at the token, outside a procedure's body; none where the grammar does not tell.
    private static string? Expected(Parser parser, ITokenStream tokens, IToken offending, ParserRuleContext? context, RecognitionException? exception)
    {
        // Property, then no Get, Let or Set: the lexer reads Property Get as one token, and Property alone as a name.
        if (TokenBefore(tokens, offending) is { } before && string.Equals(before.Text, "Property", StringComparison.OrdinalIgnoreCase))
        {
            return Exceptions.VBCompileError_ExpectedGetOrLetOrSet_Verbose;
        }

        var (rules, next) = Ahead(parser, exception);
        next.AddAll(ExpectedTokens(parser, exception));
        if (next.Contains(VBALexer.COMMA) && next.Contains(VBALexer.RPAREN))
        {
            return Exceptions.VBCompileError_ExpectedListSeparatorOrParenthesis_Verbose;
        }

        // Lib, Text and Binary are keywords a name can be: where a name can be read, they are no keyword it expected.
        var name = next.Contains(VBALexer.IDENTIFIER);
        if (!name && next.Contains(VBALexer.LIB))
        {
            return Exceptions.VBCompileError_ExpectedLib_Verbose;
        }

        if (!name && next.Contains(VBALexer.TEXT) && next.Contains(VBALexer.BINARY))
        {
            return Exceptions.VBCompileError_ExpectedTextOrBinary_Verbose;
        }

        // an expression can begin with the name of a built-in type (TypeOf x Is Long), and a type is no expression.
        if (rules.Contains(VBAParser.RULE_expression))
        {
            return Exceptions.VBCompileError_ExpectedExpression_Verbose;
        }

        if (rules.Overlaps(Types))
        {
            return CanBeNew(context) ? Exceptions.VBCompileError_ExpectedNewOrTypeName_Verbose : Exceptions.VBCompileError_ExpectedTypeName_Verbose;
        }

        if (rules.Overlaps(Names))
        {
            return Exceptions.VBCompileError_ExpectedIdentifier_Verbose;
        }

        return rules.Overlaps(EndsOfStatement) ? Exceptions.VBCompileError_ExpectedEndOfStatement_Verbose : null;
    }

    // the tokens the parse expected where it failed, its context's included; none where it cannot tell.
    private static IntervalSet ExpectedTokens(Parser parser, RecognitionException? exception)
    {
        try
        {
            return exception is not null && exception.OffendingState >= 0 ? exception.GetExpectedTokens() : parser.GetExpectedTokens();
        }
        catch (ArgumentException)
        {
            return new IntervalSet();
        }
    }

    // a type a variable is declared with can be a New one: As, and no New after it yet. A parameter's, a function's and a record field's cannot.
    private static bool CanBeNew(RuleContext? context)
    {
        for (; context is not null; context = context.Parent)
        {
            switch (context)
            {
                case VBAParser.AsTypeClauseContext clause when clause.NEW() is not null:
                    return false;
                case VBAParser.VariableSubStmtContext:
                    return true;
                case VBAParser.ArgContext or VBAParser.ModuleDeclarationsElementContext:
                    return false;
            }
        }

        return false;
    }

    // the rules the parse was in, or was about to enter, where it needed the token it failed at, and the tokens it could have read there, past the blanks:
    // from the states the prediction ended in where no alternative was viable, or from the state the parse failed in.
    private static (HashSet<int> Rules, IntervalSet Tokens) Ahead(Parser parser, RecognitionException? exception)
    {
        var atn = parser.Interpreter.atn;
        var pending = new Stack<(ATNState State, PredictionContext? Context)>();
        if (exception is NoViableAltException { DeadEndConfigs: { Count: > 0 } configs })
        {
            foreach (var config in configs)
            {
                pending.Push((config.State, config.Context));
            }
        }
        else if ((exception?.OffendingState ?? parser.State) is >= 0 and var at)
        {
            var context = exception?.Context ?? parser.Context;
            pending.Push((atn.states[at], context is null ? null : PredictionContext.FromRuleContext(atn, context)));
        }

        var rules = new HashSet<int>();
        var next = new IntervalSet();
        var seen = new HashSet<int>();
        while (pending.TryPop(out var item))
        {
            var (state, called) = item;
            if (!seen.Add(state.stateNumber))
            {
                continue;
            }

            // a rule's end goes back to every place the rule is called from: what follows it is what follows where the parse called it from.
            rules.Add(state.ruleIndex);
            if (state is RuleStopState)
            {
                for (var i = 0; called is not null && i < called.Size; i++)
                {
                    if (called.GetReturnState(i) is var follow && follow >= 0 && follow < atn.states.Count)
                    {
                        pending.Push((atn.states[follow], called.GetParent(i)));
                    }
                }

                continue;
            }

            for (var i = 0; i < state.NumberOfTransitions; i++)
            {
                switch (state.Transition(i))
                {
                    // the blanks before what comes next are no part of it.
                    case RuleTransition { target.ruleIndex: VBAParser.RULE_whiteSpace } blanks:
                        pending.Push((blanks.followState, called));
                        break;
                    case RuleTransition call:
                        pending.Push((call.target, called?.GetChild(call.followState.stateNumber)));
                        break;
                    case { IsEpsilon: true } epsilon:
                        pending.Push((epsilon.target, called));
                        break;
                    case var token when token.Label is { } label:
                        if (label.Contains(VBALexer.WS) || label.Contains(VBALexer.LINE_CONTINUATION))
                        {
                            pending.Push((token.target, called));
                        }
                        else
                        {
                            next.AddAll(label);
                        }

                        break;
                }
            }
        }

        return (rules, next);
    }

    // the token before the one given, past the blanks; none at the start of the module.
    private static IToken? TokenBefore(ITokenStream tokens, IToken token)
    {
        for (var at = (token.TokenIndex >= 0 ? token.TokenIndex : tokens.Index) - 1; at >= 0; at--)
        {
            if (tokens.Get(at) is { Type: not (VBALexer.WS or VBALexer.LINE_CONTINUATION) } before)
            {
                return before;
            }
        }

        return null;
    }
}
