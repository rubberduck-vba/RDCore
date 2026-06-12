using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract.Operators;
using RDCore.SDK.Semantics.Runtime.LetCoercion;
using RDCore.SDK.Semantics.Runtime.Operators.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.Operators.Semantics.Logical;

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

    /// <summary>
    /// Creates and returns the specific <see cref="OperatorAnalysisContext{TFlags}"/> instance for this operator.
    /// </summary>
    /// <param name="node">The <em>operator expression bound node</em>.</param>
    /// <param name="determineOperatorEffectiveTypeResult">The result of the <em>determine effective type</em> (first) evaluation step.</param>
    /// <param name="coercionResult">The result of the <em>validate operand data types</em> (second) evaluation step.</param>
    /// <param name="evaluationResult">The result of the <em>evaluate result</em> (third/last) evaluation step.</param>
    /// <param name="semanticFlags">The <em>semantic flags</em> associated with this operation evaluation.</param>
    protected override OperatorAnalysisContext<LogicalOperatorSemanticFlags> CreateAnalysisContext(
        BoundNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        LogicalOperatorSemanticFlags semanticFlags)
        => new(node.SemanticId, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    /// <summary>
    /// Determines the <em>effective type</em> of an operation based on the data type of its operands.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context..</param>
    /// <param name="expression">The <em>expression node</em> to analyze.</param>
    /// <param name="frame">The evaluation frame of the operator expression.</param>
    /// <returns><strong>Does not throw exceptions.</strong> Returns <c>DetermineOperatorEffectiveTypeResult.NotApplicable</c> if no type is statically valid.</returns>
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
    /// Evaluates the runtime semantics of a unary logical operator given a <see cref="VBDateType"/> effective type.
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="symbol">The unary operator expression symbol.</param>
    /// <param name="operand">The unary operand being evaluated.</param>
    /// <returns>A <see cref="VBDateValue"/> associated with the specified symbol.</returns>
    protected virtual VBTypedValue EvaluateRuntimeSemantics(VBDateType effectiveType, Symbol symbol, VBNumericTypedValue operand) =>
        VBTypedValueFactory.CreateValue(effectiveType, symbol, EvaluateBitwiseOp(operand.ManagedValue));

    /// <summary>
    /// Evaluates the runtime semantics of a unary logical operator given a <see cref="VBNullValue"/> operand.
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="symbol">The unary operator expression symbol.</param>
    /// <param name="operand">The unary operand being evaluated.</param>
    /// <returns>A <see cref="VBNullValue"/> associated with the specified symbol.</returns>
    protected virtual VBTypedValue EvaluateRuntimeSemantics(VBNullType effectiveType, Symbol symbol, VBNullValue operand) =>
        VBTypedValueFactory.CreateValue(effectiveType, symbol)!;
}