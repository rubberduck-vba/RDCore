using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Logical;

/// <summary>
/// MS-VBAL 5.6.9.8.2 Binary 'And' Operator
/// </summary>
public record class BinaryAndLogicalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IVerboseMessageBuilder FormatterService) 
    : BinaryLogicalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
{
    protected override double EvaluateBitwiseOp(int lhs, int rhs) => lhs & rhs;

    protected override OperatorAnalysisContext<LogicalOperatorSemanticFlags> CreateAnalysisContext(
        BoundNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        LogicalOperatorSemanticFlags semanticFlags) => new(node.SemanticId, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver,
        SemanticContext<LogicalOperatorSemanticFlags> context,
        VBBinaryOperatorExpression<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame) => DetermineOperatorEffectiveTypeResult.NotApplicable(); // already determined, but the method still needs an override.

    protected override RuntimeSemanticsEvaluationResult EvaluateSemanticallly(
        IVBExecutionContext context, 
        VBBinaryOperatorExpression<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> expression, 
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand];
        var rhs = frame[InputIndex.BinaryRightOperand];

        if (lhs is VBNumericTypedValue lhsNumeric && rhs is VBNullValue)
        {
            if (lhsNumeric.ManagedValue.InteropValue!.Value.Double == 0)
            {
                return RuntimeSemanticsEvaluationResult.Success(
                    VBTypedValueFactory.CreateValue(frame.EffectiveType, expression.ResultSymbol, VBIntegerType.Zero.ManagedValue.InteropValue!.Value));
            }
            else
            {
                return RuntimeSemanticsEvaluationResult.Success(
                    VBTypedValueFactory.CreateNullValue(expression.ResultSymbol));
            }
        }
    
        if (rhs is VBNumericTypedValue rhsNumeric && lhs is VBNullValue)
        {
            if (rhsNumeric.ManagedValue.InteropValue!.Value.Double == 0)
            {
                return RuntimeSemanticsEvaluationResult.Success(
                    VBTypedValueFactory.CreateValue(frame.EffectiveType, expression.ResultSymbol, VBIntegerType.Zero.ManagedValue.InteropValue!.Value));
            }
            else
            {
                return RuntimeSemanticsEvaluationResult.Success(
                    VBTypedValueFactory.CreateNullValue(expression.ResultSymbol));
            }
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }

    protected override ISemanticContextContributor<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> Analyze(
        ISymbolResolver resolver, 
        ConversionOperationSemanticContext coercionContext, 
        ISemanticContextContributor<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> builder, 
        VBOperatorExpression<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> expression, 
        OperatorAnalysisContext<LogicalOperatorSemanticFlags> analysisContext, 
        params VBTypedValue[] operands)
    {
        var lhs = operands[(int)InputIndex.BinaryLeftOperand];
        var rhs = operands[(int)InputIndex.BinaryRightOperand];
        if (lhs.TypeInfo is IIntegralNumericType && rhs.TypeInfo is IIntegralNumericType)
        {
            builder.AddFlags(LogicalOperatorSemanticFlags.IsBitwiseSemantics);
        }
        if (lhs is VBNullValue || rhs is VBNullValue)
        {
            builder.AddFlags(LogicalOperatorSemanticFlags.HasNullOperand);
        }

        return builder.AddFlags(analysisContext.EffectiveTypeResult.Result switch
        {
            VBBooleanType => LogicalOperatorSemanticFlags.BooleanEffectiveType,
            VBByteType => LogicalOperatorSemanticFlags.ByteEffectiveType,
            VBIntegerType => LogicalOperatorSemanticFlags.IntegerEffectiveType,
            VBLongType => LogicalOperatorSemanticFlags.LongEffectiveType,
            VBLongLongType => LogicalOperatorSemanticFlags.LongEffectiveType,
            VBNullType => LogicalOperatorSemanticFlags.NullEffectiveType,
            _ => 0
        });
    }
}
