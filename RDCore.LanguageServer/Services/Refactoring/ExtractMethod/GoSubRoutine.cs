using System.Collections.Immutable;
using RDCore.Runtime.LanguageCore.Flow;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

/// <summary>
/// Why a label names no <c>GoSub</c> routine that can be converted to a procedure.
/// </summary>
internal enum GoSubRoutineRefusal
{
    /// <summary>
    /// The procedure has no label of that name.
    /// </summary>
    NoLabel,

    /// <summary>
    /// No <c>GoSub</c> of the procedure names the label.
    /// </summary>
    NoGoSub,

    /// <summary>
    /// What runs from the label until a <c>Return</c> is not one run of whole statements that starts at the label: a jump in it goes elsewhere, or it holds code that
    /// never runs from the label.
    /// </summary>
    NotContiguous,
}

/// <summary>
/// A <c>GoSub</c> routine: the statements that run from a label until a <c>Return</c>, sharing the procedure's frame, which a <c>GoSub</c> runs. Converted to a
/// procedure, each <c>GoSub</c> of it is a call, each <c>On…GoSub</c> of it a <c>Select Case</c> with the call in its case, and each <c>Return</c> leaves the
/// procedure.
/// </summary>
/// <param name="Label">The label, as it is written.</param>
/// <param name="Declaration">The label, where it is written: the routine's first line.</param>
/// <param name="Sites">The <c>GoSub</c> statements that run the routine.</param>
/// <param name="Tables">The <c>On…GoSub</c> statements that run the routine, among others, and can be written as a <c>Select Case</c>: none is in a single-line
/// <c>If</c>.</param>
/// <param name="Returns">The routine's <c>Return</c> statements.</param>
/// <param name="IsNamedInside">Whether a jump in the routine names its label, which then stays with it.</param>
internal sealed record class GoSubRoutine(string Label, LineLabelNode Declaration, ImmutableArray<GoSubStatementNode> Sites, ImmutableArray<OnGoSubStatementNode> Tables,
    ImmutableArray<ReturnStatementNode> Returns, bool IsNamedInside)
{
    /// <summary>
    /// Finds the routine a label starts, as a selection to extract.
    /// </summary>
    /// <param name="procedure">The procedure the label is in.</param>
    /// <param name="instructions">Its lowered body.</param>
    /// <param name="graph">Its code paths.</param>
    /// <param name="directives">The <c>#If</c> blocks of its module.</param>
    /// <param name="label">The label's name, or line number.</param>
    /// <param name="selection">The routine's statements, whose <see cref="ExtractionSelection.Routine"/> is the routine, when the label starts one.</param>
    /// <param name="refusal">Why the label starts none, otherwise.</param>
    /// <returns>Whether the label starts a routine.</returns>
    /// <remarks>
    /// The routine is what runs from the label over the code paths as they are written: up to each <c>Return</c>, and over a <c>GoSub</c> in it as a call, to
    /// the statement after it. It is one run of whole statements, from the label to its last instruction; whether it can be converted (what jumps into it, what
    /// leaves it) is what the extraction's plan says of it.
    /// </remarks>
    public static bool TryFind(MemberDeclarationNode procedure, InstructionList instructions, ControlFlowGraph graph, ExtractionDirectives directives, string label,
        out ExtractionSelection? selection, out GoSubRoutineRefusal refusal)
    {
        selection = null;
        var declaration = procedure.Descendants().OfType<LineLabelNode>().FirstOrDefault(node => node.Name.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (declaration is null || !instructions.TryGetLabelOffset(declaration.Name, out var start))
        {
            refusal = GoSubRoutineRefusal.NoLabel;
            return false;
        }

        var items = instructions.Items;
        if (!items.Any(item => item.Kind is InstructionKind.GoSub && item.Target == start || item.Kind is InstructionKind.GoSubTable && item.Targets.Contains(start)))
        {
            refusal = GoSubRoutineRefusal.NoGoSub;
            return false;
        }

        var written = graph.AsWritten();
        var reached = new HashSet<int>();
        var pending = new Stack<int>([start]);
        while (pending.TryPop(out var node))
        {
            if (node >= written.Exit || !reached.Add(node))
            {
                continue;
            }

            switch (items[node].Kind)
            {
                case InstructionKind.Return:
                    continue;

                // a GoSub in the routine comes back to the statement after it.
                case InstructionKind.GoSub or InstructionKind.GoSubTable:
                    pending.Push(node + 1);
                    continue;
            }

            foreach (var edge in written.SuccessorsOf(node).Where(edge => edge.Kind is not ControlFlowEdgeKind.Error))
            {
                pending.Push(edge.To);
            }
        }

        refusal = GoSubRoutineRefusal.NotContiguous;
        var last = reached.Max();
        var ends = reached.Select(offset => items[offset].Location).OfType<SourceLocation>().Select(location => location.Range.End).ToArray();
        if (reached.Min() != start || reached.Count != last - start + 1 || ends.Length == 0
            || !ExtractionSelection.TrySnap(procedure, instructions, directives, new SourceRange(declaration.SourceLocation.Range.Start, ends.Max()), out var snapped, out _, out _)
            || !snapped!.Region.SetEquals(reached)
            || !snapped.Labels.Contains(declaration.Name, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // an On…GoSub in a single-line If cannot be written as the Select Case it becomes: the extraction's plan refuses it.
        var inline = procedure.Descendants().OfType<InlineIfStatementNode>()
            .SelectMany(statement => statement.Descendants().OfType<OnGoSubStatementNode>())
            .ToHashSet();
        var routine = new GoSubRoutine(
            declaration.Name,
            declaration,
            [.. items.Where(item => item.Kind is InstructionKind.GoSub && item.Target == start).Select(item => item.Node).OfType<GoSubStatementNode>()],
            [.. items.Where(item => item.Kind is InstructionKind.GoSubTable && item.Targets.Contains(start)).Select(item => item.Node).OfType<OnGoSubStatementNode>()
                .Where(statement => !inline.Contains(statement)).Distinct()],
            [.. reached.Order().Select(offset => items[offset].Node).OfType<ReturnStatementNode>()],
            reached.Any(offset => items[offset].Kind is not (InstructionKind.GoSub or InstructionKind.GoSubTable)
                && (items[offset].Target == start || items[offset].Targets.Contains(start))));
        selection = snapped with { Routine = routine };
        return true;
    }
}
