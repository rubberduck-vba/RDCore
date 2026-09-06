using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Arithmetic;

/// <summary>
/// MS-VBAL 5.6.9.3.6 Binary '\' Operator and 'Mod' Operator (runtime semantics)
/// </summary>
public sealed record class BinaryModuloOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryIntegerDivisionOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected override double EvaluateManagedNumericOp(double lhs, double rhs) => Math.DivRem((int)lhs, (int)rhs).Remainder;
    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime,
        BinaryArithmeticOperatorSemanticContext context, 
        VBBinaryOperatorExpressionNode expression, 
        OperatorEvaluationFrame frame)
    {
        if (frame.EffectiveType is VBByteType or VBIntegerType or VBLongType or VBLongLongType
            && frame[InputIndex.BinaryLeftOperand] is VBNumericTypedValue lhsNumeric 
            && frame[InputIndex.BinaryRightOperand] is VBNumericTypedValue rhsNumeric)
        {
            var lhs = (double)lhsNumeric.Handle.GetValue(runtime).BoxedValue;
            var rhs = (double)rhsNumeric.Handle.GetValue(runtime).BoxedValue;

            if (rhs == 0d)
            {
                OnDivisionByZero(expression, Exceptions.VBDivisionOp_DivisionByZero);
            }

            return RuntimeSemanticsEvaluationResult.Success(
                RuntimeNumericValue.Of((VBNumericType)frame.EffectiveType, 
                EvaluateManagedNumericOp(lhs, rhs)));
        }
        else if (frame.EffectiveType is VBNullType)
        {
            return EvaluateNullBinaryExpressionResult();
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }
}
