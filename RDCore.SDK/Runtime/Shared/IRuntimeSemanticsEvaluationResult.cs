using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// The outcome of evaluating any runtime semantics, read without regard to how precisely the
/// evaluation's result type was stated.
/// </summary>
/// <remarks>
/// <see cref="RuntimeSemanticsEvaluationResult{TValue}"/> names the <see cref="VBTypedValue"/> a
/// <em>specific</em> evaluation produces, which is how a standard-library signature states its own
/// VBA return type; <see cref="RuntimeSemanticsEvaluationResult"/> names none. A caller that
/// dispatches over declarations it did not write - an invoker reaching a standard-library member
/// through reflection, say - cannot know which of the two it is holding, and needs the outcome
/// regardless: this is what both of them are.
/// </remarks>
public interface IRuntimeSemanticsEvaluationResult
{
    /// <summary>
    /// The result of the evaluation, if one was produced.
    /// </summary>
    VBTypedValue? Result { get; }

    /// <summary>
    /// The error metadata for the <em>run-time</em> error to be reported, if applicable.
    /// </summary>
    VBRuntimeErrorInfo? ErrorInfo { get; }

    /// <summary>
    /// <c>true</c> if the evaluation was successfully completed.
    /// </summary>
    bool IsSuccess { get; }

    /// <summary>
    /// <c>true</c> if the evaluation semantically yields a <em>runtime error</em>.
    /// </summary>
    bool IsError { get; }

    /// <summary>
    /// <c>true</c> if an evaluation strategy could not be determined.
    /// </summary>
    bool IsInternalError { get; }
}
