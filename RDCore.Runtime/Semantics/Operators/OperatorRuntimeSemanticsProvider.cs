using RDCore.SDK.Model.Symbols.Operators;
using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.Runtime.Semantics.Plans;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.Runtime.Execution;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Facts;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// Supplies the runtime semantics for every VBA operator token <see cref="RuntimeExpressionEvaluator"/>
/// dispatches to.
/// </summary>
/// <remarks>
/// Each operator's runtime semantics is a stateless rule over its own two (or one) already-evaluated
/// operands - the same <see cref="ILetCoercionRuntimeSemanticsProvider"/> and
/// <see cref="IErrorDetailsRecorder"/> back every one of them, so one instance per operator, built once
/// and reused for the life of the provider, is exactly as valid as a fresh one per call: the same pattern
/// <see cref="ILetCoercionRuntimeSemanticsProvider"/> already uses for let-coercion strategies.
/// </remarks>
public interface IOperatorRuntimeSemanticsProvider
{
    /// <summary>
    /// Evaluates a binary operator expression against its already-evaluated operands.
    /// </summary>
    RuntimeSemanticsEvaluationResult EvaluateBinaryOperator(IRuntimeSession session, VBBinaryOperatorExpressionNode expression, VBTypedValue left, VBTypedValue right);

    /// <summary>
    /// Evaluates the binary operator <paramref name="token"/> against its already-evaluated operands, for an operation that is not an operator
    /// expression of its own in source: the comparison of a <c>Case</c>, the step and the limit of a <c>For</c> loop, an assignment.
    /// </summary>
    /// <param name="session">The session the operation is evaluated in.</param>
    /// <param name="token">The operator: one of the <see cref="Tokens"/> operators, or the let-assignment operator
    /// (<see cref="OperatorSymbolNames.BinaryAssignmentValueOp"/>).</param>
    /// <param name="expression">The expression the operation is evaluated for, whose identity and location are the operation's.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="site">The construct that asks for the operation, which is the site of the conversions of its operands. The let-assignment operator
    /// is always an <see cref="ConversionSite.Assignment"/>.</param>
    RuntimeSemanticsEvaluationResult EvaluateBinaryOperator(
        IRuntimeSession session, string token, ExpressionNode expression, VBTypedValue left, VBTypedValue right,
        ConversionSite site = ConversionSite.OperatorOperand);

    /// <summary>
    /// Evaluates a unary operator expression against its already-evaluated operand.
    /// </summary>
    RuntimeSemanticsEvaluationResult EvaluateUnaryOperator(IRuntimeSession session, VBUnaryOperatorExpressionNode expression, VBTypedValue operand);
}

/// <inheritdoc cref="IOperatorRuntimeSemanticsProvider"/>
public sealed class OperatorRuntimeSemanticsProvider : IOperatorRuntimeSemanticsProvider
{
    private readonly BinaryAdditionOperatorRuntimeSemantics _addition;
    private readonly BinarySubtractionOperatorRuntimeSematics _subtraction;
    private readonly BinaryMultiplicationOperatorRuntimeSemantics _multiplication;
    private readonly BinaryDivisionOperatorRuntimeSemantics _division;
    private readonly BinaryIntegerDivisionOperatorRuntimeSemantics _integerDivision;
    private readonly BinaryModuloOperatorRuntimeSemantics _modulo;
    private readonly BinaryExponentOperatorRuntimeSemantics _exponent;
    private readonly BinaryConcatOperatorRuntimeSemantics _concat;
    private readonly BinaryIsRelationalOperatorRuntimeSemantics _is;
    private readonly BinaryEqRelationalOperatorRuntimeSemantics _eq;
    private readonly BinaryNeqRelationalOperatorRuntimeSemantics _neq;
    private readonly BinaryGtRelationalOperatorRuntimeSemantics _gt;
    private readonly BinaryGtEqRelationalOperatorRuntimeSemantics _gtEq;
    private readonly BinaryLtRelationalOperatorRuntimeSemantics _lt;
    private readonly BinaryLtEqRelationalOperatorRuntimeSemantics _ltEq;
    private readonly LikeRelationalOperatorRuntimeSemantics _like;
    private readonly BinaryAndLogicalOperatorRuntimeSemantics _and;
    private readonly BinaryOrLogicalOperatorRuntimeSemantics _or;
    private readonly BinaryXorLogicalOperatorRuntimeSemantics _xor;
    private readonly BinaryEqvLogicalOperatorRuntimeSemantics _eqv;
    private readonly BinaryImpLogicalOperatorRuntimeSemantics _imp;
    private readonly UnaryNegationOperatorRuntimeSemantics _negation;
    private readonly UnaryNotOperatorRuntimeSemantics _not;
    private readonly UnaryLetCoerceOperatorRuntimeSemantics _letCoerce;
    private readonly BinaryLetAssignmentOperatorRuntimeSemantics _letAssignment;

