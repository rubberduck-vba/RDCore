using RDCore.Runtime.Execution.Frames;
using RDCore.SDK;
using RDCore.SDK.Model.Values.Meta;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
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

    /// <summary>
    /// The operand's value for the MS-VBAL 5.6.9.8 binary Null-operand tables, which treat Boolean
    /// as an integral value via its -1/0 representation (RD-VBAL §5.0.2.1) — the same convention
    /// <see cref="BooleanBits"/> already applies for the both-integral bitwise fast path.
    /// <see langword="null"/> for any operand the tables don't classify as integral.
    /// </summary>
    protected static double? AsNullOperandTableValue(VBTypedValue value) => value switch
    {
        VBBooleanValue b => b.Value.StoredValue != 0 ? -1d : 0d,
        VBNumericTypedValue n when n.TypeInfo is IIntegralNumericType => n.AsDouble,
        _ => null,
    };

    /// <summary>
    /// Builds a MS-VBAL 5.6.9.8 Null-operand-table result in the operation's effective type — a
    /// <see cref="VBBooleanValue"/> for a Boolean effective type (Boolean is Let-coerced to Integer
    /// for the bitwise step, but the operator's result is Let-coerced back), the effective
    /// <see cref="VBNumericType"/>'s own representation of <paramref name="value"/> otherwise.
    /// </summary>
    protected static VBTypedValue CreateNullOperandTableResult(VBType effectiveType, double value) =>
        effectiveType is VBBooleanType ? new VBBooleanValue(value != 0) : ((VBNumericType)effectiveType).CreateValue(value);

    protected override OperatorAnalysisContext<LogicalOperatorSemanticFlags> CreateAnalysisContext(
        SyntaxNode node, 
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult, 
        LetCoercionAnalysisContext coercionResult, 
        RuntimeSemanticsEvaluationResult evaluationResult, 
        LogicalOperatorSemanticFlags semanticFlags) => new(node.Identity, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver,
        BinaryLogicalOperatorSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand].GetTargetType();
        var rhs = frame[InputIndex.BinaryRightOperand].GetTargetType();

        // MS-VBAL 5.6.9.8: logical operators are first resolved as simple data operators; a dedicated
        // table applies when either operand is Null. The effective value type is always Byte, Boolean,
        // Integer, Long, LongLong, Variant or Null — floating-/fixed-point and Date operands resolve
        // to Long (or LongLong), never to their own type.
        var effectiveType = (lhs, rhs) switch
        {
            (VBByteType, VBByteType or VBNullType) or (VBNullType, VBByteType)
                => VBByteType.TypeInfo,

            // Boolean stays Boolean; operands are let-coerced to Integer for the bitwise step.
            (VBBooleanType, VBBooleanType or VBNullType) or (VBNullType, VBBooleanType)
                => VBBooleanType.TypeInfo,

            (VBByteType or VBBooleanType or VBIntegerType or VBEmptyType or VBNullType,
                VBByteType or VBBooleanType or VBIntegerType or VBEmptyType)
                or (VBByteType or VBBooleanType or VBIntegerType or VBEmptyType,
                    VBByteType or VBBooleanType or VBIntegerType or VBEmptyType or VBNullType)
                => VBIntegerType.TypeInfo,

            (VBLongLongType, INumericType or VBStringType or VBFixedStringType or VBDateType or VBEmptyType or VBNullType)
                or (INumericType or VBStringType or VBFixedStringType or VBDateType or VBEmptyType or VBNullType, VBLongLongType)
                => VBLongLongType.TypeInfo,

            (IFloatingPointNumericType or IFixedPointNumericType or VBLongType or VBStringType or VBFixedStringType or VBDateType,
                (INumericType and not VBLongLongType) or VBStringType or VBFixedStringType or VBDateType or VBEmptyType or VBNullType)
                or ((INumericType and not VBLongLongType) or VBStringType or VBFixedStringType or VBDateType or VBEmptyType or VBNullType,
                    IFloatingPointNumericType or IFixedPointNumericType or VBLongType or VBStringType or VBFixedStringType or VBDateType)
                => VBLongType.TypeInfo,

            (VBNullType, VBNullType) => VBNullType.TypeInfo,

            (VBVariantType, not (VBArrayType or VBUserDefinedType)) or (not (VBArrayType or VBUserDefinedType), VBVariantType)
                => VBVariantType.TypeInfo,

            _ => (VBType?)null,
        };

        return effectiveType is not null
            ? DetermineOperatorEffectiveTypeResult.Success(effectiveType)
            : DetermineOperatorEffectiveTypeResult.Error(OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression,
                Exceptions.VBRuntimeTypeMismatch_OperationEffectiveType_Verbose.Replace("{$OPERANDS}", string.Join(", ", [lhs.Name, rhs.Name]))));
    }

    protected override RuntimeSemanticsEvaluationResult EvaluateBinaryOperatorExpressionResult(
        ISymbolResolver resolver,
        BinaryLogicalOperatorSemanticContext context, 
        ExpressionNode expression, 
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
        ExpressionNode expression, 
        OperatorEvaluationFrame frame);

    protected override ISemanticContextContributor<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> Analyze(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext coercionContext,
        ISemanticContextContributor<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> builder,
        ExpressionNode expression,
        OperatorAnalysisContext<LogicalOperatorSemanticFlags> analysisContext,
        params VBTypedValue[] operands)
    {
        return LogicalOperatorAnalysis.Analyze(builder, analysisContext.EffectiveTypeResult, operands);
    }
}
