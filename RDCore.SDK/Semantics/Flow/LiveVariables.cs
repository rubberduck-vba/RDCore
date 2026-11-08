using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// The variables whose value may still be read, at each instruction of a procedure: <em>live</em> variables, over every code path, loops and handlers included.
/// </summary>
/// <remarks>
/// <para>
/// A variable is live before an instruction when some code path from there reads its value before anything replaces it whole (a
/// <see cref="VariableDefinition.IsComplete"/> definition). It is a backward analysis over the graph, and the answer is what makes the loop case right: a value
/// written in the body of a loop and read at its top, on the next iteration, is live after the write.
/// </para>
/// <para>
/// What may happen counts as happening, so that "not live" is true on every path the graph has (<see cref="ErrorHandlingStates"/> has the edges):
/// </para>
/// <list type="bullet">
/// <item><description>an instruction that raises an error does not complete, so what it would have replaced is still live on that edge: into a handler, to the next
/// statement under <c>On Error Resume Next</c>, or out to the caller;</description></item>
/// <item><description>a <c>Resume</c> may continue at any instruction that raised into a handler (<see cref="ControlFlowImprecision.Resumption"/>);</description></item>
/// <item><description>at the end of the activation, and where an error leaves it, what outlives it is live: a <c>ByRef</c> parameter, a field, a <c>Static</c> local, the
/// result of a function (<see cref="DefinitionsAndUses.OutliveTheActivation"/>).</description></item>
/// </list>
/// <para>
/// Where <c>On Error Resume Next</c> goes on after an error in a condition or in the header of a <c>For</c> is as MS-VBA has it
/// (<see cref="ControlFlowGraph.NextAfterAnError"/>, Excel-verified), but for the collection of a <c>For Each</c> and the object of a <c>With</c>, which are still to be
/// measured and go on at the next instruction meanwhile: until they are, "not live" holds on the paths of every other statement.
/// </para>
/// </remarks>
public sealed class LiveVariables
{
    private readonly FlowEdges _edges;
    private readonly ImmutableHashSet<SemanticId>[] _liveBefore;

    private LiveVariables(ControlFlowGraph graph, FlowEdges edges, ImmutableHashSet<SemanticId>[] liveBefore)
    {
        Graph = graph;
        _edges = edges;
        _liveBefore = liveBefore;
    }

    /// <summary>
    /// The graph the analysis is of.
    /// </summary>
    public ControlFlowGraph Graph { get; }

    /// <summary>
    /// The variables live before the instruction at <paramref name="node"/> executes.
    /// </summary>
    /// <param name="node">The offset of an instruction, or <see cref="ControlFlowGraph.Exit"/>.</param>
    public ImmutableHashSet<SemanticId> LiveBefore(int node) => _liveBefore[node];

    /// <summary>
    /// The variables live once the instruction at <paramref name="node"/> <em>completes</em>: live before one of the instructions it continues at when it raises no
    /// error. What an error leaving it makes live is not counted (see <see cref="LiveAfter(IReadOnlySet{int})"/>, which counts every way out of a region).
    /// </summary>
    /// <param name="node">The offset of an instruction.</param>
    public ImmutableHashSet<SemanticId> LiveAfterCompleting(int node)
        => [.. _edges.SuccessorsOf(node).Where(edge => edge.Kind is not ControlFlowEdgeKind.Error).SelectMany(edge => _liveBefore[edge.To])];

    /// <summary>
    /// The variables live as control leaves a region of instructions, by any edge: falling out of it, jumping out of it, or an error leaving it - into a handler
    /// outside it, or to the caller.
    /// </summary>
    /// <param name="region">The offsets of the instructions of the region.</param>
    /// <remarks>What code moved to another procedure must give back to its caller is what is written in it and live here.</remarks>
    public ImmutableHashSet<SemanticId> LiveAfter(IReadOnlySet<int> region)
        => [.. region.SelectMany(node => _edges.SuccessorsOf(node)).Where(edge => !region.Contains(edge.To)).SelectMany(edge => _liveBefore[edge.To])];

    /// <summary>
    /// Whether <paramref name="variable"/> is live before the instruction at <paramref name="node"/>.
    /// </summary>
    /// <param name="node">The offset of an instruction, or <see cref="ControlFlowGraph.Exit"/>.</param>
    /// <param name="variable">The variable.</param>
    public bool IsLiveBefore(int node, SemanticId variable) => _liveBefore[node].Contains(variable);

    /// <summary>
    /// Analyzes a procedure body.
    /// </summary>
    /// <param name="graph">The code paths of the body.</param>
    /// <param name="accesses">The definitions and uses of its variables.</param>
    /// <param name="states">Its error-handling states, when they are already known: the edges errors make are theirs.</param>
    public static LiveVariables Of(ControlFlowGraph graph, DefinitionsAndUses accesses, ErrorHandlingStates? states = null)
    {
        var edges = (states ?? ErrorHandlingStates.Of(graph)).Edges;
        var live = Enumerable.Range(0, edges.Count).Select(_ => new HashSet<SemanticId>()).ToArray();
        live[graph.Exit].UnionWith(accesses.OutliveTheActivation);

        var pending = new Queue<int>(Enumerable.Range(0, edges.Count).Reverse());
        var queued = Enumerable.Repeat(true, edges.Count).ToArray();
        while (pending.TryDequeue(out var node))
        {
            queued[node] = false;
            if (node == graph.Exit)
            {
                Requeue(node);
                continue;
            }

            var after = new HashSet<SemanticId>();
            var raised = new HashSet<SemanticId>();
            foreach (var edge in edges.SuccessorsOf(node))
            {
                (edge.Kind is ControlFlowEdgeKind.Error ? raised : after).UnionWith(live[edge.To]);
            }

            after.ExceptWith(accesses.DefinitionsAt(node).Where(definition => definition.IsComplete).Select(definition => definition.Variable));
            after.UnionWith(raised);
            after.UnionWith(accesses.UsesAt(node).Select(use => use.Variable));
            if (!after.SetEquals(live[node]))
            {
                live[node] = after;
                Requeue(node);
            }
        }

        return new LiveVariables(graph, edges, [.. live.Select(set => set.ToImmutableHashSet())]);

        void Requeue(int node)
        {
            foreach (var edge in edges.PredecessorsOf(node).Where(edge => !queued[edge.From]))
            {
                queued[edge.From] = true;
                pending.Enqueue(edge.From);
            }
        }
    }
}
