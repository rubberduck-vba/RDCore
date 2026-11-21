using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Semantics.Flow;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Flow;

/// <summary>
/// Finds the statements of a procedure that jump from outside a <c>For</c> or a <c>For Each</c> loop to a label inside it (<strong>MS-VBAL §5.4.2.3</strong>,
/// <strong>§5.4.2.4</strong>).
/// </summary>
/// <remarks>
/// A loop is its statement's lowered body and its <c>Next</c>: the instructions after the <see cref="InstructionKind.ForOpener"/> or the
/// <see cref="InstructionKind.ForEachOpener"/>, up to its <see cref="Instruction.End"/>. A <c>GoTo</c> or a <c>Resume</c> whose resolved
/// <see cref="Instruction.Target"/> is one of them, written outside them, enters the loop without its statement running. A label written before the statement
/// that opens the loop is not inside it. A jump whose target the lowering could not resolve is not stated: where it goes is not known.
/// </remarks>
public static class JumpIntoLoopAnalysis
{
    /// <summary>
    /// Finds the jumps into a loop of a procedure.
    /// </summary>
    /// <param name="instructions">The lowered body of the procedure.</param>
    /// <returns>A fact for each statement that jumps into a loop, in the order they are written; empty when there is none.</returns>
    public static ImmutableArray<JumpIntoLoopFact> Of(InstructionList instructions)
    {
        var loops = instructions.Items
            .Where(instruction => instruction.Kind is InstructionKind.ForOpener or InstructionKind.ForEachOpener && instruction.End is not null)
            .Select(opener => (First: opener.Offset + 1, Past: opener.End!.Value))
            .ToArray();
        if (loops.Length == 0)
        {
            return [];
        }

        var facts = ImmutableArray.CreateBuilder<JumpIntoLoopFact>();
        foreach (var instruction in instructions.Items)
        {
            if (StatementOf(instruction) is { } statement && instruction.Target is { } target
                && loops.Any(loop => target >= loop.First && target < loop.Past && (instruction.Offset < loop.First || instruction.Offset >= loop.Past)))
            {
                facts.Add(new JumpIntoLoopFact(statement, instruction.Node!.SourceLocation));
            }
        }

        return facts.ToImmutable();
    }

    private static JumpStatementKind? StatementOf(Instruction instruction) => instruction switch
    {
        { Kind: InstructionKind.Jump, Node: GoToStatementNode } => JumpStatementKind.GoTo,
        { Kind: InstructionKind.ResumeLabel, Node: ResumeStatementNode } => JumpStatementKind.Resume,
        _ => null,
    };
}
