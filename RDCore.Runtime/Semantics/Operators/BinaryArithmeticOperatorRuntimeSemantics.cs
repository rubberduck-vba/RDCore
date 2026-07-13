using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
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
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3 Arithmetic Operators</strong><br/>
/// 👉 Arithmetic operators are <em>simple data operators</em> that perform <strong>numerical computations</strong> on their operands.
/// </summary>
public abstract record class BinaryArithmeticOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider, 
    IVerboseMessageBuilder FormatterService)
    : BinaryOperatorRuntimeSemantics<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags>(LetCoercionProvider, FormatterService)
{
    /// <summary>
    /// Evaluates the numeric result of a binary arithmetic operation.
    /// </summary>
    /// <param name="lhs">The underlying managed value of the left-hand side (LHS) numeric binary expression operand.</param>
    /// <param name="rhs">The underlying managed value of the right-hand side (RHS) numeric binary expression operand.</param>
    /// <remarks>
    /// 👉 This method is templated by the <see cref="EvaluateBinaryExpressionResult"/> overloads.
    /// </remarks>
    protected abstract double EvaluateManagedNumericOp(double lhs, double rhs);

    protected sealed override OperatorAnalysisContext<ArithmeticOperatorSemanticFlags> CreateAnalysisContext(
        BoundNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        ArithmeticOperatorSemanticFlags semanticFlags)
        => new(node.SemanticId, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected abstract DetermineOperatorEffectiveTypeResult DetermineArithmeticOperatorEffectiveType(
        ISymbolResolver resolver,
        BinaryArithmeticOperatorSemanticContext context,
        VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame);

    protected sealed override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver, 
        SemanticContext<ArithmeticOperatorSemanticFlags> context, 
        VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame)
    {
        var result = DetermineArithmeticOperatorEffectiveType(resolver, (BinaryArithmeticOperatorSemanticContext)context, expression, frame);
        if (result.IsApplicable)
        {
            // a more specialized implementation is in charge.
            return result;
        }

        var rhsType = frame[InputIndex.BinaryRightOperand].TypeInfo;
        // the base rules are the verbatim specifications for
        // binary operators (5.6.9.3 runtime semantics):
        var effectiveType = frame[InputIndex.BinaryLeftOperand].TypeInfo switch
        {
            VBByteType when rhsType is VBByteType or VBEmptyType => VBByteType.TypeInfo,
            VBByteType or VBEmptyType when rhsType is VBByteType => VBByteType.TypeInfo,

            VBBooleanType or VBIntegerType when rhsType is VBByteType or VBBooleanType or VBIntegerType or VBEmptyType => VBIntegerType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBEmptyType when rhsType is VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,
            VBEmptyType when rhsType is VBEmptyType => VBIntegerType.TypeInfo,

            VBLongType when rhsType is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBEmptyType => VBLongType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBEmptyType when rhsType is VBLongType => VBLongType.TypeInfo,

            VBLongLongType when rhsType is IIntegralNumericType or VBEmptyType => VBLongLongType.TypeInfo,
            IIntegralNumericType or VBEmptyType when rhsType is VBLongLongType => VBLongLongType.TypeInfo,

            VBSingleType when rhsType is VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBEmptyType => VBSingleType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBEmptyType when rhsType is VBSingleType => VBSingleType.TypeInfo,

            VBSingleType when rhsType is VBLongType or VBLongLongType => VBDoubleType.TypeInfo,
            VBLongType or VBLongLongType when rhsType is VBSingleType => VBDoubleType.TypeInfo,
            VBDoubleType or VBStringType when rhsType is IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBEmptyType => VBDoubleType.TypeInfo,
            IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBEmptyType when rhsType is VBDoubleType or VBStringType => VBDoubleType.TypeInfo,

            VBCurrencyType when rhsType is IIntegralNumericType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBEmptyType => VBCurrencyType.TypeInfo,
            IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBEmptyType when rhsType is VBCurrencyType => VBCurrencyType.TypeInfo,

            // date values are let-coerced to VBDoubleValue
            VBDateType when rhsType is IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBDateType or VBEmptyType => VBDateType.TypeInfo,
            IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBDateType or VBEmptyType when rhsType is VBDateType => VBDateType.TypeInfo,

            VBDecimalType when rhsType is INumericType or VBCurrencyType or VBStringType or VBEmptyType => VBCurrencyType.TypeInfo,
            INumericType or VBStringType or VBEmptyType when rhsType is VBCurrencyType => VBCurrencyType.TypeInfo,

            VBNullType when rhsType is INumericType or VBStringType or VBDateType or VBEmptyType or VBNullType => VBNullType.TypeInfo,
            INumericType or VBStringType or VBDateType or VBEmptyType or VBNullType when rhsType is VBNullType => VBNullType.TypeInfo,

            VBErrorType when rhsType is INumericType or VBStringType or VBDateType or VBEmptyType or VBErrorType => VBErrorType.TypeInfo,
            INumericType or VBStringType or VBDateType or VBEmptyType or VBErrorType when rhsType is VBErrorType => VBErrorType.TypeInfo,

            _ => (VBType?)default
        };

        return effectiveType is not null
            ? DetermineOperatorEffectiveTypeResult.Success(effectiveType)
            // if no effective type can be determined, it's a type mismatch error:
            : DetermineOperatorEffectiveTypeResult.Error(OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression,
                Exceptions.VBRuntimeTypeMismatch_OperationEffectiveType_Verbose.Replace("{$OPERANDS}", string.Join(", ", [frame[InputIndex.BinaryLeftOperand].TypeInfo.Name, rhsType.Name]))));
    }

    /// <summary>
    /// Evaluates the <see cref="VBNumericType"/> runtime semantics of a <em>binary arithmetic operator</em> 
    /// </summary>
    /// <param name="effectiveType">The determined <em>effective data type</em> of the <em>arithmetic operation</em>.</param>
    /// <param name="symbol">The <c>ResultSymbol</c> of the <em>binary arithmetic operator expression</em>.</param>
    /// <param name="lhs">The resolved <see cref="VBNumericTypedValue"/> of the left-hand side (LHS) <em>numeric binary expression</em> operand.</param>
    /// <param name="rhs">The resolved <see cref="VBNumericTypedValue"/> of the right-hand side (RHS) <em>numeric binary expression</em> operand.</param>
    /// <remarks>
    /// 🧩 This method is <c>virtual</c> and intended to be overridden by derived semantics as needed.<br/>
    /// The base implementation invokes templated method <see cref="EvaluateManagedNumericOp"/> then uses its result to
    /// create a <see cref="VBNumericTypedValue"/> of the <em>effective data type</em> associated with the <c>ResultSymbol</c> 
    /// of the <em>binary arithmetic operator expression</em>, then returns it in a <c>Success</c> result.
    /// </remarks>
    /// <returns>
    /// A <see cref="RuntimeSemanticsEvaluationResult"/> describing the evaluation result. 
    /// <list type="bullet">
    /// <item><strong>If successful</strong>, the operation <c>Result</c> is a <see cref="VBTypedValue"/> of the <em>effective data type</em> of the operation.</item>
    /// <item>Otherwise, the result contains a <see cref="VBRuntimeErrorInfo"/> describing a specific run-time error.</item>
    /// </list>
    /// </returns>
    protected virtual RuntimeSemanticsEvaluationResult EvaluateBinaryExpressionResult(
        VBNumericType effectiveType, 
        Symbol symbol, 
        VBNumericTypedValue lhs, VBNumericTypedValue rhs) 
        => RuntimeSemanticsEvaluationResult.Success(
            VBTypedValueFactory.CreateValue(effectiveType, symbol, 
                EvaluateManagedNumericOp(lhs.ManagedValue.InteropValue!.Value.Double, rhs.ManagedValue.InteropValue!.Value.Double)));

    /// <summary>
    /// Evaluates the <see cref="VBDateType"/> runtime semantics of a <em>binary arithmetic operator</em>.<br/>
    /// 👉 <see cref="VBDateValue"/> operands have been <em>let-coerced</em> to <see cref="VBDoubleValue"/> values for evaluation.
    /// </summary>
    /// <param name="effectiveType">The <see cref="VBDateType"/> determined <em>effective data type</em> of the <em>arithmetic operation</em>.</param>
    /// <param name="symbol">The <c>ResultSymbol</c> of the <em>binary arithmetic operator expression</em>.</param>
    /// <param name="lhs">The resolved <see cref="VBNumericTypedValue"/> of the left-hand side (LHS) <em>date binary expression</em> operand.</param>
    /// <param name="rhs">The resolved <see cref="VBNumericTypedValue"/> of the right-hand side (RHS) <em>date binary expression</em> operand.</param>
    /// <remarks>
    /// 🧩 This method is <c>virtual</c> and intended to be overridden by derived semantics as needed.<br/>
    /// The base implementation invokes templated method <see cref="EvaluateManagedNumericOp"/> then uses its result to
    /// create a <see cref="VBDateValue"/> associated with the <c>ResultSymbol</c> 
    /// of the <em>binary arithmetic operator expression</em>, then returns it in a <c>Success</c> result.
    /// </remarks>
    /// <returns>
    /// A <see cref="RuntimeSemanticsEvaluationResult"/> describing the evaluation result. 
    /// <list type="bullet">
    /// <item><strong>If successful</strong>, the operation <c>Result</c> is a <see cref="VBTypedValue"/> of the <em>effective data type</em> of the operation.</item>
    /// <item>Otherwise, the result contains a <see cref="VBRuntimeErrorInfo"/> describing a specific run-time error.</item>
    /// </list>
    /// </returns>
    protected virtual RuntimeSemanticsEvaluationResult EvaluateBinaryExpressionResult(
        VBDateType effectiveType, 
        Symbol symbol, 
        VBNumericTypedValue lhs, VBNumericTypedValue rhs) =>
        RuntimeSemanticsEvaluationResult.Success(
            VBTypedValueFactory.CreateValue(effectiveType, symbol, 
                EvaluateManagedNumericOp(lhs.ManagedValue.InteropValue!.Value.Double, rhs.ManagedValue.InteropValue!.Value.Double)));

    /// <summary>
    /// 💥 Creates and returns a new <see cref="RuntimeSemanticsEvaluationResult"/> with a <see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/> error.
    /// </summary>
    /// <param name="expression">The <em>binary arithmetic operator expression</em> whose <c>ResultSymbol</c> the error result will be attached to.</param>
    /// <param name="verbose">A detailed <c>Verbose</c> message about the error.</param>
    protected static RuntimeSemanticsEvaluationResult OnInvalidProcedureCallOrArgument(BoundExpression expression, string verbose)
        => RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.InvalidProcedureCallOrArgument, expression, verbose));
    /// <summary>
    /// 💥 Creates and returns a new <see cref="RuntimeSemanticsEvaluationResult"/> with a <see cref="VBRuntimeErrorId.DivisionByZero"/> error.
    /// </summary>
    /// <param name="expression">The <em>binary arithmetic operator expression</em> whose <c>ResultSymbol</c> the error result will be attached to.</param>
    /// <param name="verbose">A detailed <c>Verbose</c> message about the error.</param>
    protected static RuntimeSemanticsEvaluationResult OnDivisionByZero(BoundExpression expression, string verbose)
        => RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.DivisionByZero, expression, verbose));
    /// <summary>
    /// 💥 Creates and returns a new <see cref="RuntimeSemanticsEvaluationResult"/> with a <see cref="VBRuntimeErrorId.Overflow"/> error.
    /// </summary>
    /// <param name="expression">The <em>binary arithmetic operator expression</em> whose <c>ResultSymbol</c> the error result will be attached to.</param>
    /// <param name="verbose">A detailed <c>Verbose</c> message about the error.</param>
    protected static RuntimeSemanticsEvaluationResult OnOverflow(BoundExpression expression, string verbose)
        => RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.Overflow, expression, verbose));

    protected override ISemanticContextContributor<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> Analyze(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext coercionContext,
        ISemanticContextContributor<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> builder,
        VBOperatorExpression<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression,
        OperatorAnalysisContext<ArithmeticOperatorSemanticFlags> analysisContext,
        params VBTypedValue[] operands)
        => builder.AddFlags(analysisContext.EffectiveTypeResult.Result switch
        {
            VBNumericType => ArithmeticOperatorSemanticFlags.VBNumericEffectiveType,
            VBDateType => ArithmeticOperatorSemanticFlags.VBDateEffectiveType,
            VBStringType => ArithmeticOperatorSemanticFlags.VBStringEffectiveType,
            VBNullType => ArithmeticOperatorSemanticFlags.VBNullEffectiveType,
            _ => 0
        });
}
