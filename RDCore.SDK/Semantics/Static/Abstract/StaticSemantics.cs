using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Semantics.Static.Abstract;

/// <summary>
/// Represents the result of the evaluation of any static semantics.
/// </summary>
/// <param name="Result">The result of the static evaluation, if one was produced.</param>
/// <param name="ErrorInfo">The error metadata for the <em>compile-time</em> error to be reported, if applicable.</param>
public readonly record struct StaticSemanticsEvaluationResult(
    VBType? Result, 
    VBCompileErrorInfo? ErrorInfo)
{
    /// <summary>
    /// <c>true</c> if the evaluation was successfully completed.
    /// </summary>
    /// <remarks>
    /// ✅ This value represents a normally completed, successful evaluation.
    /// </remarks>
    public bool IsSuccess => /*IsApplicable &&*/ ErrorInfo is null;
    /// <summary>
    /// <c>true</c> if the evaluation semantically yields a <em>runtime error</em>.
    /// </summary>
    /// <remarks>
    /// 👉 This value represents a <strong>specified, consistent</strong> state where the <strong>program module is invalid</strong> due to a specific <em>compilation error</em>.
    /// </remarks>
    public bool IsError => Result is not null && ErrorInfo is not null;

    /// <summary>
    /// Creates a new (successful) <see cref="StaticSemanticsEvaluationResult"/> with the specified evaluation result.
    /// </summary>
    /// <param name="result">The successfully evaluated <em>static semantics evaluation</em> result.</param>
    /// <remarks>✅ Use this method <em>only</em> to signal a <strong>successully</strong> evaluated expression result.</remarks>
    public static StaticSemanticsEvaluationResult Success(VBType result) => new(result, null);
    /// <summary>
    /// Creates a new (failed) <see cref="StaticSemanticsEvaluationResult"/> with the specified <see cref="VBCompileErrorInfo"/> error information metadata.
    /// </summary>
    /// <param name="error">The compile-time error metadata describing the evaluation failure.</param>
    /// <remarks>❌ Use this method <em>only</em> to signal a <strong>failed</strong> expression evaluation.</remarks>
    public static StaticSemanticsEvaluationResult Error(VBCompileErrorInfo error) => new(null, error);
}


/// <summary>
/// Represents any static semantics rules.
/// </summary>
public interface IStaticSemantics
{
    /// <summary>
    /// Determines a static <c>VBType</c> from specified operands.
    /// </summary>
    /// <param name="resolver">The static context containing the available static memory space.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="operandDeclaredTypes">The declared type of each operand involved in the evaluation.</param>
    /// <returns>
    /// A <see cref="StaticSemanticsEvaluationResult"/> encapsulating the resulting <see cref="VBType"/> if successful, or <see cref="VBCompileErrorInfo"/> error metadata otherwise.
    /// </returns>
    StaticSemanticsEvaluationResult DetermineDeclaredType(ISymbolResolver resolver, BoundExpression expression, params VBType[] operandDeclaredTypes);
}

/// <summary>
/// The class at the base of the static semantics type hierarchy that implements all the static semantic rules defined in MS-VBAL.
/// </summary>
public abstract record class StaticSemantics() : IStaticSemantics
{
    /// <summary>
    /// Determines a static <c>VBType</c> from specified operands.
    /// </summary>
    /// <param name="resolver">The static context containing the available static memory space.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="operandDeclaredTypes">The declared type of each operand involved in the evaluation.</param>
    /// <returns>
    /// A <see cref="StaticSemanticsEvaluationResult"/> encapsulating the resulting <see cref="VBType"/> if successful, or <see cref="VBCompileErrorInfo"/> error metadata otherwise.
    /// </returns>
    public abstract StaticSemanticsEvaluationResult DetermineDeclaredType(ISymbolResolver resolver, BoundExpression expression, params VBType[] operandDeclaredTypes);

    /// <summary>
    /// Gets the error metadata for a <em>type mismatch</em> compile-time error.
    /// </summary>
    /// <param name="expression">The <em>statically invalid</em> expression.</param>
    /// <param name="operandDeclaredTypes">The <em>declared types</em> of the inputs of the expression.</param>
    protected static VBCompileErrorInfo GetStaticTypeMismatchErrorInfo(BoundExpression expression, VBType[] operandDeclaredTypes)
        => VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location, 
            Exceptions.VBCompileError_TypeMismatch_Verbose.Replace("{$INPUTS}", string.Join(", ", operandDeclaredTypes.Select(type => type.Name))));

    /// <summary>
    /// Gets the error metadata for a <em>type mismatch</em> compile-time error.
    /// </summary>
    /// <param name="expression">The <em>statically invalid</em> expression.</param>
    /// <param name="operandDeclaredTypes">The <em>declared types</em> of the inputs of the expression.</param>
    /// <remarks>
    /// 👉 A different <c>verbose</c> message differenciates this error from a <em>static type mismatch</em> error; 
    /// they are the same compile-time error, but with distincly different causes that a verbose message should explain.
    /// </remarks>
    protected static VBCompileErrorInfo GetStaticCoercionTypeMismatchErrorInfo(BoundExpression expression, VBType[] operandDeclaredTypes)
        => VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location, 
            Exceptions.VBCompileError_LetCoercionTypeMismatch_Verbose);
}
