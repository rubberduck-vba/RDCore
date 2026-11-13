using RDCore.Runtime.LanguageCore.Flow;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

/// <summary>
/// The edges of a selection: which jumps into it and out of it are refused, and where control leaves it for, which the new procedure routes its caller to.
/// </summary>
/// <remarks>
/// A selection is a region with one entry, its first instruction, and one fall-through exit, the instruction after its last. An edge between two instructions of it
/// moves with the code. An edge from outside that lands in it other than as control flows into a block's first statement is a jump into it; an edge from it to
/// the instruction after it is how it ends, unless a <c>GoTo</c> makes it.
/// </remarks>
internal static class ExtractionControlFlow
{
    public static (List<ExtractionExit> Exits, List<ExtractionRefusal> Refusals) Of(ExtractionSelection selection, ControlFlowGraph graph)
    {
        var region = selection.Region;
        var items = graph.Instructions.Items;
        var first = region.Min();

        // a jump to the first instruction lands on the call, unless the label it names moves with the selection.
        var entered = selection.Labels.Any(label => graph.Instructions.TryGetLabelOffset(label, out var offset) && offset == first);
        var after = region.Max() + 1;
        var refusals = new List<ExtractionRefusal>();
        var exits = new Dictionary<(ExtractionExitKind Kind, int Target), List<SourceLocation>>();

        foreach (var offset in region.Order())
        {
            var instruction = items[offset];
            switch (instruction.Kind)
            {
                case InstructionKind.GoSub or InstructionKind.GoSubTable when graph.SuccessorsOf(offset).Any(edge => edge.Kind is not ControlFlowEdgeKind.Error && edge.To != offset + 1 && !region.Contains(edge.To)):
                    refusals.Add(new ExtractionRefusal(ExtractionRefusalKind.GoSubToOutside, instruction.Location, null));
                    continue;

                // the Return of a routine converted to a procedure leaves the procedure, back to the call.
                case InstructionKind.Return when selection.Routine is not null:
                    continue;

                // a Return goes back to the instruction after any GoSub of the procedure: the selection is the routine's when it holds a GoSub, and part of one when not.
                case InstructionKind.Return when !region.Any(other => items[other].Kind is InstructionKind.GoSub or InstructionKind.GoSubTable):
                    refusals.Add(new ExtractionRefusal(ExtractionRefusalKind.ReturnOfAGoSubOutside, instruction.Location, null));
                    continue;

                // End and Stop do what they do wherever they run.
                case InstructionKind.Halt or InstructionKind.Break or InstructionKind.Return or InstructionKind.GoSub or InstructionKind.GoSubTable:
                    continue;
            }

            // an On…GoTo leaves for each of its labels outside, once however often it names one; a selector out of range falls through, which is no exit.
            if (instruction.Kind is InstructionKind.JumpTable)
            {
                foreach (var target in instruction.Targets.OfType<int>().Where(target => !region.Contains(target)).Distinct())
                {
                    if (instruction.Location is { } location)
                    {
                        var key = (ExtractionExitKind.Label, target);
                        (exits.TryGetValue(key, out var from) ? from : exits[key] = []).Add(location);
                    }
                }

                continue;
            }

            foreach (var edge in graph.SuccessorsOf(offset).Where(edge => edge.Kind is not ControlFlowEdgeKind.Error && !region.Contains(edge.To)))
            {
                var kind = instruction.Kind switch
                {
                    InstructionKind.ExitProcedure => ExtractionExitKind.Procedure,
                    // a block's closer goes on to what follows the block, as a statement does: it is no GoTo.
                    InstructionKind.Jump when instruction.Node is null && edge.To == after => null,
                    InstructionKind.Jump or InstructionKind.ResumeLabel => ExtractionExitKind.Label,
                    _ when edge.To == after => (ExtractionExitKind?)null,
                    InstructionKind.ExitLoop => ExtractionExitKind.Loop,
                    _ => ExtractionExitKind.Label,
                };
                if (kind is { } leaves && instruction.Location is { } location)
                {
                    var key = (leaves, leaves is ExtractionExitKind.Procedure ? graph.Exit : edge.To);
                    (exits.TryGetValue(key, out var from) ? from : exits[key] = []).Add(location);
                }
            }
        }

        foreach (var instruction in items.Where(instruction => !region.Contains(instruction.Offset)))
        {
            // control that comes to the first instruction comes to the selection at its start: when the selection starts with an #If block, that is the start
            // of any branch of it.
            var starts = graph.Arrivals(instruction.Offset, first).ToHashSet();

            // a GoSub right before a routine returns into it, as the statement before it would fall into it.
            if (selection.Routine is not null && instruction.Kind is InstructionKind.GoSub or InstructionKind.GoSubTable && region.Contains(instruction.Offset + 1))
            {
                refusals.Add(new ExtractionRefusal(ExtractionRefusalKind.FallsIntoTheRoutine, instruction.Location, null));
            }

            foreach (var edge in graph.SuccessorsOf(instruction.Offset).Where(edge => region.Contains(edge.To)))
            {
                var jumps = edge.Kind is ControlFlowEdgeKind.Error
                    || instruction.Kind is InstructionKind.Jump or InstructionKind.JumpTable or InstructionKind.GoSub or InstructionKind.GoSubTable or InstructionKind.ResumeLabel or InstructionKind.Return;

                // a routine converted to a procedure is entered by its GoSub and On…GoSub statements, which become its calls, and only by them.
                if (selection.Routine is { } routine && starts.Contains(edge.To) && edge.Kind is not ControlFlowEdgeKind.Error && instruction.Kind is not InstructionKind.Return)
                {
                    var entry = instruction.Kind switch
                    {
                        InstructionKind.GoSub => (ExtractionRefusalKind?)null,
                        InstructionKind.GoSubTable when instruction.Node is OnGoSubStatementNode table && routine.Tables.Contains(table) => null,
                        InstructionKind.GoSubTable => ExtractionRefusalKind.OnGoSubInASingleLineIf,
                        _ when !jumps => ExtractionRefusalKind.FallsIntoTheRoutine,
                        _ => ExtractionRefusalKind.JumpIntoTheSelection,
                    };
                    if (entry is { } refused && !refusals.Any(refusal => refusal.Kind == refused && refusal.Location == instruction.Location))
                    {
                        refusals.Add(new ExtractionRefusal(refused, instruction.Location ?? items[first].Location, null));
                    }

                    continue;
                }

                // a Return comes back after each GoSub of the procedure: back after one of the selection, it ends that GoSub's routine, which moves with the
                // selection when it is in it, and is refused at the GoSub when not.
                if (instruction.Kind is InstructionKind.Return && region.Contains(edge.To - 1) && items[edge.To - 1].Kind is InstructionKind.GoSub or InstructionKind.GoSubTable)
                {
                    continue;
                }

                if (!starts.Contains(edge.To) || jumps && entered)
                {
                    // an error into a handler is told by where the handler is; a jump, by the jump.
                    var where = edge.Kind is ControlFlowEdgeKind.Error ? items[edge.To].Location : instruction.Location;
                    if (!refusals.Any(refusal => refusal.Kind is ExtractionRefusalKind.JumpIntoTheSelection && refusal.Location == where))
                    {
                        refusals.Add(new ExtractionRefusal(ExtractionRefusalKind.JumpIntoTheSelection, where, null));
                    }
                }
            }
        }

        var listed = exits.Select(exit => new ExtractionExit(exit.Key.Kind, exit.Key.Target,
            exit.Key.Kind is ExtractionExitKind.Label ? graph.Instructions.LabelAt(exit.Key.Target) : null, [.. exit.Value])).ToList();
        return (listed, refusals);
    }

