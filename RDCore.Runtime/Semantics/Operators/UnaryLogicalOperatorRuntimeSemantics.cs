using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// Provides <c>virtual</c> overloads to simplify the implementation of <em>unary logical operators</em> runtime semantics.
/// </summary>
public abstract record class UnaryLogicalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider, 
    IVerboseMessageBuilder FormatterService) 
    : UnaryOperatorRuntimeSemantics<UnaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags>(LetCoercionProvider, FormatterService)
{
    /// <summary>
    /// Evaluates the numeric result of a unary logical/bitwise operation.
    /// </summary>
    /// <param name="operand">The underlying managed value of a numeric unary expression operand.</param>
    protected abstract double EvaluateBitwiseOp(double operand);

    protected override OperatorAnalysisContext<LogicalOperatorSemanticFlags> CreateAnalysisContext(
        BoundNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        LogicalOperatorSemanticFlags semanticFlags)
        => new(node.SemanticId, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected sealed override DetermineOperatorEffectiveTypeResult DetermineOperatorEffectiveType(
        ISymbolResolver resolver, 
        VBOperatorExpression<UnaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> expression, 
        OperatorEvaluationFrame frame) => DetermineOperatorEffectiveTypeResult.NotApplicable(); // lets the base semantics handle this.

    /// <summary>
    /// Evaluates the runtime semantics of a unary logical operator and returns a value of the effective numeric data type.
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="symbol">The unary operator expression symbol.</param>
    /// <param name="operand">The unary operand being evaluated.</param>
    /// <returns><c>null</c> if no return value can be evaluated, which would throw a <em>type mismatch</em> error.</returns>
    protected virtual VBTypedValue EvaluateRuntimeSemantics(VBNumericType effectiveType, Symbol symbol, VBNumericTypedValue operand) =>
        VBTypedValueFactory.CreateValue(effectiveType, symbol, EvaluateBitwiseOp(operand.ManagedValue));

    /// <summary>
    /// Evaluates the runtime semantics of a unary logical operator
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="symbol">The unary operator expression symbol.</param>
    /// <param name="operand">The unary operand being evaluated.</param>
    /// <returns><c>null</c> if no return value can be evaluated, which would throw a <em>type mismatch</em> error.</returns>
    protected virtual VBTypedValue EvaluateRuntimeSemantics(VBDateType effectiveType, Symbol symbol, VBNumericTypedValue operand) =>
        VBTypedValueFactory.CreateValue(effectiveType, symbol, EvaluateBitwiseOp(operand.ManagedValue));

    protected virtual VBTypedValue EvaluateRuntimeSemantics(VBNullType effectiveType, Symbol symbol, VBNullValue operand) =>
        VBTypedValueFactory.CreateValue(effectiveType, symbol)!;
}