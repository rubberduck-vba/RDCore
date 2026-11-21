using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flow;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Flow;

/// <summary>
/// Finds what the flow of a procedure tells of its variables: the reads of its automatic locals that the variable's initial value may reach
/// (<see cref="InitialValueReadFact"/>, <strong>MS-VBAL §2.3</strong>), and the assignments whose value no code path reads
/// (<see cref="UnreadAssignmentFact"/>).
/// </summary>
/// <remarks>
/// <para>
/// Both are read off the data-flow analyses of the body: what definitions reach each read (<see cref="ReachingDefinitions"/>), and what is live once each
/// assignment completes (<see cref="LiveVariables"/>), over every code path the graph has, errors included.
/// </para>
/// <para>
/// A read made by a write to a part of the variable is not a read here: an element or a member written, <c>ReDim Preserve</c>, a <c>Mid</c> statement, the
/// variable passed to a procedure that may take it by reference. Each of those keeps the rest of the value, and the last one is how a procedure gives a
/// variable of its caller a value. Nothing is stated when the pass could not see every expression of the body, or when the graph approximates a path
/// (<see cref="ControlFlowImprecision"/>).
/// </para>
/// </remarks>
public static class VariableFlowAnalysis
{
    /// <summary>
    /// Finds the reads the initial value may reach and the assignments nothing reads.
    /// </summary>
    /// <param name="model">What the static pass found out about the body.</param>
    /// <param name="instructions">The lowered body.</param>
    /// <param name="scopes">The scopes of the workspace, which tell what kind of variable each binding is.</param>
    /// <param name="automaticLocals">
    /// The automatic local variables of the procedure: those declared with <c>Dim</c>, in a procedure that is not <c>Static</c>.
    /// </param>
    /// <returns>Both lists, each in the order it is written in; empty when nothing could be established.</returns>
    public static (ImmutableArray<InitialValueReadFact> Reads, ImmutableArray<UnreadAssignmentFact> Assignments) Of(
        ProcedureSemanticModel model, InstructionList instructions, ScopeTree scopes, IReadOnlySet<SemanticId> automaticLocals)
    {
        if (!model.IsFullyAnalyzed)
        {
            return ([], []);
        }

        var graph = ControlFlowGraph.Of(instructions);
        if (graph.Imprecision != ControlFlowImprecision.None)
        {
            return ([], []);
        }

        var accesses = DefinitionsAndUses.Of(instructions, model, scopes);
        var states = ErrorHandlingStates.Of(graph);
        return (ReadsOf(graph, accesses, ReachingDefinitions.Of(graph, accesses, states), automaticLocals),
            AssignmentsOf(graph, accesses, LiveVariables.Of(graph, accesses, states)));
    }

    private static ImmutableArray<InitialValueReadFact> ReadsOf(
        ControlFlowGraph graph, DefinitionsAndUses accesses, ReachingDefinitions reaching, IReadOnlySet<SemanticId> automaticLocals)
    {
        var reads = new List<InitialValueReadFact>();
        foreach (var use in accesses.Uses.Where(use => automaticLocals.Contains(use.Variable) && graph.IsReachable(use.Offset)))
        {
            // a write to a part of the variable reads the rest of it; passing it by reference may be how it is given a value.
            if (use.Expression is not SimpleNameExpressionNode name
                || accesses.DefinitionsAt(use.Offset).Any(definition => definition.Variable == use.Variable && !definition.IsComplete))
            {
                continue;
            }

            var definitions = reaching.ReachingBefore(use.Offset, use.Variable);
            if (definitions.Any(definition => definition.Kind is VariableDefinitionKind.Entry))
            {
                reads.Add(new InitialValueReadFact(
                    use.Variable.Uri, name.IdentifierName, name.Location, definitions.All(definition => definition.Kind is VariableDefinitionKind.Entry)));
            }
        }

        return [.. reads.DistinctBy(read => read.Location.Range).OrderBy(read => read.Location.Range.Start)];
    }

    private static ImmutableArray<UnreadAssignmentFact> AssignmentsOf(ControlFlowGraph graph, DefinitionsAndUses accesses, LiveVariables live)
    {
        var items = accesses.Instructions.Items;
        var assignments = new List<UnreadAssignmentFact>();
        foreach (var definition in accesses.Definitions.Where(definition => definition is { Kind: VariableDefinitionKind.Assignment, IsComplete: true }
                     && items[definition.Offset].Node is AssignmentStatementNode && graph.IsReachable(definition.Offset)))
        {
            // the name the target writes the variable with: itself, or the member of its module or of Me it is.
            var name = definition.Expression switch
            {
                SimpleNameExpressionNode simple => simple,
                MemberAccessExpressionNode access => access.Member,
                _ => null,
            };
            if (name is not null && !live.LiveAfterCompleting(definition.Offset).Contains(definition.Variable))
            {
                assignments.Add(new UnreadAssignmentFact(definition.Variable.Uri, name.IdentifierName, name.Location));
            }
        }

        return [.. assignments.OrderBy(assignment => assignment.Location.Range.Start)];
    }
}