    /// <summary>
    /// What extracting the selection changes of its error handling: an error raised in the new procedure, which has no
    /// handler of its own, is raised to its caller at the call.
    /// </summary>
    /// <param name="region">The instructions of the selection.</param>
    /// <param name="states">The error-handling states of the source procedure.</param>
    /// <param name="readsErr">Whether the selection reads <c>Err</c> or <c>Erl</c>.</param>
    /// <param name="isRoutine">Whether the selection is a <c>GoSub</c> routine converted to a procedure, whose <c>Return</c> statements leave it, and cannot raise
    /// the run-time error 3 a <c>Return</c> with no <c>GoSub</c> raises.</param>
    public static (bool StartsWithResumeNext, List<ExtractionBehaviourChange> Changes, List<ExtractionRefusal> Refusals) ErrorHandlingOf(
        IReadOnlySet<int> region, ErrorHandlingStates states, bool readsErr, bool isRoutine = false)
    {
        var graph = states.Graph;
        var items = graph.Instructions.Items;
        var entry = states.At(region.Min());
        var changes = new List<ExtractionBehaviourChange>();
        var refusals = new List<ExtractionRefusal>();

        foreach (var offset in region.Order())
        {
            var instruction = items[offset];
            if (instruction.Kind is InstructionKind.OnErrorGoTo && instruction.Target is { } handler && !region.Contains(handler))
            {
                refusals.Add(new ExtractionRefusal(ExtractionRefusalKind.HandlerOutsideTheSelection, instruction.Location, graph.Instructions.LabelAt(handler)));
            }

            // the error a Resume is about was raised before the selection: the new procedure has none.
            if (instruction.Kind is InstructionKind.ResumeCurrentStatement or InstructionKind.ResumeNext or InstructionKind.ResumeLabel && entry.Any(state => state.IsHandling))
            {
                refusals.Add(new ExtractionRefusal(ExtractionRefusalKind.ResumeOfAnErrorBefore, instruction.Location, null));
            }
        }

        // an On Error of the selection is the new procedure's: what follows the call runs in the state the selection found, which must be what it leaves, unless
        // nothing that follows can tell the two apart.
        if (LeftDifferent(region, graph, entry) && StateMattersAfter(region, graph))
        {
            refusals.AddRange(region.Order().Select(offset => items[offset])
                .Where(instruction => instruction.Kind is InstructionKind.OnErrorGoTo or InstructionKind.OnErrorDisable or InstructionKind.OnErrorResumeNext or InstructionKind.OnErrorReset)
                .Select(instruction => new ExtractionRefusal(ExtractionRefusalKind.ErrorHandlingChanged, instruction.Location, null)));
        }

        var raises = region.Any(offset => graph.CanRaise(offset) && !(isRoutine && items[offset].Kind is InstructionKind.Return));
        if (!raises)
        {
            return (false, changes, refusals);
        }

        var handlers = entry.Select(state => state.CatchingHandler).OfType<int>().Distinct().Order().ToArray();
        if (handlers.Length > 0)
        {
            var names = handlers.Select(handler => graph.Instructions.LabelAt(handler) ?? handler.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var resumes = items
                .Where(instruction => instruction.Kind is InstructionKind.ResumeCurrentStatement or InstructionKind.ResumeNext or InstructionKind.ResumeLabel
                    && states.At(instruction.Offset).Any(state => state.IsHandling && state.Handler is { } handler && handlers.Contains(handler)))
                .Select(instruction => instruction.Location)
                .OfType<SourceLocation>()
                .ToArray();
            changes.Add(resumes.Length > 0
                ? new ExtractionBehaviourChange(ExtractionBehaviourChangeKind.CallerHandlerResumes, [.. names], [.. resumes], IsNote: false)
                : new ExtractionBehaviourChange(ExtractionBehaviourChangeKind.ErlOnly, [.. names], [], IsNote: true));

            if (region.Any(offset => graph.Instructions.LabelAt(offset) is { Length: > 0 } label && label.All(char.IsAsciiDigit)))
            {
                changes.Add(new ExtractionBehaviourChange(ExtractionBehaviourChangeKind.ErlReportsTheCall, [.. names], [], IsNote: false));
            }
        }

        // in a handler, an error is raised to the caller whatever the policy, as it is from a callee.
        var resumingNext = entry.Where(state => !state.IsHandling).Select(state => state.Policy is ErrorHandlingPolicy.ResumeNext).Distinct().ToArray();
        if (resumingNext is [true])
        {
            // the On Error Resume Next the new procedure starts with clears Err, which the selection reads of an error before it: a call does not, an On Error
            // statement does (MS-VBA, Excel-verified).
            if (readsErr)
            {
                changes.Add(new ExtractionBehaviourChange(ExtractionBehaviourChangeKind.ErrClearedOnEntry, [], [], IsNote: false));
            }

            return (true, changes, refusals);
        }

        if (resumingNext.Length > 1)
        {
            changes.Add(new ExtractionBehaviourChange(ExtractionBehaviourChangeKind.ResumeNextOnSomePaths, [], [], IsNote: false));
        }

        return (false, changes, refusals);
    }

    // whether the code that can run after the selection depends on the error handling the selection leaves: an instruction of it can raise an error, or the
    // selection runs again (in a loop), from the state it left inline and from the one it found once extracted.
    private static bool StateMattersAfter(IReadOnlySet<int> region, ControlFlowGraph graph)
    {
        var seen = new HashSet<int>();
        var pending = new Stack<int>(region
            .SelectMany(node => graph.SuccessorsOf(node).Where(edge => edge.Kind is not ControlFlowEdgeKind.Error))
            .Select(edge => edge.To)
            .Where(node => !region.Contains(node)));
        while (pending.TryPop(out var node))
        {
            if (node >= graph.Exit || !seen.Add(node))
            {
                continue;
            }

            if (region.Contains(node) || graph.CanRaise(node))
            {
                return true;
            }

            foreach (var edge in graph.SuccessorsOf(node))
            {
                pending.Push(edge.To);
            }
        }

        return false;
    }

    // where control that comes to the selection arrives: its first instruction, and, when it starts with an #If block, the first of each other branch of the
    // block, or past the block when the block may compile no branch.
    internal static IReadOnlySet<int> EntriesOf(IReadOnlySet<int> region, ControlFlowGraph graph) => graph.EntriesOf(region.Min(), region.Max());

    // whether a path through the selection, from a state it can start in, leaves it in another: its own On Error statements, run from that state.
    private static bool LeftDifferent(IReadOnlySet<int> region, ControlFlowGraph graph, IEnumerable<ErrorHandlingState> entry)
    {
        var items = graph.Instructions.Items;
        foreach (var start in entry)
        {
            var seen = new HashSet<(int, ErrorHandlingState)>();
            var pending = new Stack<(int Node, ErrorHandlingState State)>();
            foreach (var first in EntriesOf(region, graph))
            {
                pending.Push((first, start));
            }

            while (pending.TryPop(out var arrival))
            {
                if (!region.Contains(arrival.Node))
                {
                    if (arrival.State != start)
                    {
                        return true;
                    }

                    continue;
                }

                if (!seen.Add(arrival))
                {
                    continue;
                }

                var instruction = items[arrival.Node];

                // an error the selection's own handler takes, and the next statement under Resume Next: a handler that ends without a Resume leaves the error active,
                // which the call would end - its callee's error goes with it.
                if (graph.CanRaise(arrival.Node))
                {
                    if (arrival.State.CatchingHandler is { } handler && region.Contains(handler))
                    {
                        pending.Push((handler, arrival.State with { IsHandling = true }));
                    }
                    else if (arrival.State is { Policy: ErrorHandlingPolicy.ResumeNext, IsHandling: false })
                    {
                        foreach (var next in graph.ContinuationsAfterAnError(arrival.Node))
                        {
                            pending.Push((next, arrival.State));
                        }
                    }
                }

                var leaves = ErrorHandlingStates.After(instruction, arrival.State);
                foreach (var edge in graph.SuccessorsOf(arrival.Node).Where(edge => edge.Kind is not ControlFlowEdgeKind.Error))
                {
                    pending.Push((edge.To, leaves));
                }
            }
        }

        return false;
    }
}
