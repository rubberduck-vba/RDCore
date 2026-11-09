namespace RDCore.Runtime.LanguageCore.Flow;

/// <summary>
/// Why control can pass from one instruction to another.
/// </summary>
public enum ControlFlowEdgeKind
{
    /// <summary>
    /// Control falls through to the next instruction.
    /// </summary>
    Fallthrough,

    /// <summary>
    /// Control jumps: a <c>GoTo</c>, the branch of a condition that is not taken in sequence, the back-edge of a loop, an <c>Exit</c> statement.
    /// </summary>
    Branch,

    /// <summary>
    /// An error raised by the instruction is handled by an <c>On Error GoTo</c> handler: control leaves the instruction <em>before</em> it completes.
    /// </summary>
    Error,
}

/// <summary>
/// One way control can pass from an instruction to another, in a <see cref="ControlFlowGraph"/>.
/// </summary>
/// <param name="From">The offset of the instruction control leaves.</param>
/// <param name="To">The offset of the instruction control arrives at, or <see cref="ControlFlowGraph.Exit"/> when the activation ends.</param>
/// <param name="Kind">Why control can pass.</param>
public readonly record struct ControlFlowEdge(int From, int To, ControlFlowEdgeKind Kind);
