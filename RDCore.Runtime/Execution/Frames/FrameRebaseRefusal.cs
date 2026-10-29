namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// Why an activation can't go on in the edited body of its procedure from where it is (<see cref="CallStackFrame.WhyNotRebased"/>).
/// </summary>
public enum FrameRebaseRefusal
{
    /// <summary>The call the activation is in the middle of was edited or removed: it has nowhere to return to.</summary>
    CallChanged,

    /// <summary>
    /// The call the activation is in the middle of is made by a condition, a <c>Select Case</c>, or the header of a loop or a <c>With</c>, which goes on by
    /// offsets of the body it began in.
    /// </summary>
    InTheMiddleOfAHeader,

    /// <summary>The opening of a block the activation is in, or a <c>GoSub</c> it is to return from, was edited or removed.</summary>
    BlockChanged,

    /// <summary>The handler of the activation's <c>On Error GoTo</c>, or the statement its error was raised at, was edited or removed.</summary>
    HandlerChanged,

    /// <summary>The statement the activation waits at an error of was edited or removed: there is no statement left for the error to be raised at.</summary>
    FaultChanged,

    /// <summary>
    /// What the activation goes on from can't be told for certain: the edit can be read in more than one way there (<see cref="RDCore.SDK.Semantics.Instructions.InstructionAlignment.IsCertain"/>),
    /// as when one of two identical statements was removed.
    /// </summary>
    Uncertain,
}
