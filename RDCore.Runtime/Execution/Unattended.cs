using RDCore.SDK;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// What a dialog of a program run unattended is (<see cref="IRuntimeSession.RunsUnattended"/>): shown nowhere, and the end of the run.
/// </summary>
internal static class Unattended
{
    /// <summary>
    /// The error a dialog of a program run unattended fails the run with: recorded as one no handler of the program catches
    /// (<see cref="ISessionErrorState.RaiseUntrappable"/>), and given to the call that opened the dialog, which ends the run with it.
    /// </summary>
    /// <param name="session">The session the program runs in.</param>
    /// <param name="dialog">The dialog, as the program opened it: <c>MsgBox</c>, <c>InputBox</c>, a form's <c>Show</c>.</param>
    /// <param name="location">Where the program opened it, when it is known.</param>
    public static VBRuntimeErrorInfo NobodyToAnswer(IRuntimeSession session, string dialog, SourceLocation location = default)
    {
        var error = VBRuntimeErrorInfo.For(VBRuntimeErrorId.NobodyToAnswer, location, string.Format(Exceptions.VBRuntimeError_NobodyToAnswer_Verbose, dialog));
        session.Errors.RaiseUntrappable(error);
        return error;
    }
}
