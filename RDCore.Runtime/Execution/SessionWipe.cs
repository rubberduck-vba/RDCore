using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// What an <c>End</c> statement does to a session: the program is over, and nothing of it is left.
/// </summary>
/// <remarks>
/// <c>End</c> is not <c>Exit Sub</c> repeated for every activation on the stack. It resets the session to the state it was loaded in without unwinding
/// anything first: no <c>Terminate</c> runs for any object, no <c>Class_Terminate</c> that would find the program half torn down, and nothing is let go of
/// in the order the program would have let go of it. Doing it as an unwinding would be the wrong model, and a dangerous one - a <c>Terminate</c> is code,
/// and code runs against the session as it is, which is the session the program was in the middle of.
/// <para>
/// So it is a wipe, and it is atomic in the one sense that counts: the variables, the objects and the files go together, and the declarations and the code stay.
/// A session that <c>End</c>ed is ready to run the program again from the start.
/// </para>
/// <para>
/// ℹ️ Not the same as breaking. A <c>Stop</c>, or a <c>Ctrl+Break</c>, stops the program where it is and leaves the session as the program made it.
/// </para>
/// </remarks>
public static class SessionWipe
{
    /// <summary>
    /// Wipes the session of everything a program that has ended made of it.
    /// </summary>
    /// <param name="session">The session the program ran in.</param>
    public static void End(IRuntimeSession session)
    {
        // the activations of a program that was stopped are popped as the stop unwinds; whatever is left is not the program's any more.
        while (session.CallStack.TryPop(out _))
        {
        }

        _ = session.Files.CloseAll();
        session.Errors.Clear();
        session.Objects.Clear();
        session.Symbols.ResetStorage();
    }
}
