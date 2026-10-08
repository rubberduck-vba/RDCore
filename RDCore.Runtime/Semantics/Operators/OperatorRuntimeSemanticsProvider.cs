using RDCore.SDK.Model.Symbols.Operators;
using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// Supplies the runtime semantics for every VBA operator token <see cref="RuntimeExpressionEvaluator"/>
/// dispatches to.
/// </summary>
/// <remarks>
/// Each operator's runtime semantics is a stateless rule over its own two (or one) already-evaluated
/// operands - the same <see cref="ILetCoercionRuntimeSemanticsProvider"/> and
/// <see cref="IVerboseMessageBuilder"/> back every one of them, so one instance per operator, built once
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
    /// <param name="formatterService">Builds the verbose half of an error message.</param>
    /// <param name="observation">Told of every operation this provider evaluates, as an <see cref="OperatorFact"/>; <see langword="null"/> when nothing
    /// is analyzing the code. The same one the <paramref name="letCoercionProvider"/> is given, so that describing an operation does not state
    /// the conversions of its operands a second time.</param>
    public OperatorRuntimeSemanticsProvider(
        ILetCoercionRuntimeSemanticsProvider letCoercionProvider, IVerboseMessageBuilder formatterService, AnalysisObservation? observation = null)
    {
        _observation = observation;
        _addition = new(letCoercionProvider, formatterService);
        _subtraction = new(letCoercionProvider, formatterService);
        _multiplication = new(letCoercionProvider, formatterService);
        _division = new(letCoercionProvider, formatterService);
        _integerDivision = new(letCoercionProvider, formatterService);
        _modulo = new(letCoercionProvider, formatterService);
        _exponent = new(letCoercionProvider, formatterService);
        _concat = new(letCoercionProvider, formatterService);
        _is = new(letCoercionProvider, formatterService);
        _eq = new(letCoercionProvider, formatterService);
        _neq = new(letCoercionProvider, formatterService);
        _gt = new(letCoercionProvider, formatterService);
        _gtEq = new(letCoercionProvider, formatterService);
        _lt = new(letCoercionProvider, formatterService);
        _ltEq = new(letCoercionProvider, formatterService);
        _like = new(letCoercionProvider, formatterService);
        _and = new(letCoercionProvider, formatterService);
        _or = new(letCoercionProvider, formatterService);
        _xor = new(letCoercionProvider, formatterService);
        _eqv = new(letCoercionProvider, formatterService);
        _imp = new(letCoercionProvider, formatterService);
        _negation = new(letCoercionProvider, formatterService);
        _not = new(letCoercionProvider, formatterService);
        _letCoerce = new(letCoercionProvider, formatterService);
        _letAssignment = new(letCoercionProvider, formatterService);
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
            OperatorSymbolNames.BinaryAssignmentValueOp => _letAssignment.EvaluateAtSite(session, new(), expression, ConversionSite.Assignment, left, right),
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
            // MS-VBAL 5.6.6: a pair of parentheses around an expression is an operator, and this is it. Its operation is its operand's conversion.
            OperatorSymbolNames.UnaryLetCoerceOp => _letCoerce.Evaluate(session, new(), expression, operand),
            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };

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
        var result = semantics.EvaluateAtSite(session, new TContext(), expression, site, operands);
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

        return result;
    }
}
