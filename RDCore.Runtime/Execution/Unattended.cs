using RDCore.SDK;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.Execution;

/// <summary>
/// What a dialog of a program run unattended is (<see cref="IRuntimeSession.RunsUnattended"/>): shown nowhere, and the end of the run; and what a form it
/// leaves loaded is, which nobody unloads.
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

    /// <summary>
    /// The error a program run unattended fails the run with when it returns from its entry point with a form of Visual Basic 6.0's form model still
    /// loaded: nobody is there to unload it, and the run would last for it. The same error as a dialog's (<see cref="NobodyToAnswer"/>), recorded as one no
    /// handler of the program catches (<see cref="ISessionErrorState.RaiseUntrappable"/>).
    /// </summary>
    /// <param name="session">The session the program runs in.</param>
    /// <param name="form">The name of the form left loaded.</param>
    public static VBRuntimeErrorInfo NobodyToUnload(IRuntimeSession session, string form)
    {
        var error = VBRuntimeErrorInfo.For(VBRuntimeErrorId.NobodyToAnswer, default, string.Format(Exceptions.VBRuntimeError_NobodyToUnload_Verbose, form));
        session.Errors.RaiseUntrappable(error);
        return error;
    }
}
