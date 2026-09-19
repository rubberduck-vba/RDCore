using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;
using System.Globalization;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RDCore.Runtime.Semantics.Operators.Relational;

/// <summary>
/// MS-VBAL 5.6.9.6 Binary 'Like' Operator
/// </summary>
public sealed record class LikeRelationalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryRelationalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
{
    /// <summary>
    /// MS-VBAL 5.6.9.6: if either operand's value is Null, the result is Null; otherwise both
    /// operands are Let-coerced to String regardless of their own value type — unlike the other
    /// relational operators, Like never resolves an effective type off the base numeric/date table.
    /// </summary>
    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver,
        BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags> context,
        VBBinaryOperatorExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        var lhs = frame.Operands[(int)InputIndex.BinaryLeftOperand].GetTargetType();
        var rhs = frame.Operands[(int)InputIndex.BinaryRightOperand].GetTargetType();

        return DetermineOperatorEffectiveTypeResult.Success(
            lhs is VBNullType || rhs is VBNullType ? VBNullType.TypeInfo : VBStringType.TypeInfo);
    }

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        ISymbolResolver resolver,
        BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags> context,
        VBBinaryOperatorExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        try
        {
            return base.EvaluateExpressionResult(resolver, context, expression, frame);
        }
        catch (ArgumentException)
        {
            // MS-VBAL 5.6.9.6: the pattern doesn't form a valid, complete like-pattern-element —
            // runtime error 93 (Invalid pattern string), never an uncaught regex exception.
            // RegexParseException (an unterminated charlist that still slips past ToRegex's own
            // check) derives from ArgumentException too, so this also covers that edge.
            return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.InvalidPatternString, expression,
                Exceptions.LikeOperatorRuntimeErrorExceptionInvalidPatternString_Verbose));
        }
    }

    protected override bool ComparisonOp(string lhs, string rhs, StringComparisonRules rules)
    {
        var pattern = ToRegex(rhs);
        if (!rules.IgnoresCase)
        {
            return Regex.IsMatch(lhs, pattern, RegexOptions.CultureInvariant);
        }

        // MS-VBAL 5.6.9.6: in text mode the match is regardless of case, by the regional settings of the environment; a Regex
        // takes the culture it ignores case by from the current culture at the time it is constructed.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = rules.EffectiveCulture;
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase).IsMatch(lhs);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    protected override bool ComparisonOp<T>(T lhs, T rhs) => throw new NotSupportedException();

    private static string ToRegex(string likePattern)
    {
        StringBuilder regexStr = new();
        for (var i = 0;  i < likePattern.Length; i++)
        {
            var token = likePattern[i];
            switch (token)
            {
                case '?':
                    regexStr.Append('.');
                    break;
                case '#':
                    regexStr.Append(@"\d");
                    break;
                case '*':
                    regexStr.Append(@".*?");
                    break;
                case '[':
                    var close = likePattern.IndexOf(']', i + 1);
                    if (close < 0)
                    {
                        // like-pattern-charlist = "[" ["!"] ["-"] *like-pattern-charlist-element ["-"] "]"
                        // — an unterminated charlist is not a valid, complete like-pattern-element.
                        throw new ArgumentException("Unterminated like-pattern-charlist.", nameof(likePattern));
                    }
                    regexStr.Append(ToCharListRegex(likePattern[(i + 1)..close]));
                    i = close;
                    break;
                default:
                    // any other character matches itself literally — escape it so a regex
                    // metacharacter in the source string (".", "+", "(", ...) can't be misread
                    // as pattern-matching syntax.
                    regexStr.Append(Regex.Escape(token.ToString()));
                    break;

            }
        }
        // Full string match, e.g. "abcd" should NOT match "a.c"
        return $"^{regexStr}$";
    }

    /// <summary>
    /// Translates a VBA <c>like-pattern-charlist</c> body (the text between "[" and "]", exclusive)
    /// into a .NET regex character class. VBA's "!" negation prefix and "-" range syntax already
    /// match .NET's own char-class syntax; only characters ".NET" treats specially inside a class
    /// ("^", "\", "]") that VBA does not need escaping so they can't be misread as regex syntax.
    /// </summary>
    private static string ToCharListRegex(string body)
    {
        var negate = body.StartsWith('!');
        if (negate)
        {
            body = body[1..];
        }

        var escaped = body.Replace("\\", "\\\\").Replace("^", "\\^").Replace("]", "\\]");
        return negate ? $"[^{escaped}]" : $"[{escaped}]";
    }
}