    // told of every operation this provider evaluates; null when nothing is analyzing the code, which is how code runs.
    private readonly AnalysisObservation? _observation;

    /// <param name="letCoercionProvider">Coerces the operands of every operator.</param>
    /// <param name="errorDetails">Records the details of the errors raised.</param>
    /// <param name="observation">Told of every operation this provider evaluates, as an <see cref="OperatorFact"/>; <see langword="null"/> when nothing
    /// is analyzing the code. The same one the <paramref name="letCoercionProvider"/> is given, so that describing an operation does not state
    /// the conversions of its operands a second time.</param>
    public OperatorRuntimeSemanticsProvider(
        ILetCoercionRuntimeSemanticsProvider letCoercionProvider, IErrorDetailsRecorder errorDetails, AnalysisObservation? observation = null)
    {
        _observation = observation;
        _addition = new(letCoercionProvider, errorDetails);
        _subtraction = new(letCoercionProvider, errorDetails);
        _multiplication = new(letCoercionProvider, errorDetails);
        _division = new(letCoercionProvider, errorDetails);
        _integerDivision = new(letCoercionProvider, errorDetails);
        _modulo = new(letCoercionProvider, errorDetails);
        _exponent = new(letCoercionProvider, errorDetails);
        _concat = new(letCoercionProvider, errorDetails);
        _is = new(letCoercionProvider, errorDetails);
        _eq = new(letCoercionProvider, errorDetails);
        _neq = new(letCoercionProvider, errorDetails);
        _gt = new(letCoercionProvider, errorDetails);
        _gtEq = new(letCoercionProvider, errorDetails);
        _lt = new(letCoercionProvider, errorDetails);
        _ltEq = new(letCoercionProvider, errorDetails);
        _like = new(letCoercionProvider, errorDetails);
        _and = new(letCoercionProvider, errorDetails);
        _or = new(letCoercionProvider, errorDetails);
        _xor = new(letCoercionProvider, errorDetails);
        _eqv = new(letCoercionProvider, errorDetails);
        _imp = new(letCoercionProvider, errorDetails);
        _negation = new(letCoercionProvider, errorDetails);
        _not = new(letCoercionProvider, errorDetails);
        _letCoerce = new(letCoercionProvider, errorDetails);
        _letAssignment = new(letCoercionProvider, errorDetails);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult EvaluateBinaryOperator(IRuntimeSession session, VBBinaryOperatorExpressionNode expression, VBTypedValue left, VBTypedValue right)
        => EvaluateBinaryOperator(session, expression.Token, expression, left, right);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult EvaluateBinaryOperator(
        IRuntimeSession session, string token, ExpressionNode expression, VBTypedValue left, VBTypedValue right, ConversionSite site = ConversionSite.OperatorOperand)
        => token switch
        {
            // an assignment states the conversion of its source, which is a fact of its own: it is not an operation to state.
            // one nothing observes is let-coerced by its plan when it can be.
            OperatorSymbolNames.BinaryAssignmentValueOp => (IsObserved ? null : _letAssignment.TryAssignPlanned(session, left, right))
                ?? _letAssignment.EvaluateAtSite(session, Empty<ConversionOperationSemanticContext>.Context, expression, ConversionSite.Assignment, left, right),
            Tokens.AdditionOp => Evaluate(_addition, session, token, expression, site, left, right),
            Tokens.SubtractionOp => Evaluate(_subtraction, session, token, expression, site, left, right),
            Tokens.MultiplicationOp => Evaluate(_multiplication, session, token, expression, site, left, right),
            Tokens.DivisionOp => Evaluate(_division, session, token, expression, site, left, right),
            Tokens.IntegerDivisionOp => Evaluate(_integerDivision, session, token, expression, site, left, right),
            Tokens.ModuloOp => Evaluate(_modulo, session, token, expression, site, left, right),
            Tokens.PowerOp => Evaluate(_exponent, session, token, expression, site, left, right),
            Tokens.ConcatOp => Evaluate(_concat, session, token, expression, site, left, right),
            Tokens.CompareIsOp => Evaluate(_is, session, token, expression, site, left, right),
            Tokens.CompareEqualOp => Evaluate(_eq, session, token, expression, site, left, right),
            Tokens.CompareNotEqualOp => Evaluate(_neq, session, token, expression, site, left, right),
            Tokens.CompareGreaterThanOp => Evaluate(_gt, session, token, expression, site, left, right),
            Tokens.CompareGreaterThanOrEqualOp => Evaluate(_gtEq, session, token, expression, site, left, right),
            Tokens.CompareLessThanOp => Evaluate(_lt, session, token, expression, site, left, right),
            Tokens.CompareLessThanOrEqualOp => Evaluate(_ltEq, session, token, expression, site, left, right),
            Tokens.CompareLikeOp => Evaluate(_like, session, token, expression, site, left, right),
            Tokens.LogicalAndOp => Evaluate(_and, session, token, expression, site, left, right),
            Tokens.LogicalOrOp => Evaluate(_or, session, token, expression, site, left, right),
            Tokens.LogicalXOrOp => Evaluate(_xor, session, token, expression, site, left, right),
            Tokens.LogicalEqvOp => Evaluate(_eqv, session, token, expression, site, left, right),
            Tokens.LogicalImpOp => Evaluate(_imp, session, token, expression, site, left, right),
            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult EvaluateUnaryOperator(IRuntimeSession session, VBUnaryOperatorExpressionNode expression, VBTypedValue operand)
        => expression.Token switch
        {
            Tokens.NegationOp => Evaluate(_negation, session, expression.Token, expression, ConversionSite.OperatorOperand, operand),
            Tokens.LogicalNotOp => Evaluate(_not, session, expression.Token, expression, ConversionSite.OperatorOperand, operand),
            // MS-VBAL 5.6.6: a pair of parentheses around an expression is an operator - the precedence one around an operand of an operator, the
            // let-coercion one anywhere else (RD-VBAL §5.6.6) - and either evaluates to the simple data value of what it encloses: an object's default
            // member, read once (`(o) Is Nothing` tests what it returns, `Set x = (o)` is set to it; MS-VBA, Excel-verified), and a value's conversion.
            // "the declared type of a parenthesized expression is that of the enclosed expression": a Variant's value is still a Variant's, which `(v + 1)`
            // compared with a String is (MS-VBA, observed in Excel).
            OperatorSymbolNames.UnaryLetCoerceOp or OperatorSymbolNames.UnaryArithmeticPrecedenceOp => operand switch
            {
                VBObjectValue enclosed => Execution.ObjectDefaultMember.EnclosedValueOf(session, enclosed),
                VBVariantValue => AsVariant(_letCoerce.Evaluate(session, new(), expression, operand)),
                _ => _letCoerce.Evaluate(session, new(), expression, operand),
            },
            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };

    // whether what is evaluated now is stated: describing an operation suspends the observation, and what is evaluated to describe it is not.
    private bool IsObserved => _observation is { IsSuspended: false };

    // the context an operation is evaluated in when nothing has analyzed it: a context is immutable, and an empty one is the same for every operation.
    private static class Empty<TContext> where TContext : new()
    {
        public static readonly TContext Context = new();
    }

    // replaces each operand that is an object, or a Variant that holds one, with its simple data value (ObjectDefaultMember.ValueOf); the error of the
    // first that has none, raised at the operation unless its default member raised it somewhere of its own.
    private static RuntimeSemanticsEvaluationResult? SimpleDataValues(IRuntimeSession session, ExpressionNode expression, VBTypedValue[] operands)
    {
        for (var index = 0; index < operands.Length; index++)
        {
            // an object an analysis assumes is not one to call: what it is said of is the operation's, on what the operand might be.
            if (operands[index] is not (VBObjectValue or VBVariantValue { TypedValue: VBObjectValue or VBVariantValue }) || operands[index].IsIndeterminate)
            {
                continue;
            }

            var read = ObjectDefaultMember.ValueOf(session, operands[index]);
            if (!read.IsSuccess)
            {
                return read.ErrorInfo is VBRuntimeErrorInfo { Location: var location } error && location == default
                    ? RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For((VBRuntimeErrorId)error.ErrorId, expression.Location, error.Verbose))
                    : read;
            }

            operands[index] = read.Result!;
        }

        return null;
    }

    // the one place an operation is evaluated: what the evaluation says is the operation's, and is stated as it is, once the
    // operation is done, so that what is observed is what ran.
    private RuntimeSemanticsEvaluationResult Evaluate<TContext, TFlags>(
        OperatorRuntimeSemantics<TContext, TFlags> semantics,
        IRuntimeSession session,
        string token,
        ExpressionNode expression,
        ConversionSite site,
        params VBTypedValue[] operands)
        where TContext : SemanticContext<TFlags>, new()
        where TFlags : struct, Enum
    {
        // a Variant operand is one before its simple data value is read: the operation is declared Variant whatever the object's default member returns.
        var isDeclaredVariant = IsDeclaredVariantBy(token) && operands.Any(operand => operand is VBVariantValue);

        // MS-VBAL 5.6.9.2: a simple data operator - every operator but Is, which compares references - "first evaluates its operands as simple data
        // values": an object is what its default member returns (MS-VBAL 5.6.2.2), before the operation is planned on what its operands are. An operand
        // that has no value is the operation's error, which is stated as the operation's like any other.
        var unreadable = token != Tokens.CompareIsOp ? SimpleDataValues(session, expression, operands) : null;

        // an operation nothing observes is evaluated by its plan when it can be, and by the pipeline otherwise. An observed operation
        // always goes through the pipeline, which is what describes the conversions of its operands.
        if (unreadable is null && !IsObserved && PlanExecutor.TryEvaluate(semantics, session, Empty<TContext>.Context, token, expression, site, operands) is { } planned)
        {
            return isDeclaredVariant ? AsVariant(planned) : planned;
        }

        var result = unreadable ?? semantics.EvaluateAtSite(session, Empty<TContext>.Context, expression, site, operands);
        if (_observation is { IsSuspended: false } observation)
        {
            // describing the operation evaluates it again, operands and all: those conversions are not the code's.
            OperatorFact? fact;
            using (observation.Suspend())
            {
                fact = semantics.Observe(session, token, expression, result, operands);
            }

            if (fact is not null)
            {
                observation.OnOperation(fact);
            }
        }

        return isDeclaredVariant ? AsVariant(result) : result;
    }

    // MS-VBAL 5.6.9.3-6, 5.6.9.8: an arithmetic operator, &, a relational operator, Like and a logical operator with an operand declared Variant are declared
    // Variant, and what they evaluate to is held in one: compared with a String, `v + 1` is compared as text as v is, and `v & ""` as a Variant String
    // (MS-VBA, recorded in Excel); compared with a Variant String, `v Or 255` and `v < 255` are ranked below it as a Variant number is, whatever their
    // value (MS-VBA, recorded in Excel). Is is declared Boolean (MS-VBAL 5.6.9.7).
    private static bool IsDeclaredVariantBy(string token) => token is Tokens.AdditionOp or Tokens.SubtractionOp or Tokens.MultiplicationOp or Tokens.DivisionOp
        or Tokens.IntegerDivisionOp or Tokens.ModuloOp or Tokens.PowerOp or Tokens.ConcatOp or Tokens.NegationOp
        or Tokens.CompareEqualOp or Tokens.CompareNotEqualOp or Tokens.CompareLessThanOp or Tokens.CompareLessThanOrEqualOp or Tokens.CompareGreaterThanOp
        or Tokens.CompareGreaterThanOrEqualOp or Tokens.CompareLikeOp
        or Tokens.LogicalAndOp or Tokens.LogicalOrOp or Tokens.LogicalXOrOp or Tokens.LogicalEqvOp or Tokens.LogicalImpOp or Tokens.LogicalNotOp;

    private static RuntimeSemanticsEvaluationResult AsVariant(RuntimeSemanticsEvaluationResult result)
        => result is { IsSuccess: true, Result: { } value } && value is not VBVariantValue
            ? RuntimeSemanticsEvaluationResult.Success(value.IsIndeterminate ? new VBVariantValue(value).AsIndeterminate() : new VBVariantValue(value))
            : result;
}
