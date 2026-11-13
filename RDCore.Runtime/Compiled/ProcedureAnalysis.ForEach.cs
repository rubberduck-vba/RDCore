using RDCore.Runtime.LanguageCore.Binding;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a For Each: its statement and its Next, run by the interpreter for compiled code as a support call is, with the state the frame
// holds of the loop.
internal sealed partial class ProcedureAnalysis
{
    // the For Each statements analyzed so far, by offset, whose control variable the frame holds: a Next comes after its statement.
    private readonly HashSet<int> _framedForEach = [];

    // every For Each block of the body, by the offset of its statement and the offset right past it.
    private readonly List<(int Opener, int Past)> _forEachBlocks = [];

    /// <summary>
    /// Whether a jump from <paramref name="from"/> to <paramref name="to"/> leaves a <c>For Each</c> block (an <c>Exit For</c>), which ends the loop: what
    /// it held is let go of there (<see cref="CompiledActivation.LeaveLoops"/>), as the interpreter lets go of it.
    /// </summary>
    /// <param name="from">The offset of the jump.</param>
    /// <param name="to">The offset it goes to.</param>
    public bool LeavesForEach(int from, int to)
        => _forEachBlocks.Exists(block => from >= block.Opener && from < block.Past && (to < block.Opener || to >= block.Past));

    // MS-VBAL §5.4.2.4: the For Each statement, which evaluates the collection and assigns the control variable its first element, or skips the loop; and its
    // Next, which assigns it the next one, or ends the loop. The interpreter runs either for compiled code (CompiledActivation.StepForEach), the enumerator and
    // the array it reads held in the frame, where the interpreter holds them. The control variable is a Variant or an object, which the frame holds too. One
    // compiled code holds in a local of its own (a scalar) is the interpreter's to assign, which steps the instruction. A statement inside a With block is
    // evaluated against the block's object, which the interpreter steps too; its Next evaluates nothing.
    private CompiledStep? ForEachStepOf(Instruction instruction)
    {
        if (instruction is { Kind: InstructionKind.ForEachNext, Matching: { } opener, Target: { } body })
        {
            return _framedForEach.Contains(opener) ? new ForEachNextStep(instruction.Offset, opener, body) : null;
        }

        if (instruction is not { Kind: InstructionKind.ForEachOpener, End: { } past, Node: ForEachStatementNode { ControlExpression: var control } }
            || Bind(control) is BoundVariable variable && SlotOf(variable) is not null)
        {
            return null;
        }

        _ = _framedForEach.Add(instruction.Offset);
        return instruction.EnclosingWith is null ? new ForEachOpenStep(instruction.Offset, past) : null;
    }
}

// a For Each statement, run by the interpreter for compiled code with every variable written into the frame: the body next, or the instruction at Past
// when the collection has no element.
internal sealed record class ForEachOpenStep(int Offset, int Past) : CompiledStep(Offset);

// the Next of the For Each at Opener, run by the interpreter for compiled code with the state the frame holds of the loop: the body again, at Body, or the
// instruction after it when the collection has no element left.
internal sealed record class ForEachNextStep(int Offset, int Opener, int Body) : CompiledStep(Offset);
