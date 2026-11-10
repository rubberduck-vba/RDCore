using RDCore.Runtime.LanguageCore.Instructions;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a With block.
internal sealed partial class ProcedureAnalysis
{
    // MS-VBAL §5.4.2.21: the End With of the block opened at the With statement it matches, which compiled code does (CompiledActivation.TryEndWith). The With
    // statement is the interpreter's, which evaluates the block's object and holds it in the frame.
    private static EndWithStep? EndWithOf(Instruction instruction)
        => instruction.Matching is { } opener ? new EndWithStep(instruction.Offset, opener) : null;
}

/// <summary>
/// An <c>End With</c> (MS-VBAL §5.4.2.21): the block opened at <see cref="Opener"/> has no object from here on, and what it held is let go of
/// (<c>CompiledActivation.TryEndWith</c>).
/// </summary>
internal sealed record class EndWithStep(int Offset, int Opener) : CompiledStep(Offset);
