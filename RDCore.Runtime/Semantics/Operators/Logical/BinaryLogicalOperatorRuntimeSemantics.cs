using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Values.Meta;
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
using System.Numerics;

namespace RDCore.Runtime.Semantics.Operators.Logical;

/// <summary>
/// <strong>MS-VBAL 5.6.9.8 Logical Operators</strong><br/>
/// 👉 Logical operators are <em>simple data operators</em> 
/// that perform <strong>bitwise computations</strong> on their operands.
/// </summary>
public abstract record class BinaryLogicalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider, 
    IVerboseMessageBuilder FormatterService)
    : BinaryOperatorRuntimeSemantics<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags>(LetCoercionSemanticsProvider, FormatterService)
{
    /// <summary>
    /// Evaluates the bitwise result of a binary logical operation in the effective type's own representation.
    /// </summary>
    /// <typeparam name="T">The CLR representation of the operation's <em>effective integral type</em>.</typeparam>
    /// <param name="lhs">The managed value of the left-hand side (LHS) operand, in the operation's effective type.</param>
    /// <param name="rhs">The managed value of the right-hand side (RHS) operand, in the operation's effective type.</param>
    protected abstract T EvaluateBitwiseOp<T>(T lhs, T rhs) where T : IBinaryInteger<T>;

    /// <summary>
    /// Computes the bitwise result in the effective type's own CLR representation, dispatching
    /// <see cref="EvaluateBitwiseOp{T}(T, T)"/> on the effective integral type. Operands have already
    /// been let-coerced to <paramref name="effectiveType"/> by the evaluation pipeline.
    /// </summary>
    protected RuntimeSemanticsEvaluationResult EvaluateBitwise(VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        VBTypedValue result = effectiveType switch
        {
            VBBooleanType => new VBBooleanValue(EvaluateBitwiseOp(BooleanBits(lhs), BooleanBits(rhs)) != 0),
            VBByteType => new VBByteValue(EvaluateBitwiseOp(((VBByteValue)lhs).Value, ((VBByteValue)rhs).Value)),
            VBIntegerType => new VBIntegerValue(EvaluateBitwiseOp(((VBIntegerValue)lhs).Value, ((VBIntegerValue)rhs).Value)),
            VBLongType => new VBLongValue(EvaluateBitwiseOp(((VBLongValue)lhs).Value, ((VBLongValue)rhs).Value)),
            VBLongLongType => new VBLongLongValue(EvaluateBitwiseOp(((VBLongLongValue)lhs).Value, ((VBLongLongValue)rhs).Value)),
            _ => throw new NotSupportedException($"Effective type '{effectiveType.Name}' is not a supported logical/bitwise type."),
        };
        return RuntimeSemanticsEvaluationResult.Success(result);
    }

    // VBA Boolean is bitwise over its -1 / 0 representation.
    private static int BooleanBits(VBTypedValue value) => value switch
    {
        VBBooleanValue b => b.Value.StoredValue != 0 ? -1 : 0,
        VBNumericTypedValue n => n.AsDouble != 0 ? -1 : 0,
        _ => 0,
    };

    protected override OperatorAnalysisContext<LogicalOperatorSemanticFlags> CreateAnalysisContext(
        SyntaxNode node, 
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult, 
        LetCoercionAnalysisContext coercionResult, 
        RuntimeSemanticsEvaluationResult evaluationResult, 
        LogicalOperatorSemanticFlags semanticFlags) => new(node.Identity, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver,
        BinaryLogicalOperatorSemanticContext context, 
        VBBinaryOperatorExpressionNode expression, 
        OperatorEvaluationFrame frame)
        => frame[InputIndex.BinaryLeftOperand].TypeInfo switch
        {
            VBByteType or VBNullType when frame[InputIndex.BinaryLeftOperand].TypeInfo is VBByteType 
                => DetermineOperatorEffectiveTypeResult.Success(VBByteType.TypeInfo),

            _ => DetermineOperatorEffectiveTypeResult.NotApplicable()
        };

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        ISymbolResolver resolver,
        BinaryLogicalOperatorSemanticContext context, 
        VBBinaryOperatorExpressionNode expression, 
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand];
        var rhs = frame[InputIndex.BinaryRightOperand];

        // both operands are integral (or Boolean): a plain bitwise computation in the effective type.
        // the evaluation pipeline has already let-coerced them to it.
        if (lhs.TypeInfo is IIntegralNumericType or VBBooleanType && rhs.TypeInfo is IIntegralNumericType or VBBooleanType)
        {
            return EvaluateBitwise(frame.EffectiveType, lhs, rhs);
        }
        else if (lhs is VBNullValue && rhs is VBNullValue)
        {
            return EvaluateNullBinaryExpressionResult();
        }

        return EvaluateSemanticallly(resolver, expression, frame);
    }

    /// <summary>
    /// Evaluates the not-bitwise evaluation branches of the MS-VBAL specifications for a logical operator.
    /// </summary>
    /// <remarks>
    /// Operands are <strong>explicitly specified</strong> as being evaluated bitwise only given specific operand data types.
    /// Base implementation has already handled the case where both operands are <see cref="IIntegralNumericType"/>, and the case where they're both <see cref="VBNullValue"/>.
    /// </remarks>
    protected abstract RuntimeSemanticsEvaluationResult EvaluateSemanticallly(
        ISymbolResolver resolver, 
        VBBinaryOperatorExpressionNode expression, 
        OperatorEvaluationFrame frame);

    protected override ISemanticContextContributor<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> Analyze(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext coercionContext,
        ISemanticContextContributor<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> builder,
        VBOperatorExpression expression,
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
