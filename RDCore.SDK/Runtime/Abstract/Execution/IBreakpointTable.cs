using RDCore.SDK.Semantics.Instructions;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The lines of the source a program that is run under a debugger waits at before it runs them.
/// </summary>
/// <remarks>
/// A breakpoint is a place in the source, not in the code: the code of a module is loaded again whenever the document is edited, and a breakpoint stays on its line.
/// The place is a line of a module, and the instruction it is is found in the code that runs - the first instruction of a procedure's body that begins on the line.
/// A line with no statement on it is not a place a program waits at, and is not looked for after: the client that sets breakpoints says which lines they are on, and
/// is told which of them are statements (<see cref="Verify"/>).
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
    /// <param name="lines">The zero-based lines of the source. None removes the module's breakpoints.</param>
    void Set(string module, IReadOnlyCollection<int> lines);

    /// <summary>
    /// Removes every breakpoint.
    /// </summary>
    void Clear();

    /// <summary>
    /// Whether the instruction at <paramref name="offset"/> of <paramref name="body"/> is where a breakpoint is.
    /// </summary>
    /// <param name="module">The address of the module whose procedure the body is of.</param>
    /// <param name="body">The body.</param>
    /// <param name="offset">The offset of the instruction about to run.</param>
    bool IsAt(string module, InstructionList body, int offset);

    /// <summary>
    /// Whether a line is one a program can wait at: a statement of one of the bodies begins on it.
    /// </summary>
    /// <param name="bodies">The bodies of the module's procedures.</param>
    /// <param name="line">A zero-based line of the source.</param>
    static bool Verify(IEnumerable<InstructionList> bodies, int line)
        => bodies.Any(body => body.Items.Any(instruction => instruction.Node is { } node && node.SourceLocation.Range.Start.Line == line));
}
