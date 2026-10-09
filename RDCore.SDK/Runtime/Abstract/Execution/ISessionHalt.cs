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
    /// program suspended, with "all variables maintain their state if execution resumes". A program that runs under an <see cref="IExecutionGate"/> is not unwound by a
    /// <c>Stop</c> at all: it waits where it is, and can be resumed. One that does not is unwound, and the next program that is started lets go of the stack.
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

    /// <summary>
    /// The gate a program that can be resumed runs under, or <see langword="null"/> for one that cannot, which a <c>Stop</c> unwinds.
    /// </summary>
    IExecutionGate? Gate { get; set; }

    /// <summary>
    /// Offers the program to the <see cref="Gate"/> to wait at the place it is stopped at.
    /// </summary>
    /// <param name="kind">Why the program is stopped.</param>
    /// <param name="location">The statement that stopped it, or <see langword="null"/> when it was not one.</param>
    /// <returns>
    /// <see langword="true"/> when the program waited and was resumed: it goes on from the statement after the one that stopped it, as if that had completed.
    /// <see langword="false"/> when it did not wait, because there is no gate, or because it was abandoned: the program is stopped as it is when nothing can resume it.
    /// </returns>
    bool TrySuspend(RuntimeHaltKind kind, SourceLocation? location);

    /// <summary>
    /// Asks the program to stop again at the first instruction boundary that <paramref name="kind"/> says, once it is resumed.
    /// </summary>
    /// <param name="kind">How far the program goes before it stops.</param>
    /// <param name="callDepth">The depth of the call stack the program is stepping from.</param>
    void RequestStep(StepKind kind, int callDepth);

    /// <summary>
    /// Whether a step that was asked for is due at the instruction about to run, which forgets it: it is one step.
    /// </summary>
    /// <param name="callDepth">The depth of the call stack at that instruction.</param>
    /// <remarks>
    /// Asked between every two instructions, and so nothing but a field is looked at when no step was asked for.
    /// </remarks>
    bool TakeStep(int callDepth);

    /// <summary>
    /// The lines a program that runs under a <see cref="Gate"/> waits at before it runs them. They are the session's, and outlive the program: they are still
    /// there when the program is started again.
    /// </summary>
    IBreakpointTable Breakpoints { get; }
}

/// <summary>
/// How far a program that is resumed goes before it stops again. The program only ever stops between two instructions, never inside one: a statement is not
/// half-done at a step, and a call that is stepped over is one that is made.
/// </summary>
public enum StepKind
{
    /// <summary>At the next instruction, in whichever procedure it is: a call is stepped into.</summary>
    Into,

    /// <summary>At the next instruction of the procedure the program is in, or of the one that called it if this one is over: a call is made, and stepped over.</summary>
    Over,

    /// <summary>At the next instruction of the procedure that called the one the program is in.</summary>
    Out,
}
