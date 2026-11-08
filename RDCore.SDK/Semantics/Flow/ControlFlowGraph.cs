using RDCore.SDK.Semantics.Instructions;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// The code paths of a procedure: every way control can pass from one <see cref="Instruction"/> of an <see cref="InstructionList"/> to another, from the first
/// instruction to the end of the activation (<strong>RD-VBAL §5.0.3</strong>).
/// </summary>
/// <remarks>
/// <para>
/// A <em>code path</em> is a walk from <see cref="Entry"/> along the edges of the graph. The graph does not evaluate anything, so every branch of a condition is a
/// path, except where the condition is the literal <c>True</c> or <c>False</c>; a path that exists in the graph may be one that no input could take, and the
/// questions the graph answers are the ones whose answer does not depend on that - whether there <em>is</em> a way to reach something, never whether it
/// <em>will</em> be taken.
/// </para>
/// <para>
/// The activation ends at <see cref="Exit"/>: a virtual node that is not an instruction, which control reaches by falling off the end of the body and by an
/// <c>Exit Sub</c>, <c>Exit Function</c> or <c>Exit Property</c>. <c>End</c> halts the whole program and does not reach it; neither does an error that no handler
/// takes.
/// </para>
/// <para>
/// An <c>On Error GoTo</c> handler is reached from every instruction that can raise an error while the statement is in effect, <em>before</em> the instruction
/// completes (<see cref="ControlFlowEdgeKind.Error"/>). What the graph could not describe exactly is <see cref="Imprecision"/>.
/// </para>
/// </remarks>
public sealed class ControlFlowGraph
{
    private readonly ImmutableArray<ControlFlowEdge>[] _successors;
    private readonly IReadOnlySet<int> _raiseFree;

    private ControlFlowGraph(InstructionList instructions, ImmutableArray<ControlFlowEdge>[] successors, ControlFlowImprecision imprecision, IReadOnlySet<int> raiseFree)
    {
        Instructions = instructions;
        _successors = successors;
        Imprecision = imprecision;
        _raiseFree = raiseFree;
    }

    /// <summary>
    /// The instructions the nodes of the graph are the offsets of.
    /// </summary>
    public InstructionList Instructions { get; }

    /// <summary>
    /// The node control starts at: the first instruction, or <see cref="Exit"/> when the body has none.
    /// </summary>
    public int Entry => 0;

    /// <summary>
    /// The node the activation ends at, which is one past the last instruction.
    /// </summary>
    public int Exit => Instructions.Items.Length;

    /// <summary>
    /// What the graph could not describe exactly.
    /// </summary>
    public ControlFlowImprecision Imprecision { get; }

    /// <summary>
    /// Builds the graph of a procedure body.
    /// </summary>
    /// <param name="instructions">The lowered body of a procedure.</param>
    /// <param name="model">
    /// What the static pass found out about the body, which tells of the assignments that cannot raise an error (a literal let-coerced without one); without it,
    /// every instruction of a kind that can raise one is taken to.
    /// </param>
    public static ControlFlowGraph Of(InstructionList instructions, ProcedureSemanticModel? model = null)
        => new Builder(instructions, model is null ? new HashSet<int>() : RaiseFreeAssignments.Of(instructions, model)).Build();

    /// <summary>
    /// Whether the instruction at <paramref name="node"/> can raise an error: one of a kind that can (<see cref="CanRaiseError"/>), and not an assignment the
    /// static pass tells cannot.
    /// </summary>
    /// <param name="node">The offset of an instruction.</param>
    public bool CanRaise(int node) => node < Instructions.Items.Length && CanRaiseError(Instructions.Items[node].Kind) && !_raiseFree.Contains(node);

    /// <summary>
    /// The ways control can pass from <paramref name="node"/> to another.
    /// </summary>
    /// <param name="node">The offset of an instruction; <see cref="Exit"/> has none.</param>
    public ImmutableArray<ControlFlowEdge> SuccessorsOf(int node) => _successors[node];

    /// <summary>
    /// Whether control can ever arrive at <paramref name="node"/>.
    /// </summary>
    /// <param name="node">The offset of an instruction, or <see cref="Exit"/>.</param>
    public bool IsReachable(int node) => CanReach(node, _ => false);

    /// <summary>
    /// Whether there is a code path from the start of the procedure to <paramref name="target"/> on which no instruction satisfies <paramref name="occurs"/>.
    /// </summary>
    /// <param name="target">The offset of an instruction, or <see cref="Exit"/>.</param>
    /// <param name="occurs">
    /// What the path must avoid: an instruction that satisfies it, once it completes, ends every path through it. An instruction that raises an error does not complete,
    /// so the path to a handler that leaves it is not ended by it; and the <paramref name="target"/> itself, which the path arrives at and does not execute, is not
    /// tested.
    /// </param>
    /// <remarks>
    /// "Is the return value assigned on every path to the end?" is the question <c>!CanReach(Exit, assigns)</c>; "is this statement dead code?" is
    /// <c>!IsReachable(node)</c>. The answer is exact for the paths the graph has (see <see cref="Imprecision"/> for those it approximates).
    /// </remarks>
    public bool CanReach(int target, Func<Instruction, bool> occurs)
    {
        var visited = new bool[_successors.Length];
        var pending = new Stack<int>();
        pending.Push(Entry);
        visited[Entry] = true;

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node == target)
            {
                return true;
            }

