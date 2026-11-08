namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// How a run ended.
/// </summary>
public enum ExecutionOutcome
{
    /// <summary>The entry point ran to completion.</summary>
    Completed,

    /// <summary>The source did not parse; the result's diagnostics say why.</summary>
    SyntaxError,

    /// <summary>There is no such procedure to invoke, or no runtime session to invoke it in.</summary>
    NotFound,

    /// <summary>A run-time error was raised and nothing handled it.</summary>
    RuntimeError,

    /// <summary>An <c>End</c> statement stopped the program.</summary>
    Halted,

    /// <summary>A <c>Stop</c> statement, or the caller cancelling the request, interrupted the program.</summary>
    Interrupted,

    /// <summary>
    /// The interpreter reached something it has no implementation for. Not a program error: a gap.
    /// </summary>
    NotImplemented,

    /// <summary>
    /// The program was run under a debugger and waits where it stopped: the result's error line and character say where. It is neither over nor failed.
    /// </summary>
    Suspended,

    /// <summary>
    /// The request cannot be answered now: a program is running, or is suspended and the request would wreck it.
    /// </summary>
    Refused,
}
