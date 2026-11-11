using System.Collections.Immutable;
using RDCore.Runtime.LanguageCore.Instructions;

namespace RDCore.Runtime.LanguageCore.Flow;

/// <summary>
/// The error-handling states an instruction can run in (<strong>MS-VBAL §5.4.4</strong>), over every code path to it: which <c>On Error</c> statement is in effect, and
/// whether an error is being handled.
/// </summary>
/// <remarks>
/// <para>
/// The states are propagated from the start of the activation (<see cref="ErrorHandlingState.Initial"/>), as MS-VBA runs the statements (observed in Excel):
/// </para>
/// <list type="bullet">
/// <item><description>an <c>On Error</c> statement sets the policy (<strong>MS-VBAL §5.4.4.1</strong>); inside a handler, before it resumes, it sets the policy for after the
/// handler ends, and an error raised meanwhile is still raised to the caller;</description></item>
/// <item><description>an error raised under <c>On Error GoTo</c> reaches the handler with the error active; a <c>Resume</c> ends it (<strong>MS-VBAL §5.4.4.2</strong>), and so
/// does <c>On Error GoTo -1</c>, which keeps the policy: the handler is the next error's again;</description></item>
/// <item><description>a <c>Resume</c> continues at an instruction that raised into a handler, and a <c>Resume Next</c> at the one after it, which only the run knows: every one
/// that can is a place it may continue (<see cref="ControlFlowImprecision.Resumption"/>).</description></item>
/// </list>
/// <para>
/// A state is in the set of an instruction when some code path in the graph reaches it in that state. The states are also where the data-flow analyses take the
/// edges an error makes from: to the handler that takes it, to the next statement under <c>On Error Resume Next</c>, or out of the activation to the caller.
/// </para>
/// </remarks>
public sealed class ErrorHandlingStates
{
    private readonly ImmutableHashSet<ErrorHandlingState>[] _states;

    private ErrorHandlingStates(ControlFlowGraph graph, ImmutableHashSet<ErrorHandlingState>[] states, IReadOnlySet<int> faults)
    {
        Graph = graph;
        _states = states;
        Edges = FlowEdges.Of(graph, states, faults);
    }

    /// <summary>
    /// The graph the states were propagated over.
    /// </summary>
    public ControlFlowGraph Graph { get; }

    /// <summary>
    /// The edges a data-flow analysis walks: the graph's normal edges, and those errors and resumptions make, as the states say.
    /// </summary>
    public FlowEdges Edges { get; }

    /// <summary>
    /// The states the instruction at <paramref name="node"/> can run in: before it executes. Empty when control cannot reach it.
    /// </summary>
    /// <param name="node">The offset of an instruction, or <see cref="ControlFlowGraph.Exit"/>.</param>
    public ImmutableHashSet<ErrorHandlingState> At(int node) => _states[node];

    /// <summary>
    /// The handlers an error raised by the instruction at <paramref name="node"/> can go to: the offsets of their first instruction.
    /// </summary>
    /// <param name="node">The offset of an instruction.</param>
    public ImmutableArray<int> HandlersAt(int node) => [.. _states[node].Select(state => state.CatchingHandler).OfType<int>().Distinct().Order()];

    /// <summary>
    /// Propagates the states of a procedure body.
    /// </summary>
    /// <param name="graph">The code paths of the body.</param>
    public static ErrorHandlingStates Of(ControlFlowGraph graph)
    {
        // where a Resume continues depends on which instructions raise into a handler, which depends on the states a Resume brings back: until it is the same.
        var faults = new HashSet<int>();
        while (true)
        {
            var states = Propagate(graph, faults);
            var found = FaultsOf(graph, states);
            if (found.SetEquals(faults))
            {
                return new ErrorHandlingStates(graph, states, faults);
            }

            faults = found;
        }
    }

    // the instructions that can raise an error into a handler: the fault statements a Resume may continue at.
    private static HashSet<int> FaultsOf(ControlFlowGraph graph, ImmutableHashSet<ErrorHandlingState>[] states)
        => [.. graph.Instructions.Items
            .Where(instruction => graph.CanRaise(instruction.Offset) && states[instruction.Offset].Any(state => state.CatchingHandler is not null))
            .Select(instruction => instruction.Offset)];

