using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Instructions;

/// <summary>
/// One entry of an <see cref="InstructionList"/> — <strong>RD-VBAL §3.5</strong>. <see cref="Offset"/>
/// is both the entry's index into <see cref="InstructionList.Items"/> and the program-counter value
/// that fetches it.
/// </summary>
/// <param name="Offset">The entry's index into the owning <see cref="InstructionList.Items"/>.</param>
/// <param name="Node">
/// The <see cref="StatementNode"/> this instruction was lowered from, or <c>null</c> for an instruction
/// lowering synthesized — a block statement's closer, or the unconditional jump a branch's body ends
/// with — which no source statement corresponds to.
/// </param>
/// <param name="Kind">The control-flow shape of this instruction.</param>
/// <param name="Target">
/// For <see cref="InstructionKind.Jump"/>, <see cref="InstructionKind.LoopBack"/>,
/// <see cref="InstructionKind.ForNext"/>, <see cref="InstructionKind.ForEachNext"/>, and
/// <see cref="InstructionKind.ExitLoop"/>: the resolved offset to branch to. <c>null</c> for a
/// <see cref="InstructionKind.Jump"/> lowered from <c>GoTo</c>/<c>On…GoTo</c> whose operand did not
/// resolve to a label the procedure defines (lowering already reported the
/// <see cref="RDCore.SDK.Model.Errors.VBCompileErrorId.LabelNotDefined"/> diagnostic for it), or for an
/// <see cref="InstructionKind.ExitLoop"/> lowering found no matching enclosing loop for. Unused
/// otherwise.
/// </param>
/// <param name="Targets">
/// For <see cref="InstructionKind.JumpTable"/>: the resolved offset for each label in source order,
/// with a <c>null</c> entry wherever the corresponding label did not resolve. Empty otherwise.
/// </param>
/// <param name="Else">
/// For <see cref="InstructionKind.ConditionalBranch"/>: the offset to branch to when the condition is
/// false, or the case does not match — the next header in the same <c>If</c>/<c>ElseIf</c>/<c>Case</c>
/// chain, or the offset right past the whole construct when there is no next branch to try. Unused
/// otherwise.
/// </param>
/// <param name="End">
/// On a block's own opening instruction (an <c>If</c>'s first header, a pre-test loop's header, a
/// <see cref="InstructionKind.ForOpener"/>/<see cref="InstructionKind.ForEachOpener"/>/
/// <see cref="InstructionKind.With"/>/<see cref="InstructionKind.Select"/>): the offset right past the
/// whole construct. <c>null</c> on every other instruction, including a construct's own inner headers
/// (an <c>ElseIf</c> or a <c>Case</c>) and constructs with no single opening instruction (a bare
/// <c>Do…Loop</c>, a post-test loop, an inline <c>If</c>).
/// </param>
/// <param name="Matching">
/// For a <c>Select Case</c> <c>Case</c> header (<see cref="InstructionKind.ConditionalBranch"/> lowered
/// from a <c>CaseExpressionStatementNode</c>): the offset of the enclosing <see cref="InstructionKind.Select"/>
/// instruction whose hidden selector value this header matches its range clauses against. <c>null</c>
/// otherwise — including on a <c>Case Else</c>, which never reads the selector.
/// </param>
/// <param name="EnclosingWith">
/// The offset of the innermost <see cref="InstructionKind.With"/> instruction lexically enclosing this
/// one, so a <c>.Member</c>/<c>!member</c> with-expression reads the right hidden target however
/// control arrived here — a <c>GoTo</c> into or out of a <c>With</c> block leaves no stale stack to
/// unwind. <c>null</c> when this instruction is not inside any <c>With</c> block.
/// </param>
public sealed record class Instruction(int Offset, StatementNode? Node, InstructionKind Kind, int? Target, ImmutableArray<int?> Targets, int? Else, int? End, int? Matching, int? EnclosingWith);
