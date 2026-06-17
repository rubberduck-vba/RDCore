using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// Represents the result of the evaluation of any runtime semantics.
/// </summary>
/// <param name="Result">The result of the evaluation, if one was produced.</param>
/// <param name="ErrorInfo">The error metadata for the <em>run-time</em> error to be reported, if applicable.</param>
public readonly record struct RuntimeSemanticsEvaluationResult(
    VBTypedValue? Result, 
    VBRuntimeErrorInfo? ErrorInfo) 
{
    /// <summary>
    /// <c>true</c> if the evaluation was successfully completed.
    /// </summary>
    /// <remarks>
    /// ✅ This value represents a normally completed, successful evaluation.
    /// </remarks>
    public bool IsSuccess => Result is not null && ErrorInfo is null;
    /// <summary>
    /// <c>true</c> if the evaluation semantically yields a <em>runtime error</em>.
    /// </summary>
    /// <remarks>
    /// 👉 This value represents a <strong>specified, consistent state</strong> where program execution resumes in a controlled error state.
    /// </remarks>
    public bool IsError => Result is not null && ErrorInfo is not null;
    /// <summary>
    /// <c>true</c> if an evaluation strategy could not be determined.
    /// </summary>
    /// <remarks>
    /// ⚠️ This value represents an <strong>unspecified, inconsistent</strong> internal state that is ultimately surfaced as an <see cref="VBRuntimeErrorId.InternalError"/> run-time error.
    /// </remarks>
    public bool IsInternalError => Result is null && ErrorInfo is null;

    /// <summary>
    /// Creates a new (successful) <see cref="RuntimeSemanticsEvaluationResult"/> with the specified evaluation result.
    /// </summary>
    /// <param name="result">The successfully evaluated <em>runtime semantics evaluation</em> result.</param>
    /// <remarks>✅ Use this method <em>only</em> to signal a <strong>successully</strong> evaluated expression result.</remarks>
    public static RuntimeSemanticsEvaluationResult Success(VBTypedValue result) => new(result, null);
    /// <summary>
    /// Creates a new (failed) <see cref="RuntimeSemanticsEvaluationResult"/> with the specified <see cref="VBRuntimeErrorInfo"/> error information metadata.
    /// </summary>
    /// <param name="error">The runtime error metadata describing the evaluation failure.</param>
    /// <remarks>❌ Use this method <em>only</em> to signal a <strong>failed</strong> expression evaluation. A result value may still have been assigned.</remarks>
    public static RuntimeSemanticsEvaluationResult Error(VBRuntimeErrorInfo error, VBTypedValue? result = null) => new(result, error);

    /// <summary>
    /// Creates a new <c>RuntimeSemanticsEvalutationResult</c> without a result, and without any <c>VBRuntimeErrorInfo</c> error metadata.
    /// </summary>
    /// <remarks>
    /// 💥 This results signals a <c>InternalError</c> run-time error to the evaluation pipeline.
    /// Use it <em>only</em> as a fallback result, when no result can be evaluated for a given <em>effective type</em>.</remarks>
    public static RuntimeSemanticsEvaluationResult InternalError() => new(null, null);
}
