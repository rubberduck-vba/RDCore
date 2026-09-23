namespace RDCore.SDK.Semantics.Instructions;

/// <summary>
/// Discriminates the control-flow shape of an <see cref="Instruction"/> within an
/// <see cref="InstructionList"/> — RD-VBAL §3.5.
/// </summary>
/// <remarks>
/// A statement's own runtime semantics stay pure (<strong>RD-VBAL §3.5</strong>: they never mutate
/// control state); <see cref="InstructionKind"/> only tells the interpreter's fetch/decode loop which
/// pre-resolved offset on <see cref="Instruction"/> to consult, and whether to consult it at all.
/// </remarks>
public enum InstructionKind
{
    /// <summary>
    /// Falls through to the following offset. Every statement kind not covered by another member of
    /// this enum lowers as <see cref="Simple"/>.
    /// </summary>
    Simple,

    /// <summary>
    /// An unconditional branch (<c>GoTo</c>, <strong>MS-VBAL §5.4.2.12</strong>; also a bare
    /// <c>Do…Loop</c>'s back-edge, and the synthesized jump a lowered <c>If</c>/<c>ElseIf</c>/<c>Else</c>
    /// or <c>Case</c>/<c>Case Else</c> branch ends with) to <see cref="Instruction.Target"/>.
    /// </summary>
    Jump,

    /// <summary>
    /// An indexed branch (<c>On…GoTo</c>, <strong>MS-VBAL §5.4.2.13</strong>) to one of
    /// <see cref="Instruction.Targets"/>, selected at runtime; an out-of-range selector falls through
    /// instead.
    /// </summary>
    JumpTable,

    /// <summary>
    /// <c>Exit Sub</c>/<c>Exit Function</c>/<c>Exit Property</c> (<strong>MS-VBAL §5.4.2.17</strong>–
    /// <strong>§5.4.2.19</strong>): ends the current activation as if execution had reached the end of
    /// its body.
    /// </summary>
    ExitProcedure,

    /// <summary>
    /// <c>End</c> (not a MS-VBAL-numbered statement): halts the whole program.
    /// </summary>
    Halt,

    /// <summary>
    /// <c>Stop</c> (<strong>MS-VBAL §5.4.2.11</strong>): suspends execution for a debugger to resume.
    /// </summary>
    Break,

    /// <summary>
    /// An <c>If</c>/<c>ElseIf</c> header, a pre-test loop header (<c>Do While</c>/<c>Do Until</c>/
    /// <c>While…Wend</c>), or a <c>Select Case</c> <c>Case</c> header: evaluates <see cref="Instruction.Node"/>
    /// (a boolean condition, or a case match against the selector <see cref="Instruction.Matching"/>
    /// names); on match/true, falls through; otherwise branches to <see cref="Instruction.Else"/>.
    /// </summary>
    ConditionalBranch,

    /// <summary>
    /// A post-test loop's closer (<c>Do…Loop While</c>/<c>Do…Loop Until</c>, <strong>MS-VBAL
    /// §5.4.2.6</strong>): evaluates <see cref="Instruction.Node"/>'s condition; when the loop should
    /// continue, branches back to <see cref="Instruction.Target"/> (the body's first instruction);
    /// otherwise falls through, ending the loop.
    /// </summary>
    LoopBack,

    /// <summary>
    /// A <c>For</c> loop's opener (<strong>MS-VBAL §5.4.2.3</strong>): evaluates the start/end/step
    /// expressions into per-activation hidden state. Always falls through into the body.
    /// </summary>
    ForOpener,

    /// <summary>
    /// A <c>For</c> loop's <c>Next</c> closer: advances the counter by the step and tests it against the
    /// end bound; while still in range, branches back to <see cref="Instruction.Target"/> (the body's
    /// first instruction); otherwise falls through, ending the loop. A jump directly to a <c>Next</c>
    /// whose opener never ran is <strong>MS-VBAL §5.4.2.3</strong> error 92, "For loop not initialized"
    /// — a runtime concern, not lowering's.
    /// </summary>
    ForNext,

    /// <summary>
    /// A <c>For Each</c> loop's opener (<strong>MS-VBAL §5.4.2.4</strong>): evaluates the collection
    /// expression into a per-activation enumerator. Always falls through into the body.
    /// </summary>
    ForEachOpener,

    /// <summary>
    /// A <c>For Each</c> loop's <c>Next</c> closer: advances the enumerator; while it still has an
    /// element, branches back to <see cref="Instruction.Target"/> (the body's first instruction);
    /// otherwise falls through, ending the loop.
    /// </summary>
    ForEachNext,

    /// <summary>
    /// A <c>With</c> block's opener (<strong>MS-VBAL §5.4.2.21</strong>): evaluates the with-expression
    /// into per-activation hidden state that every instruction lexically inside the block reads through
    /// <see cref="Instruction.EnclosingWith"/>. Always falls through into the body.
    /// </summary>
    With,

    /// <summary>
    /// A <c>Select Case</c> block's opener (<strong>MS-VBAL §5.4.2.10</strong>): evaluates the control
    /// expression into per-activation hidden state each <c>Case</c> header — an <see cref="Instruction"/>
    /// whose <see cref="Instruction.Matching"/> names this opener's offset — compares against. Always
    /// falls through into the first <c>Case</c> header, or past the block when it declares none.
    /// </summary>
    Select,

    /// <summary>
    /// <c>Exit For</c>/<c>Exit Do</c> (<strong>MS-VBAL §5.4.2.5</strong>, <strong>§5.4.2.7</strong>):
    /// branches to <see cref="Instruction.Target"/>, the offset right past the innermost enclosing loop
    /// of the matching kind's closer, or <c>null</c> when lowering found no such enclosing loop.
    /// </summary>
    ExitLoop,
}
