using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// What becomes of a program that is waiting at a <c>Stop</c>.
/// </summary>
public enum SuspensionDecision
{
    /// <summary>The program goes on from the statement after the one it stopped at.</summary>
    Resume,

    /// <summary>The program is over: it is stopped as one is that nothing can resume, and unwound.</summary>
    Abandon,
}

/// <summary>
/// Where a program waits when it is stopped, if it is to be resumed.
/// </summary>
/// <remarks>
/// A program cannot be resumed from what it leaves behind when it is unwound. A <c>Stop</c> in a function that is called in the middle of an expression has half
/// of the statement that called it evaluated, and the interpreter has nothing that holds half a statement: the state of the program is its call stack and the program
/// counter of each activation, and the rest is on the stack of whoever is evaluating. So a program that is to be resumed does not leave: it waits where it is, and the
/// thread it runs on is held by <see cref="Suspend"/> until the answer comes.
/// <para>
/// Whoever runs the program on a thread of its own installs a gate in <see cref="ISessionHalt.Gate"/>; nothing else needs to know that there is one. A program
/// that is run without one is stopped by a <c>Stop</c> the way it always was.
/// </para>
/// <para>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </para>
/// </remarks>
public interface IExecutionGate
{
    /// <summary>
    /// Holds the calling thread, which is the program's, until it is told what to do.
    /// </summary>
    /// <param name="kind">Why the program is stopped.</param>
    /// <param name="location">The statement that stopped it, or <see langword="null"/> when it was not one.</param>
    /// <returns>What to do with the program.</returns>
    SuspensionDecision Suspend(RuntimeHaltKind kind, SourceLocation? location);
}
