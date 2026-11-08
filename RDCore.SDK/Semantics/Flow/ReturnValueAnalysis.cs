using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// Tells on how many of the code paths of a function or a property getter the return value is assigned: the function result variable (<strong>MS-VBAL §5.3.1</strong>).
/// </summary>
/// <remarks>
/// <para>
/// The return value is assigned by a statement that writes to the name of the procedure (<see cref="ValueExpressionSemanticFlags.AssignmentTarget"/> on an expression
/// that is bound to the procedure itself), which is a fact of the expressions the static pass analyzed: the question is then whether
/// <see cref="ControlFlowGraph.CanReach"/> finds a code path to the end of the activation that does not go through one.
/// </para>
/// <para>
/// A fact is stated only when it is true, so nothing is stated when it could not be established: when the static pass found something wrong with the procedure or did not
/// analyze every expression of it (an assignment may be in what it did not see), or when the graph approximates a jump (<see cref="ControlFlowImprecision.UnresolvedJump"/>,
/// <see cref="ControlFlowImprecision.Subroutines"/>).
/// </para>
/// <para>
/// The paths are those of the edges <see cref="ErrorHandlingStates"/> derives, not the graph's own: where a <c>Resume</c> continues and where an error goes under
/// <c>On Error Resume Next</c> are paths of their own, and an assignment reached only through one is reached (<see cref="ControlFlowImprecision.Resumption"/>). An
/// error raised to the caller ends the activation with no value returned, and is no path to a return.
/// </para>
/// </remarks>
public static class ReturnValueAnalysis
{
    /// <summary>
    /// Tells about the return value of a procedure.
    /// </summary>
    /// <param name="declaration">The declaration of the procedure.</param>
    /// <param name="model">What the static pass found out about its body.</param>
    /// <param name="instructions">Its lowered body.</param>
    /// <returns>
    /// The fact, or <see langword="null"/> when the procedure does not return a value (it is not a function or a property getter) or the fact could not be established.
    /// </returns>
    public static ReturnValueFact? Of(MemberDeclarationNode declaration, ProcedureSemanticModel model, InstructionList instructions)
    {
        if (declaration.MemberKind is not (MemberKind.Function or MemberKind.PropertyGet) || !model.IsFullyAnalyzed)
        {
            return null;
        }

        var graph = ControlFlowGraph.Of(instructions, model);
        if (graph.Imprecision.HasFlag(ControlFlowImprecision.UnresolvedJump) || graph.Imprecision.HasFlag(ControlFlowImprecision.Subroutines))
        {
            return null;
        }

        var states = ErrorHandlingStates.Of(graph);
        var assigns = instructions.Items.Select(instruction => Assigns(instruction, model)).ToArray();
        var assignment = !ReturnsWithout(graph, states, assigns)
            ? ReturnValueAssignment.Always
            : instructions.Items.Any(instruction => assigns[instruction.Offset] && !states.At(instruction.Offset).IsEmpty)
                ? ReturnValueAssignment.Sometimes
                : ReturnValueAssignment.Never;

        var name = new SourceLocation(declaration.SourceLocation.Uri, declaration.NameRange ?? declaration.SourceLocation.Range);
        return new ReturnValueFact(assignment, declaration.Name, name, IsEmpty: instructions.Items.IsEmpty);
    }

    // whether a path returns from the activation on which no assignment completes: an instruction that raises an error has not. An error edge to the end is a
    // return when Resume Next goes on past the last instruction, and returns nothing when the error is raised to the caller.
    private static bool ReturnsWithout(ControlFlowGraph graph, ErrorHandlingStates states, bool[] assigns)
    {
        var edges = states.Edges;
        var visited = new bool[edges.Count];
        var pending = new Stack<int>();
        pending.Push(graph.Entry);
        visited[graph.Entry] = true;
        while (pending.TryPop(out var node))
        {
            if (node == graph.Exit)
            {
                return true;
            }

            foreach (var edge in edges.SuccessorsOf(node))
            {
                var completes = edge.Kind is not ControlFlowEdgeKind.Error;
                var raisedToTheCaller = !completes && edge.To == graph.Exit
                    && !states.At(node).Any(state => state is { Policy: ErrorHandlingPolicy.ResumeNext, IsHandling: false });
                if (completes && assigns[node] || raisedToTheCaller || visited[edge.To])
                {
                    continue;
                }

                visited[edge.To] = true;
                pending.Push(edge.To);
            }
        }

        return false;
    }

    // what the statement itself writes to, not what is in the blocks it guards: those are instructions of their own.
    private static bool Assigns(Instruction instruction, ProcedureSemanticModel model)
        => instruction.Node is { } statement
           && ExpressionsOf(statement).Any(node =>
               model.Expressions.TryGetValue(node.Identity, out var fact)
               && fact.Binding == model.Procedure
               && fact.Flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget));

    private static IEnumerable<SyntaxNode> ExpressionsOf(SyntaxNode node)
    {
        foreach (var child in node.Children)
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
