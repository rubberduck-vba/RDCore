using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
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

    public OperatorRuntimeSemanticsProvider(ILetCoercionRuntimeSemanticsProvider letCoercionProvider, IVerboseMessageBuilder formatterService)
    {
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
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult EvaluateBinaryOperator(IRuntimeSession session, VBBinaryOperatorExpressionNode expression, VBTypedValue left, VBTypedValue right)
        => expression.Token switch
        {
            Tokens.AdditionOp => _addition.Evaluate(session, new(), expression, left, right),
            Tokens.SubtractionOp => _subtraction.Evaluate(session, new(), expression, left, right),
            Tokens.MultiplicationOp => _multiplication.Evaluate(session, new(), expression, left, right),
            Tokens.DivisionOp => _division.Evaluate(session, new(), expression, left, right),
            Tokens.IntegerDivisionOp => _integerDivision.Evaluate(session, new(), expression, left, right),
            Tokens.ModuloOp => _modulo.Evaluate(session, new(), expression, left, right),
            Tokens.PowerOp => _exponent.Evaluate(session, new(), expression, left, right),
            Tokens.ConcatOp => _concat.Evaluate(session, new(), expression, left, right),
            Tokens.CompareIsOp => _is.Evaluate(session, new(), expression, left, right),
            Tokens.CompareEqualOp => _eq.Evaluate(session, new(), expression, left, right),
            Tokens.CompareNotEqualOp => _neq.Evaluate(session, new(), expression, left, right),
            Tokens.CompareGreaterThanOp => _gt.Evaluate(session, new(), expression, left, right),
            Tokens.CompareGreaterThanOrEqualOp => _gtEq.Evaluate(session, new(), expression, left, right),
            Tokens.CompareLessThanOp => _lt.Evaluate(session, new(), expression, left, right),
            Tokens.CompareLessThanOrEqualOp => _ltEq.Evaluate(session, new(), expression, left, right),
            Tokens.CompareLikeOp => _like.Evaluate(session, new(), expression, left, right),
            Tokens.LogicalAndOp => _and.Evaluate(session, new(), expression, left, right),
            Tokens.LogicalOrOp => _or.Evaluate(session, new(), expression, left, right),
            Tokens.LogicalXOrOp => _xor.Evaluate(session, new(), expression, left, right),
            Tokens.LogicalEqvOp => _eqv.Evaluate(session, new(), expression, left, right),
            Tokens.LogicalImpOp => _imp.Evaluate(session, new(), expression, left, right),
            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult EvaluateUnaryOperator(IRuntimeSession session, VBUnaryOperatorExpressionNode expression, VBTypedValue operand)
        => expression.Token switch
        {
            Tokens.NegationOp => _negation.Evaluate(session, new(), expression, operand),
            Tokens.LogicalNotOp => _not.Evaluate(session, new(), expression, operand),
            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };
}
