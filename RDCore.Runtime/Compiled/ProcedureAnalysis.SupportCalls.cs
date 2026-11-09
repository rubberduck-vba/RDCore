using RDCore.Runtime.LanguageCore.Instructions;

namespace RDCore.Runtime.Compiled;

// a statement the interpreter runs for compiled code, as a call compiled code makes.
internal sealed partial class ProcedureAnalysis
{
    // the statement of instruction, which compiled code does not run, as a support call (CompiledActivation.RunStatement): the reasons it gave since mark are
    // not the procedure's. Not inside a With block, whose object the statement is evaluated against: that one the interpreter steps (null).
    private SupportCallStep? SupportCallOf(Instruction instruction, int mark)
    {
        if (instruction.EnclosingWith is not null)
        {
            return null;
        }

        _reasons.RemoveRange(mark, _reasons.Count - mark);
        return new SupportCallStep(instruction.Offset);
    }
}

/// <summary>
/// The statement at <see cref="CompiledStep.Offset"/> (a <c>Set</c>, or a call statement) run by the interpreter as a support call, every variable written
/// into the frame before it and read back after it.
/// </summary>
internal sealed record class SupportCallStep(int Offset) : CompiledStep(Offset);
