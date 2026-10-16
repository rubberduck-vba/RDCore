using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Plans;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Services.VerboseMessages;
using System.Numerics;

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
    /// <param name="lhs">The managed value of the left-hand side (LHS) numeric binary expression operand, in the operation's effective type.</param>
    /// <param name="rhs">The managed value of the right-hand side (RHS) numeric binary expression operand, in the operation's effective type.</param>
    /// <typeparam name="T">The CLR representation of the operation's <em>effective numeric type</em>.</typeparam>
    /// <remarks>
    /// 👉 This method is templated by the <see cref="EvaluateBinaryExpressionResult(VBNumericType, VBNumericTypedValue, VBNumericTypedValue, ExpressionNode)"/> dispatcher,
    /// which computes the result in the effective type's own representation (a <c>checked</c> context surfaces integral overflow as <see cref="VBRuntimeErrorId.Overflow"/>).
    /// </remarks>
    protected abstract T EvaluateManagedNumericOp<T>(T lhs, T rhs) where T : INumber<T>;

    /// <summary>
    /// Computes the arithmetic result in the effective type's own CLR representation, dispatching
    /// <see cref="EvaluateManagedNumericOp{T}(T, T)"/> on the effective numeric type. Overflow and
    /// division-by-zero raised by the managed operation are surfaced as run-time errors.
    /// </summary>
    /// <remarks>
    /// 👉 Both operands have already been let-coerced to <paramref name="effectiveType"/> by the evaluation pipeline, so the casts are safe.
    /// An operation of a Variant operand that overflows an integral effective type is evaluated again in a wider type (<see cref="VariantArithmeticOverflow"/>).
    /// </remarks>
    protected RuntimeSemanticsEvaluationResult EvaluateManagedArithmetic(
        OperatorEvaluationFrame frame,
        VBNumericType effectiveType,
        VBNumericTypedValue lhs, VBNumericTypedValue rhs,
        ExpressionNode expression)
    {
        try
        {
            VBTypedValue result = effectiveType switch
            {
                VBByteType => new VBByteValue(EvaluateManagedNumericOp(((VBByteValue)lhs).Value, ((VBByteValue)rhs).Value)),
                VBIntegerType => new VBIntegerValue(EvaluateManagedNumericOp(((VBIntegerValue)lhs).Value, ((VBIntegerValue)rhs).Value)),
                VBLongType => new VBLongValue(EvaluateManagedNumericOp(((VBLongValue)lhs).Value, ((VBLongValue)rhs).Value)),
                VBLongLongType => new VBLongLongValue(EvaluateManagedNumericOp(((VBLongLongValue)lhs).Value, ((VBLongLongValue)rhs).Value)),
                VBSingleType => new VBSingleValue(EvaluateManagedNumericOp(((VBSingleValue)lhs).Value, ((VBSingleValue)rhs).Value)),
                VBDoubleType => new VBDoubleValue(EvaluateManagedNumericOp(((VBDoubleValue)lhs).Value, ((VBDoubleValue)rhs).Value)),
                VBCurrencyType => new VBCurrencyValue(EvaluateManagedNumericOp(((VBCurrencyValue)lhs).Value.Value, ((VBCurrencyValue)rhs).Value.Value)),
                VBDecimalType => new VBDecimalValue(EvaluateManagedNumericOp(((VBDecimalValue)lhs).Value, ((VBDecimalValue)rhs).Value)),
                _ => throw new NotSupportedException($"Effective type '{effectiveType.Name}' is not a supported arithmetic numeric type."),
            };
            return RuntimeSemanticsEvaluationResult.Success(result);
        }
        catch (OverflowException) when (VariantArithmeticOverflow.Widens(frame, effectiveType)
            && VariantArithmeticOverflow.Widened(EvaluateManagedNumericOp(lhs.AsDouble, rhs.AsDouble)) is { } widened)
        {
            return RuntimeSemanticsEvaluationResult.Success(widened);
        }
        catch (OverflowException)
        {
            return OnOverflow(expression, Exceptions.VBRuntimeError_ArithmeticOverflow);
        }
        catch (DivideByZeroException)
        {
            return OnDivisionByZero(expression, Exceptions.VBDivisionOp_DivisionByZero);
        }
    }

    protected sealed override OperatorAnalysisContext<ArithmeticOperatorSemanticFlags> CreateAnalysisContext(
        SyntaxNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        ArithmeticOperatorSemanticFlags semanticFlags)
        => new(node.Identity, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected sealed override OperatorFact? CreateFact(
        string token, ExpressionNode expression, VBType? effectiveType, ArithmeticOperatorSemanticFlags flags,
        StringComparisonRules comparison, bool isValueKnown, VBErrorInfo? error)
        => new ArithmeticOperatorFact(expression.Identity, expression.Location, token, effectiveType, isValueKnown, error, flags);

    /// <summary>
    /// The operator: one of the <c>Tokens</c> arithmetic operators, which is what its <see cref="OperationPlan"/> is made for.
    /// </summary>
    protected abstract string Operator { get; }

    /// <remarks>
    /// The effective type is the operation's plan (<see cref="OperationPlanner"/>), made once for each pair of value types and kept: the table of the specification
    /// is the planner's, and is not consulted again for every evaluation.
    /// </remarks>
    protected sealed override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver,
        BinaryArithmeticOperatorSemanticContext context, 
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
        => EffectiveTypeOf(OperationPlanner.ForValues(Operator, frame[InputIndex.BinaryLeftOperand].TypeInfo, frame[InputIndex.BinaryRightOperand].TypeInfo), expression);

    // MS-VBAL 5.6.9.3: "If the value type of any operand is an array, UDT or Error, runtime error 13 (Type mismatch) is raised."
    protected override LetCoercionResult ValidateOperand(ISymbolResolver resolver, ExpressionNode expression, OperatorEvaluationFrame frame, InputIndex index)
        => frame[index] is VBErrorValue
            ? LetCoercionResult.Error(OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression,
                Exceptions.VBRuntimeTypeMismatch_OperationEffectiveType_Verbose.Replace("{$OPERANDS}", string.Join(", ", frame.Operands.Select(operand => operand.TypeInfo.Name)))))
            : base.ValidateOperand(resolver, expression, frame, index);

    /// <summary>
    /// Evaluates the <see cref="VBNumericType"/> runtime semantics of a <em>binary arithmetic operator</em> 
    /// </summary>
    /// <param name="frame">The frame of the operation: what it says of the operands as they were given decides what an overflow does.</param>
    /// <param name="effectiveType">The determined <em>effective data type</em> of the <em>arithmetic operation</em>.</param>
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
        OperatorEvaluationFrame frame,
        VBNumericType effectiveType,
        VBNumericTypedValue lhs, VBNumericTypedValue rhs,
        ExpressionNode expression)
        => EvaluateManagedArithmetic(frame, effectiveType, lhs, rhs, expression);


    /// <summary>
    /// Evaluates the <see cref="VBDateType"/> runtime semantics of a <em>binary arithmetic operator</em>.<br/>
    /// 👉 <see cref="VBDateValue"/> operands have been <em>let-coerced</em> to <see cref="VBDoubleValue"/> values for evaluation.
    /// </summary>
    /// <param name="effectiveType">The <see cref="VBDateType"/> determined <em>effective data type</em> of the <em>arithmetic operation</em>.</param>
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
        VBNumericTypedValue lhs, VBNumericTypedValue rhs,
        ExpressionNode expression) =>
        RuntimeSemanticsEvaluationResult.Success(
            new VBDateValue(
                EvaluateManagedNumericOp(((VBDoubleValue)lhs).Value, ((VBDoubleValue)rhs).Value)));

    /// <summary>
    /// 💥 Creates and returns a new <see cref="RuntimeSemanticsEvaluationResult"/> with a <see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/> error.
    /// </summary>
    /// <param name="expression">The <em>binary arithmetic operator expression</em> whose <c>ResultSymbol</c> the error result will be attached to.</param>
    /// <param name="verbose">A detailed <c>Verbose</c> message about the error.</param>
    protected static RuntimeSemanticsEvaluationResult OnInvalidProcedureCallOrArgument(ExpressionNode expression, string verbose)
        => RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.InvalidProcedureCallOrArgument, expression, verbose));
    /// <summary>
    /// 💥 Creates and returns a new <see cref="RuntimeSemanticsEvaluationResult"/> with a <see cref="VBRuntimeErrorId.DivisionByZero"/> error.
    /// </summary>
    /// <param name="expression">The <em>binary arithmetic operator expression</em> whose <c>ResultSymbol</c> the error result will be attached to.</param>
    /// <param name="verbose">A detailed <c>Verbose</c> message about the error.</param>
    protected static RuntimeSemanticsEvaluationResult OnDivisionByZero(ExpressionNode expression, string verbose)
        => RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.DivisionByZero, expression, verbose));
    /// <summary>
    /// 💥 Creates and returns a new <see cref="RuntimeSemanticsEvaluationResult"/> with a <see cref="VBRuntimeErrorId.Overflow"/> error.
    /// </summary>
    /// <param name="expression">The <em>binary arithmetic operator expression</em> whose <c>ResultSymbol</c> the error result will be attached to.</param>
    /// <param name="verbose">A detailed <c>Verbose</c> message about the error.</param>
    protected static RuntimeSemanticsEvaluationResult OnOverflow(ExpressionNode expression, string verbose)
        => RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.Overflow, expression, verbose));

    protected override ISemanticContextContributor<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> Analyze(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext coercionContext,
        ISemanticContextContributor<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> builder,
        ExpressionNode expression,
        OperatorAnalysisContext<ArithmeticOperatorSemanticFlags> analysisContext,
        params VBTypedValue[] operands)
    {
        // an operand let-coerced from a fractional to a whole-number type is rounded, and the operation runs on the rounded value.
        if (coercionContext.Flags.HasFlag(ConversionSemanticFlags.BankersRounding))
        {
            builder.AddFlags(ArithmeticOperatorSemanticFlags.BankersRounding);
        }

        return builder.AddFlags(analysisContext.EffectiveTypeResult.Result switch
        {
            VBNumericType => ArithmeticOperatorSemanticFlags.VBNumericEffectiveType,
            VBDateType => ArithmeticOperatorSemanticFlags.VBDateEffectiveType,
            VBStringType => ArithmeticOperatorSemanticFlags.VBStringEffectiveType,
            VBNullType => ArithmeticOperatorSemanticFlags.VBNullEffectiveType,
            VBErrorType => ArithmeticOperatorSemanticFlags.VBErrorEffectiveType,
            _ => 0
        });
    }
}
