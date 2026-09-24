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
    /// <c>GoSub</c> (<strong>MS-VBAL §5.4.2.14</strong>): pushes the offset right after this instruction
    /// onto the activation's own GoSub Resumption List, then branches unconditionally to
    /// <see cref="Instruction.Target"/> — everything <see cref="Jump"/> does, plus the push.
    /// </summary>
    GoSub,

    /// <summary>
    /// <c>Return</c> (<strong>MS-VBAL §5.4.2.15</strong>): pops the activation's own GoSub Resumption
    /// List and branches to the popped offset. An empty list is error 3, "Return without GoSub".
    /// </summary>
    Return,

    /// <summary>
    /// An indexed branch that also pushes a resumption point (<c>On…GoSub</c>, <strong>MS-VBAL
    /// §5.4.2.16</strong>) to one of <see cref="Instruction.Targets"/>, selected at runtime — everything
    /// <see cref="JumpTable"/> does, plus the same push <see cref="GoSub"/> does on a successful branch;
    /// an out-of-range selector falls through without pushing anything.
    /// </summary>
    GoSubTable,

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

    /// <summary>
    /// <c>On Error GoTo</c> &lt;label&gt; (<strong>MS-VBAL §5.4.4.1</strong>): sets the activation's
    /// error-handling policy to branch to <see cref="Instruction.Target"/> on the next error. Always
    /// falls through — the branch only happens later, when (if) an error is actually raised.
    /// </summary>
    OnErrorGoTo,

    /// <summary>
    /// <c>On Error GoTo 0</c> (or the undocumented VBA6/7 <c>On Error GoTo -1</c>) — disables the
    /// activation's error-handling policy and clears any active error. Always falls through.
    /// </summary>
    OnErrorDisable,

    /// <summary>
    /// <c>On Error Resume Next</c> (<strong>MS-VBAL §5.4.4.1</strong>): sets the activation's
    /// error-handling policy to silently continue at the statement after any statement that raises an
    /// error. Always falls through.
    /// </summary>
    OnErrorResumeNext,

    /// <summary>
    /// A bare <c>Resume</c>, or <c>Resume 0</c> (<strong>MS-VBAL §5.4.4.2</strong>): re-executes the
    /// statement whose fault raised the activation's active error, and clears it. No active error is
    /// error 20, "Resume without error".
    /// </summary>
    ResumeCurrentStatement,

    /// <summary>
    /// <c>Resume Next</c> (<strong>MS-VBAL §5.4.4.2</strong>): continues at the statement right after
    /// the one whose fault raised the activation's active error, and clears it. No active error is error
    /// 20, "Resume without error".
    /// </summary>
    ResumeNext,

    /// <summary>
    /// <c>Resume</c> &lt;label&gt; (<strong>MS-VBAL §5.4.4.2</strong>): branches to
    /// <see cref="Instruction.Target"/> and clears the activation's active error. No active error is
    /// error 20, "Resume without error".
    /// </summary>
    ResumeLabel,

    /// <summary>
    /// <c>Error</c> &lt;number&gt; (<strong>MS-VBAL §5.4.4.3</strong>): raises the given number as a
    /// run-time error, "as if the <c>Err.Raise</c> method were invoked" with it.
    /// </summary>
    RaiseError,
}