    private static ImmutableHashSet<ErrorHandlingState>[] Propagate(ControlFlowGraph graph, IReadOnlySet<int> faults)
    {
        var items = graph.Instructions.Items;
        var states = Enumerable.Range(0, items.Length + 1).Select(_ => new HashSet<ErrorHandlingState>()).ToArray();
        var pending = new Queue<(int Node, ErrorHandlingState State)>();
        foreach (var entry in graph.Entries)
        {
            pending.Enqueue((entry, ErrorHandlingState.Initial));
        }

        while (pending.TryDequeue(out var arrival))
        {
            if (!states[arrival.Node].Add(arrival.State) || arrival.Node == graph.Exit)
            {
                continue;
            }

            var instruction = items[arrival.Node];

            // an error raised in this state goes to the handler, which runs with the error active.
            if (graph.CanRaise(arrival.Node) && arrival.State.CatchingHandler is { } handler)
            {
                pending.Enqueue((handler, arrival.State with { IsHandling = true }));
            }

            // under On Error Resume Next it goes on at the next instruction, in the same state: the only way there for an Error statement, which never completes.
            else if (graph.CanRaise(arrival.Node) && arrival.State is { Policy: ErrorHandlingPolicy.ResumeNext, IsHandling: false })
            {
                foreach (var next in graph.ContinuationsAfterAnError(arrival.Node))
                {
                    pending.Enqueue((next, arrival.State));
                }
            }

            var after = After(instruction, arrival.State);
            foreach (var next in NormalSuccessorsOf(graph, instruction, faults))
            {
                pending.Enqueue((next, after));
            }
        }

        return [.. states.Select(set => set.ToImmutableHashSet())];
    }

    // where control goes when the instruction completes: the graph's edges, and the places a Resume may continue at.
    internal static IEnumerable<int> NormalSuccessorsOf(ControlFlowGraph graph, Instruction instruction, IReadOnlySet<int> faults)
    {
        var successors = graph.SuccessorsOf(instruction.Offset).Where(edge => edge.Kind is not ControlFlowEdgeKind.Error).Select(edge => edge.To);
        return instruction.Kind switch
        {
            InstructionKind.ResumeCurrentStatement => successors.Concat(faults),
            InstructionKind.ResumeNext => successors.Concat(faults.SelectMany(fault => AfterFault(graph, fault))),
            _ => successors,
        };
    }

    // the statement after one that raised an error: where the instruction would have continued, or the next one in order, or where Resume Next goes on after it.
    private static IEnumerable<int> AfterFault(ControlFlowGraph graph, int fault)
        => graph.SuccessorsOf(fault).Where(edge => edge.Kind is not ControlFlowEdgeKind.Error).Select(edge => edge.To)
            .Concat(graph.Arrivals(fault, fault + 1)).Concat(graph.ContinuationsAfterAnError(fault)).Distinct();

    /// <summary>
    /// The state an instruction leaves when it runs to completion: an <c>On Error</c> statement sets the policy, and <c>On Error GoTo -1</c> or a <c>Resume</c>
    /// ends the error being handled. Any other instruction leaves the state as it was.
    /// </summary>
    /// <param name="instruction">The instruction.</param>
    /// <param name="before">The state it runs in.</param>
    public static ErrorHandlingState After(Instruction instruction, ErrorHandlingState before) => instruction.Kind switch
    {
        // inside a handler, the policy set is for after the handler ends: the error stays active (MS-VBA, Excel-verified).
        InstructionKind.OnErrorGoTo => new ErrorHandlingState(ErrorHandlingPolicy.GoTo, instruction.Target, before.IsHandling),
        InstructionKind.OnErrorResumeNext => new ErrorHandlingState(ErrorHandlingPolicy.ResumeNext, null, before.IsHandling),
        InstructionKind.OnErrorDisable => new ErrorHandlingState(ErrorHandlingPolicy.Default, null, before.IsHandling),

        // On Error GoTo -1 ends the active error and keeps the policy (MS-VBA, Excel-verified).
        InstructionKind.OnErrorReset => before with { IsHandling = false },
        InstructionKind.ResumeCurrentStatement or InstructionKind.ResumeNext or InstructionKind.ResumeLabel => before with { IsHandling = false },
        _ => before,
    };
}
