using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Services.VerboseMessages;
using System.Numerics;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// Provides <c>virtual</c> overloads to simplify the implementation of <em>unary arithmetic operators</em> runtime semantics.
/// </summary>
public abstract record class UnaryArithmeticOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider, 
    IVerboseMessageBuilder FormatterService) 
    : UnaryOperatorRuntimeSemantics<UnaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags>(LetCoercionProvider, FormatterService)
{
    protected sealed override ISemanticContextContributor<UnaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> AnalyzeEffectiveType(
        ISemanticContextContributor<UnaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> builder,
        DetermineOperatorEffectiveTypeResult context)
    {
        builder.AddOnError(context.ErrorInfo);
        return context.Result switch
        {
            VBNumericType => builder.AddFlags(ArithmeticOperatorSemanticFlags.VBNumericEffectiveType),
            VBDateType => builder.AddFlags(ArithmeticOperatorSemanticFlags.VBDateEffectiveType),
            VBNullType => builder.AddFlags(ArithmeticOperatorSemanticFlags.VBNullEffectiveType),
            _ => builder
        };
    }

    protected override OperatorAnalysisContext<ArithmeticOperatorSemanticFlags> CreateAnalysisContext(
        SyntaxNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        ArithmeticOperatorSemanticFlags semanticFlags) 
        => new(node.Identity, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    /// <summary>
    /// Evaluates the runtime semantics of a unary arithmetic operator, computing the result in the
    /// effective type's own CLR representation. Integral overflow (e.g. negating the minimum value of
    /// a signed integral type) is surfaced as <see cref="VBRuntimeErrorId.Overflow"/>.
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="operand">The unary operand being evaluated, already let-coerced to <paramref name="effectiveType"/>.</param>
    /// <param name="expression">The unary operator expression, for error attribution.</param>
    protected virtual RuntimeSemanticsEvaluationResult EvaluateRuntimeSemantics(VBNumericType effectiveType, VBNumericTypedValue operand, ExpressionNode expression)
    {
        try
        {
            VBTypedValue result = effectiveType switch
            {
                VBByteType => new VBByteValue(EvaluateNumericOp(((VBByteValue)operand).Value)),
                VBIntegerType => new VBIntegerValue(EvaluateNumericOp(((VBIntegerValue)operand).Value)),
                VBLongType => new VBLongValue(EvaluateNumericOp(((VBLongValue)operand).Value)),
                VBLongLongType => new VBLongLongValue(EvaluateNumericOp(((VBLongLongValue)operand).Value)),
                VBSingleType => new VBSingleValue(EvaluateNumericOp(((VBSingleValue)operand).Value)),
                VBDoubleType => new VBDoubleValue(EvaluateNumericOp(((VBDoubleValue)operand).Value)),
                VBCurrencyType => new VBCurrencyValue(EvaluateNumericOp(((VBCurrencyValue)operand).Value.Value)),
                VBDecimalType => new VBDecimalValue(EvaluateNumericOp(((VBDecimalValue)operand).Value)),
                _ => throw new NotSupportedException($"Effective type '{effectiveType.Name}' is not a supported arithmetic numeric type."),
            };
            return RuntimeSemanticsEvaluationResult.Success(result);
        }
        catch (OverflowException)
        {
            return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.Overflow, expression, Exceptions.VBRuntimeError_ArithmeticOverflow));
        }
    }

    /// <summary>
    /// Evaluates the runtime semantics of a unary arithmetic operator whose effective type is <see cref="VBDateType"/>.<br/>
    /// 👉 the operand has been let-coerced to a <see cref="VBDoubleValue"/> during validation.
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="operand">The unary operand being evaluated.</param>
    /// <param name="expression">The unary operator expression, for error attribution.</param>
    protected virtual RuntimeSemanticsEvaluationResult EvaluateRuntimeSemantics(VBDateType effectiveType, VBNumericTypedValue operand, ExpressionNode expression)
        => RuntimeSemanticsEvaluationResult.Success(new VBDateValue(EvaluateNumericOp(((VBDoubleValue)operand).Value)));

    /// <summary>
    /// Evaluates the numeric result of a unary arithmetic operation in the effective type's own representation.
    /// </summary>
    /// <typeparam name="T">The CLR representation of the operation's <em>effective numeric type</em>.</typeparam>
    /// <param name="operand">The managed value of a numeric unary expression operand, in the operation's effective type.</param>
    protected abstract T EvaluateNumericOp<T>(T operand) where T : INumber<T>;
}
