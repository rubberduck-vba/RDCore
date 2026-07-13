using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
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
        BoundNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        ArithmeticOperatorSemanticFlags semanticFlags) 
        => new(node.SemanticId, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    /// <summary>
    /// Evaluates the runtime semantics of a unary arithmetic operator and returns a value of the effective numeric data type.
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="symbol">The unary operator expression symbol.</param>
    /// <param name="operand">The unary operand being evaluated.</param>
    /// <returns><c>null</c> if no return value can be evaluated, which would throw a <em>type mismatch</em> error.</returns>
    protected virtual VBTypedValue EvaluateRuntimeSemantics(VBNumericType effectiveType, Symbol symbol, VBNumericTypedValue operand) 
        => VBTypedValueFactory.CreateValue(effectiveType, symbol, EvaluateNumericOp(operand.ManagedValue.InteropValue!.Value.Double));

    /// <summary>
    /// Evaluates the runtime semantics of a unary arithmetic operator
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="symbol">The unary operator expression symbol.</param>
    /// <param name="operand">The unary operand being evaluated.</param>
    /// <returns><c>null</c> if no return value can be evaluated, which would throw a <em>type mismatch</em> error.</returns>
    protected virtual VBTypedValue EvaluateRuntimeSemantics(VBDateType effectiveType, Symbol symbol, VBNumericTypedValue operand) 
        => VBTypedValueFactory.CreateValue(effectiveType, symbol, EvaluateNumericOp(operand.ManagedValue.InteropValue!.Value.Double));

    /// <summary>
    /// Evaluates the numeric result of a unary arithmetic operation.
    /// </summary>
    /// <param name="operand">The underlying managed value of a numeric unary expression operand.</param>
    protected abstract double EvaluateNumericOp(double operand);
}
