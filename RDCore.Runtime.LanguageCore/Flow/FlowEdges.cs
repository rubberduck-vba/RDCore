using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Flow;

/// <summary>
/// The edges a data-flow analysis walks: the <see cref="ControlFlowGraph"/>'s normal edges, where a <c>Resume</c> can continue, and where an error goes, as the
/// <see cref="ErrorHandlingStates"/> say.
/// </summary>
/// <remarks>
/// <para>
/// An analysis that misses a path states facts that are false on it, so every way an error leaves an instruction is an edge, of kind
/// <see cref="ControlFlowEdgeKind.Error"/>, along which the instruction has not completed:
/// </para>
/// <list type="bullet">
/// <item><description>to the handler, when one takes it;</description></item>
/// <item><description>to where <c>On Error Resume Next</c> goes on, under it: the next instruction, or past <c>End Select</c> (<see cref="ControlFlowGraph.NextAfterAnError"/>);</description></item>
/// <item><description>to <see cref="ControlFlowGraph.Exit"/>, when it is raised to the caller (under no policy, or while a handler runs), where what outlives the activation
/// is what the caller may read.</description></item>
/// </list>
/// <para>
/// A <c>Resume</c> is given an edge to every instruction that can raise into a handler, and a <c>Resume Next</c> to every one after one: which is the fault statement
/// is known only at run time.
/// </para>
/// </remarks>
public sealed class FlowEdges
{
    private readonly ImmutableArray<ControlFlowEdge>[] _successors;
    private readonly ImmutableArray<ControlFlowEdge>[] _predecessors;

    private FlowEdges(ImmutableArray<ControlFlowEdge>[] successors)
    {
        _successors = successors;
        var predecessors = Enumerable.Range(0, successors.Length).Select(_ => new List<ControlFlowEdge>()).ToArray();
        foreach (var edge in successors.SelectMany(edges => edges))
        {
            predecessors[edge.To].Add(edge);
        }

        _predecessors = [.. predecessors.Select(edges => edges.ToImmutableArray())];
    }

    /// <summary>
    /// The number of nodes: every instruction, and the exit.
    /// </summary>
    public int Count => _successors.Length;

    /// <summary>
    /// The edges that leave a node: where control goes after it, normally or by an error.
    /// </summary>
    /// <param name="node">The offset of an instruction, or <see cref="ControlFlowGraph.Exit"/>, which has none.</param>
    public ImmutableArray<ControlFlowEdge> SuccessorsOf(int node) => _successors[node];

    /// <summary>
    /// The edges that reach a node: where control can come to it from, normally or by an error.
    /// </summary>
    /// <param name="node">The offset of an instruction, or <see cref="ControlFlowGraph.Exit"/>.</param>
    public ImmutableArray<ControlFlowEdge> PredecessorsOf(int node) => _predecessors[node];

    internal static FlowEdges Of(ControlFlowGraph graph, ImmutableHashSet<ErrorHandlingState>[] states, IReadOnlySet<int> faults)
    {
        var items = graph.Instructions.Items;
        var successors = Enumerable.Range(0, items.Length + 1).Select(_ => new List<ControlFlowEdge>()).ToArray();

        void Add(int from, int to, ControlFlowEdgeKind kind)
        {
            var edge = new ControlFlowEdge(from, to, kind);
            if (!successors[from].Contains(edge))
            {
                successors[from].Add(edge);
            }
        }

        foreach (var instruction in items)
        {
            foreach (var next in ErrorHandlingStates.NormalSuccessorsOf(graph, instruction, faults))
            {
                Add(instruction.Offset, next, ControlFlowEdgeKind.Branch);
            }

            if (!graph.CanRaise(instruction.Offset))
            {
                continue;
            }

            foreach (var state in states[instruction.Offset])
            {
                if (state.CatchingHandler is { } handler)
                {
                    Add(instruction.Offset, handler, ControlFlowEdgeKind.Error);
                }
                else if (state is { Policy: ErrorHandlingPolicy.ResumeNext, IsHandling: false })
                {
                    foreach (var next in graph.ContinuationsAfterAnError(instruction.Offset))
                    {
                        Add(instruction.Offset, next, ControlFlowEdgeKind.Error);
                    }
                }
                else
                {
                    Add(instruction.Offset, graph.Exit, ControlFlowEdgeKind.Error);
                }
            }
        }

        return new FlowEdges([.. successors.Select(edges => edges.ToImmutableArray())]);
    }
}
