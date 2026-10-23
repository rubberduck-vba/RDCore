using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Binding;
using RDCore.SDK.Semantics.Plans;

namespace RDCore.Runtime.Semantics;

// IR lowering S1.2: expressions of numeric scalar variables evaluated on payloads (ScalarPayload), with no value made of what they evaluate to. A node the
// payload evaluation does not do - or one that would raise an error - declines it, and the boxed evaluation evaluates the expression as it always has: the
// nodes it does are literals, variables and operators, which can be evaluated again with no other effect, so the error the boxed evaluation raises is the
// one the expression raises.
public sealed partial class RuntimeExpressionEvaluator
{
    /// <summary>
    /// Let-assigns <paramref name="value"/> to <paramref name="target"/> on payloads, when the target is a variable that holds a payload and the value is an
    /// expression evaluated on them.
    /// </summary>
    /// <returns><see langword="false"/> when the assignment is not done here, and is to be done the way it always is.</returns>
    internal bool TryAssignUnboxed(IRuntimeSession session, ExpressionNode target, ExpressionNode value, RuntimeEvaluationContext context)
    {
        if (!UnboxedEvaluation.IsEnabled || Observation is not null || AssumesVariables
            || BoundVariableOf(session, target, context) is not { } variable
            || BindingOf(session, variable) is not (ScalarCell cell, null)
            || !TryEvaluatePayload(session, BoundTreeOf(session, value, context), out var payload)
            || !TryConvertPayload(payload, cell.Type, out var converted))
        {
            return false;
        }

        cell.Write(converted);
        return true;
    }

    private bool TryEvaluatePayload(IRuntimeSession session, BoundNode node, out ScalarPayload payload)
    {
        payload = default;
        switch (node)
        {
            case BoundLiteral literal:
                return ScalarPayload.TryOf(literal.Literal.StaticValue.TypeInfo, literal.Literal.StaticValue.RuntimeValue, out payload);
            case BoundVariable variable:
                return BindingOf(session, variable) is (ScalarCell cell, null) && cell.TryRead(out payload);
            case BoundUnary { Plan: { IsDeferred: false, EffectiveType: { } effective } plan, Operand: var operand } when plan.Operator == Tokens.NegationOp:
                return TryEvaluatePayload(session, operand, out var negated) && TryConvertPayload(negated, effective, out var operandValue)
                    && TryNegate(operandValue, out payload);
            case BoundBinary { Plan: { IsDeferred: false, OperandDestination: { } destination } plan, Left: var left, Right: var right }:
                return TryEvaluatePayload(session, left, out var lhs) && TryEvaluatePayload(session, right, out var rhs)
                    && TryConvertPayload(lhs, destination, out var l) && TryConvertPayload(rhs, destination, out var r)
                    && TryOperate(plan.Operator, plan.EffectiveType!, l, r, out payload);
            default:
                return false;
        }
    }

    // the conversions that are exact - what a value of one numeric scalar type is in another, with no rounding to decide: an integer in the range of an
    // integer type, an integer as a floating-point value, a Single as a Double. Anything else is the boxed coercion's (MS-VBAL §5.5.1.2.1), which rounds.
    private static bool TryConvertPayload(ScalarPayload payload, VBType destination, out ScalarPayload converted)
    {
        converted = payload;
        if (ReferenceEquals(payload.Type, destination) || payload.Type.Equals(destination))
        {
            return true;
        }

        return (payload.IsIntegral, destination) switch
        {
            (true, VBByteType or VBIntegerType or VBLongType or VBLongLongType) => ScalarPayload.TryIntegral(destination, payload.Integral, out converted),
            (true, VBDoubleType) => ScalarPayload.TryFloating(destination, payload.Integral, out converted),
            (true, VBSingleType) when payload.Type is not VBLongLongType => ScalarPayload.TryFloating(destination, payload.Integral, out converted),
            (false, VBDoubleType) => ScalarPayload.TryFloating(destination, payload.Floating, out converted),
            _ => false,
        };
    }

    // MS-VBAL §5.6.9.3: the operators on their effective type, as the boxed evaluation does them (checked, in the type's own width); a result that does not
    // fit, a division by zero and a result that is not finite are errors, which the boxed evaluation raises.
    private static bool TryOperate(string token, VBType effective, ScalarPayload l, ScalarPayload r, out ScalarPayload result)
    {
        result = default;
        try
        {
            return (effective, token) switch
            {
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.AdditionOp) => ScalarPayload.TryIntegral(effective, checked(l.Integral + r.Integral), out result),
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.SubtractionOp) => ScalarPayload.TryIntegral(effective, checked(l.Integral - r.Integral), out result),
                (VBByteType or VBIntegerType or VBLongType or VBLongLongType, Tokens.MultiplicationOp) => ScalarPayload.TryIntegral(effective, checked(l.Integral * r.Integral), out result),
                (VBSingleType, Tokens.AdditionOp) => ScalarPayload.TryFloating(effective, (float)l.Floating + (float)r.Floating, out result),
                (VBSingleType, Tokens.SubtractionOp) => ScalarPayload.TryFloating(effective, (float)l.Floating - (float)r.Floating, out result),
                (VBSingleType, Tokens.MultiplicationOp) => ScalarPayload.TryFloating(effective, (float)l.Floating * (float)r.Floating, out result),
                (VBSingleType, Tokens.DivisionOp) when r.Floating != 0 => ScalarPayload.TryFloating(effective, (float)l.Floating / (float)r.Floating, out result),
                (VBDoubleType, Tokens.AdditionOp) => ScalarPayload.TryFloating(effective, l.Floating + r.Floating, out result),
                (VBDoubleType, Tokens.SubtractionOp) => ScalarPayload.TryFloating(effective, l.Floating - r.Floating, out result),
                (VBDoubleType, Tokens.MultiplicationOp) => ScalarPayload.TryFloating(effective, l.Floating * r.Floating, out result),
                (VBDoubleType, Tokens.DivisionOp) when r.Floating != 0 => ScalarPayload.TryFloating(effective, l.Floating / r.Floating, out result),
                _ => false,
            };
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryNegate(ScalarPayload operand, out ScalarPayload result)
    {
        result = default;
        if (!operand.IsIntegral)
        {
            return ScalarPayload.TryFloating(operand.Type, -operand.Floating, out result);
        }

        // the negation of the least value of an integer type is not in its range (MS-VBA: Overflow), which the boxed evaluation raises.
        return operand.Integral != long.MinValue && ScalarPayload.TryIntegral(operand.Type, -operand.Integral, out result);
    }
}
