using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Services.VerboseMessages;

using RDCore.SDK.Model;

namespace RDCore.Runtime.Semantics.Operators.Arithmetic;

/// <summary>
/// MS-VBAL 5.6.9.3.4 Binary '*' Operator (runtime semantics)
/// </summary>
public record class BinaryMultiplicationOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected override T EvaluateManagedNumericOp<T>(T lhs, T rhs) => checked(lhs * rhs);

    protected override string Operator => Tokens.MultiplicationOp;

    protected override RuntimeSemanticsEvaluationResult EvaluateBinaryOperatorExpressionResult(
        ISymbolResolver resolver,
        BinaryArithmeticOperatorSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame) => frame.EffectiveType switch
        {
            VBNumericType numericEffectiveType
                when frame[InputIndex.BinaryLeftOperand] is VBNumericTypedValue lhsNumeric
                  && frame[InputIndex.BinaryRightOperand] is VBNumericTypedValue rhsNumeric
                    => EvaluateBinaryExpressionResult(frame, numericEffectiveType, lhsNumeric, rhsNumeric, expression),

            VBNullType => EvaluateNullBinaryExpressionResult(),

            _ => RuntimeSemanticsEvaluationResult.InternalError()
        };
}
