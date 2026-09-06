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
using System.Numerics;

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
    /// Evaluates the bitwise result of a unary logical operation in the effective type's own representation.
    /// </summary>
    /// <typeparam name="T">The CLR representation of the operation's <em>effective integral type</em>.</typeparam>
    /// <param name="operand">The managed value of a unary expression operand, in the operation's effective type.</param>
    protected abstract T EvaluateBitwiseOp<T>(T operand) where T : IBinaryInteger<T>;

    protected override OperatorAnalysisContext<LogicalOperatorSemanticFlags> CreateAnalysisContext(
        SyntaxNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        LogicalOperatorSemanticFlags semanticFlags)
        => new(node.Identity, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected sealed override DetermineOperatorEffectiveTypeResult DetermineOperatorEffectiveType(
        ISymbolResolver resolver, 
        VBOperatorExpression expression, 
        OperatorEvaluationFrame frame) => DetermineOperatorEffectiveTypeResult.NotApplicable(); // lets the base semantics handle this.

    /// <summary>
    /// Evaluates the runtime semantics of a unary logical operator and returns a value of the effective numeric data type.
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="operand">The unary operand being evaluated.</param>
    /// <returns><c>null</c> if no return value can be evaluated, which would throw a <em>type mismatch</em> error.</returns>
    protected virtual VBTypedValue EvaluateRuntimeSemantics(VBNumericType effectiveType, VBNumericTypedValue operand) =>
        effectiveType switch
        {
            VBByteType => new VBByteValue(EvaluateBitwiseOp(((VBByteValue)operand).Value)),
            VBIntegerType => new VBIntegerValue(EvaluateBitwiseOp(((VBIntegerValue)operand).Value)),
            VBLongType => new VBLongValue(EvaluateBitwiseOp(((VBLongValue)operand).Value)),
            VBLongLongType => new VBLongLongValue(EvaluateBitwiseOp(((VBLongLongValue)operand).Value)),
            _ => throw new NotSupportedException($"Effective type '{effectiveType.Name}' is not a supported logical/bitwise type."),
        };

    /// <summary>
    /// Evaluates the runtime semantics of a unary logical operator
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="operand">The unary operand being evaluated.</param>
    /// <returns><c>null</c> if no return value can be evaluated, which would throw a <em>type mismatch</em> error.</returns>
    protected virtual VBTypedValue EvaluateRuntimeSemantics(VBDateType effectiveType, VBNumericTypedValue operand) =>
        new VBDateValue(EvaluateBitwiseOp((int)operand.AsDouble));

    protected virtual VBTypedValue EvaluateRuntimeSemantics(VBNullType effectiveType, VBNullValue operand) =>
        effectiveType.DefaultValue;
}