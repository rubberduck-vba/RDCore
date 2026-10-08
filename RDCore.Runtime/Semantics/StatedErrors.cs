using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;

namespace RDCore.Runtime.Semantics;

/// <summary>
/// Decides which of the errors an evaluation raised are errors <em>of the code</em>, and so facts about it.
/// </summary>
internal static class StatedErrors
{
    /// <summary>
    /// The error an evaluation raised, as a fact states it.
    /// </summary>
    /// <param name="error">The error the evaluation raised, if it did.</param>
    /// <returns>
    /// <see langword="null"/> when nothing was raised, and also for an internal error: that is a defect of the semantics, not
    /// something the code does, and a fact that said the code raises error 51 would be a lie.
    /// </returns>
    public static VBErrorInfo? Of(IVBRaisableError? error)
        => error is null || error.ErrorId == (int)VBRuntimeErrorId.InternalError ? null : error.AsErrorInfo;
}
