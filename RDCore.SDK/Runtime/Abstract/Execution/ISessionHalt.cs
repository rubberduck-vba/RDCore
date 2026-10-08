using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Why the program stopped running before it came to the end of its entry point.
/// </summary>
public enum RuntimeHaltKind
{
    /// <summary>
    /// An <c>End</c> statement. The program is over and nothing of it is left: the session returns to the state it was loaded in, without unwinding
    /// the call stack first (see <see cref="ISessionHalt"/>).
    /// </summary>
    End,

    /// <summary>
    /// A <c>Stop</c> statement, or a break asked for from outside - <c>Ctrl+Break</c> at a keyboard, a request cancelled by its client. The program stops
    /// where it is, and what it did to the session stays: the variables it assigned are there to be looked at, the module-level ones in the session and the locals
    /// in the activations that stay on the <see cref="ICallStack"/>, each with the place it stopped at (<see cref="ICallStackFrame.Pc"/>). MS-VBAL 5.4.2.11 has the
    /// program suspended, with "all variables maintain their state if execution resumes". Nothing resumes it yet: the next program that is started lets go of the stack.
    /// </summary>
    Break,
}

/// <summary>
/// Whether the program has been stopped, and why.
/// </summary>
/// <remarks>
/// <c>End</c> and <c>Stop</c> are not values, and not errors: they come out of the middle of an expression - a function called from the right-hand side of an assignment
/// can <c>End</c> the program - through code whose only ways to answer are a value and an error. So the statement says so here, and fails the way a call that
/// has no answer fails; whatever is waiting for the answer sees that it has none, and the one that started the program asks this why.
/// <para>
/// A break asked for from outside takes the same road. It is what <c>Stop</c> is when nobody wrote it: the cancellation that reaches the interpreter is a
/// request to <see cref="Request"/> a <see cref="RuntimeHaltKind.Break"/>, so that a program stopped at the keyboard and a program that stopped itself are told
/// apart by nothing but the place they stopped at.
/// </para>
/// <para>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </para>
/// </remarks>
public interface ISessionHalt
{
    /// <summary>
    /// Why the program has been stopped, or <see langword="null"/> when it has not.
    /// </summary>
    RuntimeHaltKind? Pending { get; }

    /// <summary>
    /// Where the program was stopped, when the statement that stopped it is known.
    /// </summary>
    SourceLocation? Location { get; }

    /// <summary>
    /// Stops the program.
    /// </summary>
    /// <param name="kind">Why.</param>
    /// <param name="location">The statement that stopped it, or <see langword="null"/> when it was not one.</param>
    /// <remarks>
    /// The first request stands: a program that is already stopping is not stopped again by what unwinds it.
    /// </remarks>
    void Request(RuntimeHaltKind kind, SourceLocation? location = null);

    /// <summary>
    /// Forgets the request, so that the next program starts as one that was never stopped.
    /// </summary>
    void Clear();
}
