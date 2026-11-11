using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Flow;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Flow;

/// <summary>
/// Finds the reads of a procedure's own <c>For Each</c> control variables that see the variable as the end of the loop left it: emptied, where
/// <strong>MS-VBAL §5.4.2.4</strong> says it holds the last element (<see cref="EmptiedControlReadFact"/>).
/// </summary>
/// <remarks>
/// <para>
/// A read is stated only when it sees an emptied variable on every code path that reaches it: each one leaves a loop over the variable by its <c>Next</c> running
/// out of elements, or by its opener finding none, and assigns nothing to the variable after that. A loop left by <c>Exit For</c>, a jump or an error keeps the
/// element it was at, so a read that one of those can reach is not stated. The usual search, which leaves the loop by <c>Exit For</c> when it finds the element
/// and then tests the variable, is therefore not stated.
/// </para>
/// <para>
/// Only an automatic local variable of the procedure is followed, a <c>Dim</c> of a procedure that is not <c>Static</c>: nothing but the procedure's own code
/// assigns it. Passing it to a procedure is taken to assign it, as a <c>ByRef</c> parameter may, and is not taken to read it. Nothing is stated when the pass could
/// not see every expression of the body, when the graph approximates a path (<see cref="ControlFlowImprecision"/>), or in a procedure with
/// <c>On Error Resume Next</c>, whose errors go on at the next statement on paths the graph does not have.
/// </para>
/// </remarks>
public static class ForEachControlAnalysis
{
    /// <summary>
    /// Finds the reads of the procedure's <c>For Each</c> control variables that see them emptied by the end of the loop.
    /// </summary>
    /// <param name="model">What the static pass found out about the body.</param>
    /// <param name="instructions">The lowered body.</param>
    /// <param name="automaticLocals">The automatic local variables of the procedure: those declared with <c>Dim</c>, in a procedure that is not <c>Static</c>.</param>
    /// <returns>The reads, in the order they are written in; none when nothing could be established.</returns>
    public static ImmutableArray<EmptiedControlReadFact> Of(ProcedureSemanticModel model, InstructionList instructions, IReadOnlySet<SemanticId> automaticLocals)
    {
        var items = instructions.Items;
        if (!model.IsFullyAnalyzed || automaticLocals.Count == 0 || items.Any(instruction => instruction.Kind is InstructionKind.OnErrorResumeNext))
        {
            return [];
        }

        // the variable each instruction ends a loop over: an opener, and the Next that matches it.
        var loops = items.Select(instruction => ControlOf(instruction.Kind switch
        {
            InstructionKind.ForEachOpener => instruction,
            InstructionKind.ForEachNext when instruction.Matching is { } opener => items[opener],
            _ => null,
        }, model, automaticLocals)).ToArray();
        if (!loops.Any(control => control is not null))
        {
            return [];
        }

        var graph = ControlFlowGraph.Of(instructions);
        if (graph.Imprecision != ControlFlowImprecision.None)
        {
            return [];
        }

        var reads = new List<EmptiedControlReadFact>();
        foreach (var control in loops.OfType<SemanticId>().Distinct())
        {
            var emptied = EmptiedOnEntry(graph, model, control, loops);
            foreach (var instruction in items.Where(instruction => emptied[instruction.Offset] && graph.IsReachable(instruction.Offset)))
            {
                reads.AddRange(ExpressionsOf(instruction.Node)
                    .Select(node => (Node: node, Fact: model.Expressions.TryGetValue(node.Identity, out var fact) ? fact : null))
                    .Where(read => read.Fact is { } fact && fact.Binding == control && !Writes(fact))
                    .Select(read => new EmptiedControlReadFact(
                        read.Node is SimpleNameExpressionNode name ? name.IdentifierName : string.Empty, read.Fact!.Location,
                        read.Fact.DeclaredType is VBObjectType or VBClassType)));
            }
        }

        return [.. reads.OrderBy(read => read.Location.Range.Start.Line).ThenBy(read => read.Location.Range.Start.Character)];
    }

    // the automatic local variable a For Each opener assigns, when it is one.
    private static SemanticId? ControlOf(Instruction? opener, ProcedureSemanticModel model, IReadOnlySet<SemanticId> automaticLocals)
        => opener?.Node is ForEachStatementNode loop && model.Expressions.TryGetValue(loop.ControlExpression.Identity, out var fact)
           && fact.Binding is { } binding && automaticLocals.Contains(binding)
            ? binding
            : null;

    // whether the control variable is emptied when control arrives at each instruction: on every path to it, the last thing that happened to the variable is the
    // end of a loop over it. Every node starts out emptied but the start of the procedure, and a node stays emptied only while every edge into it brings an emptied
    // variable.
    private static bool[] EmptiedOnEntry(ControlFlowGraph graph, ProcedureSemanticModel model, SemanticId control, SemanticId?[] loops)
    {
        var items = graph.Instructions.Items;
        var incoming = Enumerable.Range(0, graph.Exit + 1).Select(_ => new List<ControlFlowEdge>()).ToArray();
        for (var node = 0; node < graph.Exit; node++)
        {
            foreach (var edge in graph.SuccessorsOf(node))
            {
                incoming[edge.To].Add(edge);
            }
        }

        var writes = items
            .Select(instruction => ExpressionsOf(instruction.Node).Any(node => model.Expressions.TryGetValue(node.Identity, out var fact) && fact.Binding == control && Writes(fact)))
            .ToArray();
        var emptied = Enumerable.Repeat(true, graph.Exit + 1).ToArray();
        foreach (var entry in graph.Entries)
        {
            emptied[entry] = false;
        }

        for (var changed = true; changed;)
        {
            changed = false;
            for (var node = 0; node <= graph.Exit; node++)
            {
                if (emptied[node] && (graph.Entries.Contains(node) || incoming[node].Count == 0 || incoming[node].Any(edge => !Brings(edge))))
                {
                    emptied[node] = false;
                    changed = true;
                }
            }
        }

        return emptied;

        // the end of a loop over the variable empties it: its Next falling through, its opener skipping the body. Any other edge out of a loop over it has
        // assigned it an element; an error on the way to a handler may have left it either way; and an instruction that assigns it does not leave it emptied.
        bool Brings(ControlFlowEdge edge)
        {
            var from = items[edge.From];
            if (loops[edge.From] == control)
            {
                return edge.Kind is not ControlFlowEdgeKind.Error
                    && (from.Kind is InstructionKind.ForEachNext ? edge.Kind is ControlFlowEdgeKind.Fallthrough : edge.To == from.End);
            }

            return !writes[edge.From] && emptied[edge.From];
        }
    }

    // an expression that writes to what it is bound to: the target of an assignment, a loop's control variable, a variable given to a procedure.
    private static bool Writes(ExpressionFact fact)
        => (fact.Flags & (ValueExpressionSemanticFlags.AssignmentTarget | ValueExpressionSemanticFlags.PassedAsArgument)) != 0;

    // what the statement itself evaluates, not what is in the blocks it guards: those are instructions of their own.
    private static IEnumerable<SyntaxNode> ExpressionsOf(SyntaxNode? node)
    {
        foreach (var child in node?.Children ?? [])
        {
            if (child is StatementNode)
            {
                continue;
            }

            yield return child;
            foreach (var descendant in ExpressionsOf(child))
            {
                yield return descendant;
            }
        }
    }
}
