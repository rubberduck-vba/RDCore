namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The lines of the source a program that is run under a debugger waits at before it runs them.
/// </summary>
/// <remarks>
/// A breakpoint is a place in the source, not in the code: the code of a module is loaded again whenever the document is edited, and a breakpoint stays on its line.
/// The place is a line of a module, and the instructions it is are found in the code that runs. On a line a statement begins on, it is the first instruction of a
/// procedure's body that begins there. On a line that only ends a block (an <c>End If</c>, an <c>End Select</c>, an <c>End With</c>, a <c>Next</c> or a
/// <c>Loop</c>), it is each instruction shown there, so that the program waits there from whichever branch it took.
/// Any other line is not a place a program waits at, and is not looked for after: the client that sets breakpoints says which lines they are on, and is told
/// which of them are places a program waits at.
/// <para>
/// The program waits <em>before</em> the instruction, as it does at a step, and is resumed from the same instruction without waiting there again.
/// </para>
/// <para>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </para>
/// </remarks>
public interface IBreakpointTable
{
    /// <summary>
    /// Whether any line has a breakpoint, which is all that the interpreter looks at between two instructions when none has.
    /// </summary>
    bool HasAny { get; }

    /// <summary>
    /// Replaces the breakpoints of a module.
    /// </summary>
    /// <param name="module">The address of the module, as its symbol has it.</param>
    /// <param name="breakpoints">The breakpoints, one per line. None removes the module's breakpoints.</param>
    void Set(string module, IReadOnlyCollection<RuntimeBreakpoint> breakpoints);

    /// <summary>
    /// Removes every breakpoint.
    /// </summary>
    void Clear();
}
