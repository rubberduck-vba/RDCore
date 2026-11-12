using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Flow;

/// <summary>
/// The definitions whose value may still be the variable's, at each instruction of a procedure: <em>reaching</em> definitions, over every code path, loops and handlers
/// included.
/// </summary>
/// <remarks>
/// <para>
/// A definition reaches an instruction when some code path from it to there does not go through another definition that replaces the variable whole (a
/// <see cref="VariableDefinition.IsComplete"/> one). It is a forward analysis over the graph, from the definitions every variable has at the start of the activation
/// (<see cref="VariableDefinitionKind.Entry"/>): an entry definition that reaches a use is a read of the argument, of the initial value, or of the value a field had.
/// </para>
/// <para>
/// What may happen counts as happening, so that "does not reach" is true on every path the graph has (<see cref="ErrorHandlingStates"/> has the edges): an
/// instruction that raises an error may have written some of what it writes (a procedure it called may have written an argument before it failed), and replaced
/// none of it, whether the error goes to a handler, or to the next statement under <c>On Error Resume Next</c>; a <c>Resume</c> may continue at any instruction that
/// raised into a handler.
/// </para>
/// </remarks>
public sealed class ReachingDefinitions
{
    private readonly FlowEdges _edges;
    private readonly ImmutableArray<VariableDefinition>[] _reaching;

    private ReachingDefinitions(ControlFlowGraph graph, FlowEdges edges, ImmutableArray<VariableDefinition>[] reaching)
    {
        Graph = graph;
        _edges = edges;
        _reaching = reaching;
    }

    /// <summary>
    /// The graph the analysis is of.
    /// </summary>
    public ControlFlowGraph Graph { get; }

    /// <summary>
    /// The definitions that reach the instruction at <paramref name="node"/>, before it executes.
    /// </summary>
    /// <param name="node">The offset of an instruction, or <see cref="ControlFlowGraph.Exit"/>.</param>
    public ImmutableArray<VariableDefinition> ReachingBefore(int node) => _reaching[node];

    /// <summary>
    /// The definitions of <paramref name="variable"/> that reach the instruction at <paramref name="node"/>, before it executes.
    /// </summary>
    /// <param name="node">The offset of an instruction, or <see cref="ControlFlowGraph.Exit"/>.</param>
    /// <param name="variable">The variable.</param>
    public ImmutableArray<VariableDefinition> ReachingBefore(int node, SemanticId variable) => [.. _reaching[node].Where(definition => definition.Variable == variable)];

    /// <summary>
    /// The definitions from outside a region of instructions that reach into it, by any edge that enters it.
    /// </summary>
    /// <param name="region">The offsets of the instructions of the region.</param>
    /// <remarks>What code moved to another procedure must be given by its caller is what it reads of these.</remarks>
    public ImmutableArray<VariableDefinition> ReachingInto(IReadOnlySet<int> region)
        => [.. region
            .Where(node => Graph.Entries.Contains(node) || _edges.PredecessorsOf(node).Any(edge => !region.Contains(edge.From)))
            .SelectMany(node => _reaching[node])
            .Where(definition => !region.Contains(definition.Offset))
            .Distinct()];

    /// <summary>
    /// Analyzes a procedure body.
    /// </summary>
    /// <param name="graph">The code paths of the body.</param>
    /// <param name="accesses">The definitions and uses of its variables.</param>
    /// <param name="states">Its error-handling states, when they are already known: the edges errors make are theirs.</param>
    public static ReachingDefinitions Of(ControlFlowGraph graph, DefinitionsAndUses accesses, ErrorHandlingStates? states = null)
    {
        var edges = (states ?? ErrorHandlingStates.Of(graph)).Edges;
        var definitions = accesses.Definitions;
        var indices = definitions.Select((definition, index) => (definition, index)).ToDictionary(pair => pair.definition, pair => pair.index, (IEqualityComparer<VariableDefinition>)ReferenceEqualityComparer.Instance);
        var reaching = Enumerable.Range(0, edges.Count).Select(_ => new HashSet<int>()).ToArray();
        var entryDefinitions = definitions.Select((definition, index) => (definition, index)).Where(pair => pair.definition.Kind is VariableDefinitionKind.Entry).Select(pair => pair.index).ToArray();
        foreach (var entry in graph.Entries)
        {
            reaching[entry].UnionWith(entryDefinitions);
        }

        var pending = new Queue<int>(Enumerable.Range(0, edges.Count));
        var queued = Enumerable.Repeat(true, edges.Count).ToArray();
        while (pending.TryDequeue(out var node))
        {
            queued[node] = false;
            if (node == graph.Exit)
            {
                continue;
            }

            var own = accesses.DefinitionsAt(node).Select(definition => indices[definition]).ToArray();
            // an error edge carries what reached the instruction, and what it may have written before it failed; only completing it replaces anything.
            var replaced = accesses.DefinitionsAt(node).Where(definition => definition.IsComplete).Select(definition => definition.Variable).ToHashSet();

            var completed = new HashSet<int>(reaching[node].Where(index => !replaced.Contains(definitions[index].Variable)));
            completed.UnionWith(own);
            var raised = new HashSet<int>(reaching[node]);
            raised.UnionWith(own);

            foreach (var edge in edges.SuccessorsOf(node))
            {
                var arriving = edge.Kind is ControlFlowEdgeKind.Error ? raised : completed;
                if (!arriving.IsSubsetOf(reaching[edge.To]))
                {
                    reaching[edge.To].UnionWith(arriving);
                    if (!queued[edge.To])
                    {
                        queued[edge.To] = true;
                        pending.Enqueue(edge.To);
                    }
                }
            }
        }

        return new ReachingDefinitions(graph, edges, [.. reaching.Select(set => set.Order().Select(index => definitions[index]).ToImmutableArray())]);
    }
}