            var completes = node != Exit && occurs(Instructions.Items[node]);
            foreach (var edge in _successors[node])
            {
                if ((completes && edge.Kind is not ControlFlowEdgeKind.Error) || visited[edge.To])
                {
                    continue;
                }

                visited[edge.To] = true;
                pending.Push(edge.To);
            }
        }

        return false;
    }

    private sealed class Builder(InstructionList instructions, IReadOnlySet<int> raiseFree)
    {
        private readonly ImmutableArray<Instruction> _items = instructions.Items;
        private readonly List<ControlFlowEdge>[] _edges = [.. Enumerable.Range(0, instructions.Items.Length + 1).Select(_ => new List<ControlFlowEdge>())];
        private ControlFlowImprecision _imprecision;

        private int Exit => _items.Length;

        public ControlFlowGraph Build()
        {
            foreach (var instruction in _items)
            {
                AddNormalEdges(instruction);
            }

            AddReturnEdges();
            AddErrorEdges();

            return new ControlFlowGraph(instructions, [.. _edges.Select(edges => edges.ToImmutableArray())], _imprecision, raiseFree);
        }

        private void Add(int from, int to, ControlFlowEdgeKind kind)
        {
            if (!_edges[from].Contains(new ControlFlowEdge(from, to, kind)))
            {
                _edges[from].Add(new ControlFlowEdge(from, to, kind));
            }
        }

        private void Branch(Instruction instruction, int? target)
        {
            if (target is { } to)
            {
                Add(instruction.Offset, to, ControlFlowEdgeKind.Branch);
            }
            else
            {
                _imprecision |= ControlFlowImprecision.UnresolvedJump;
            }
        }

        private void Fallthrough(Instruction instruction) => Add(instruction.Offset, instruction.Offset + 1, ControlFlowEdgeKind.Fallthrough);

        private void AddNormalEdges(Instruction instruction)
        {
            switch (instruction.Kind)
            {
                case InstructionKind.Jump:
                case InstructionKind.ExitLoop:
                case InstructionKind.ResumeLabel:
                    Branch(instruction, instruction.Target);
                    break;

                case InstructionKind.JumpTable:
                    foreach (var target in instruction.Targets)
                    {
                        Branch(instruction, target);
                    }

                    // an out-of-range selector falls through.
                    Fallthrough(instruction);
                    break;

                case InstructionKind.GoSub:
                    _imprecision |= ControlFlowImprecision.Subroutines;
                    Branch(instruction, instruction.Target);
                    break;

                case InstructionKind.GoSubTable:
                    _imprecision |= ControlFlowImprecision.Subroutines;
                    foreach (var target in instruction.Targets)
                    {
                        Branch(instruction, target);
                    }

                    Fallthrough(instruction);
                    break;

                case InstructionKind.Return:
                    _imprecision |= ControlFlowImprecision.Subroutines;
                    break;

                case InstructionKind.ExitProcedure:
                    Add(instruction.Offset, Exit, ControlFlowEdgeKind.Branch);
                    break;

                case InstructionKind.ConditionalBranch:
                    AddConditionalEdges(instruction);
                    break;

                case InstructionKind.LoopBack:
                    switch (ConstantFlow.OutcomeOf(instruction))
                    {
                        case true:
                            Branch(instruction, instruction.Target);
                            break;
                        case false:
                            Fallthrough(instruction);
                            break;
                        default:
                            Branch(instruction, instruction.Target);
                            Fallthrough(instruction);
                            break;
                    }

                    break;

                case InstructionKind.ForNext:
                case InstructionKind.ForEachNext:
                    Branch(instruction, instruction.Target);
                    Fallthrough(instruction);
                    break;

                case InstructionKind.ForOpener:
                case InstructionKind.ForEachOpener:
                    // a loop that has nothing to run over is skipped.
                    var runs = instruction.Kind is InstructionKind.ForOpener ? ConstantFlow.RunsAtLeastOnce(instruction) : null;
                    if (runs is not false)
                    {
                        Fallthrough(instruction);
                    }

                    if (runs is not true)
                    {
                        Branch(instruction, instruction.End);
                    }

                    break;

                case InstructionKind.ResumeCurrentStatement:
                case InstructionKind.ResumeNext:
                    _imprecision |= ControlFlowImprecision.Resumption;
                    break;

                case InstructionKind.Halt:
                case InstructionKind.RaiseError:
                    break;

                default:
                    Fallthrough(instruction);
                    break;
            }
        }

        private void AddConditionalEdges(Instruction instruction)
        {
            var outcome = ConstantFlow.OutcomeOf(instruction);
            if (outcome is not false)
            {
                Fallthrough(instruction);
            }

            if (outcome is not true)
            {
                Branch(instruction, instruction.Else);
            }
        }

        // a Return goes back to just after whichever GoSub sent control there, and which one is not known.
        private void AddReturnEdges()
        {
            var continuations = _items
                .Where(instruction => instruction.Kind is InstructionKind.GoSub or InstructionKind.GoSubTable)
                .Select(instruction => instruction.Offset + 1)
                .ToArray();

            foreach (var instruction in _items.Where(instruction => instruction.Kind is InstructionKind.Return))
            {
                foreach (var continuation in continuations)
                {
                    Add(instruction.Offset, continuation, ControlFlowEdgeKind.Branch);
                }
            }
        }

        // an error raised while On Error GoTo is in effect goes to the handler. It is in effect from the statement until another On Error statement replaces it.
        private void AddErrorEdges()
        {
            foreach (var statement in _items.Where(instruction => instruction.Kind is InstructionKind.OnErrorGoTo && instruction.Target is not null))
            {
                foreach (var node in ProtectedBy(statement))
                {
                    if (CanRaiseError(_items[node].Kind) && !raiseFree.Contains(node))
                    {
                        Add(node, statement.Target!.Value, ControlFlowEdgeKind.Error);
                    }
                }
            }
        }

        private List<int> ProtectedBy(Instruction statement)
        {
            var protectedNodes = new List<int>();
            var visited = new bool[_edges.Length];
            var pending = new Stack<int>();
            pending.Push(statement.Offset + 1);

            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node == Exit || visited[node])
                {
                    continue;
                }

                visited[node] = true;
                if (_items[node].Kind is InstructionKind.OnErrorGoTo or InstructionKind.OnErrorDisable or InstructionKind.OnErrorResumeNext)
                {
                    // the policy of the statement ends here.
                    continue;
                }

                protectedNodes.Add(node);
                foreach (var edge in _edges[node].Where(edge => edge.Kind is not ControlFlowEdgeKind.Error))
                {
                    pending.Push(edge.To);
                }
            }

            return protectedNodes;
        }
    }

    /// <summary>
    /// Whether an instruction of this kind can raise an error: it evaluates something, or is <c>Error n</c>, or is a <c>Return</c> (error 3, with no <c>GoSub</c>) or
    /// a <c>Resume</c> (error 20, with no active error).
    /// </summary>
    /// <param name="kind">The kind of the instruction.</param>
    internal static bool CanRaiseError(InstructionKind kind) => kind is
        InstructionKind.Simple or InstructionKind.ConditionalBranch or InstructionKind.LoopBack or InstructionKind.ForOpener or InstructionKind.ForNext
        or InstructionKind.ForEachOpener or InstructionKind.ForEachNext or InstructionKind.With or InstructionKind.Select or InstructionKind.JumpTable
        or InstructionKind.GoSubTable or InstructionKind.RaiseError or InstructionKind.Return or InstructionKind.ResumeCurrentStatement or InstructionKind.ResumeNext
        or InstructionKind.ResumeLabel;

    /// <summary>
    /// Where <c>On Error Resume Next</c> goes on after the instruction raised an error: the next instruction, but past the <c>End Select</c> for the expression of a
    /// <c>Select Case</c>, with no <c>Case</c> run.
    /// </summary>
    /// <remarks>
    /// Measured in MS-VBA (Excel-verified): an error in the expression of a <c>Select Case</c> goes on after <c>End Select</c>, the <c>Case Else</c> included; one in a
    /// <c>Case</c> expression runs that <c>Case</c>'s body, one in an <c>If</c> or <c>ElseIf</c> condition runs that branch, one in a <c>Do While</c>, <c>Do Until</c> or
    /// <c>While</c> condition runs the body, and one in a <c>Loop Until</c> or <c>Loop While</c> condition leaves the loop - which is the next instruction for each of
    /// them as they are lowered. An error in the start, the end or the step of a <c>For</c> goes on at the first statement of the body, with no test of the bounds and
    /// the counter not assigned: the next instruction too, along whose error edge the counter keeps the value it had. An error in the collection of a
    /// <c>For Each</c> or in the object of a <c>With</c> is still to be measured, and goes on at the next instruction meanwhile: the first statement of the body.
    /// </remarks>
    /// <param name="instruction">The instruction that raised the error.</param>
    internal static int NextAfterAnError(Instruction instruction)
        => instruction is { Kind: InstructionKind.Select, End: { } after } ? after : instruction.Offset + 1;
}
