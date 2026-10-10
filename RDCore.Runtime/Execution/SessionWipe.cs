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
        Abandon(session);

        _ = session.Files.CloseAll();
        session.Errors.Clear();
        session.Objects.Clear();
        session.ExternalObjects.ReleaseAll();
        session.Symbols.ResetStorage();
    }

    /// <summary>
    /// Lets go of the activations of a program that was stopped where it is, so that the next program starts on an empty stack.
    /// </summary>
    /// <param name="session">The session the program ran in.</param>
    /// <remarks>
    /// A <c>Stop</c> or a break that unwinds the program leaves the call stack as it was, so that the locals of every activation can be looked at (MS-VBAL 5.4.2.11). That
    /// is all it is for: a program is resumed from where it waits (<see cref="SuspendableExecution"/>), not from a stack that is left behind, so a program that is started,
    /// or a module that is discarded, abandons it. Only the stack is let go of: the module-level
    /// variables are the session's and stay as the program made them, and the objects the locals held are the session's too, until it is wiped by an <c>End</c>.
    /// </remarks>
    // TODO: the objects only a local of an abandoned activation held keep their reference until the session is wiped; releasing them runs their Terminate, which a program
    // that is merely abandoned has not asked for - a reference release that does not is the missing half of this.
    public static void Abandon(IRuntimeSession session)
    {
        while (session.CallStack.TryPop(out _))
        {
        }
    }
}
