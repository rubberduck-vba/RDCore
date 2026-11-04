using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

namespace RDCore.Runtime.Execution;

/// <inheritdoc cref="ISessionErrorState"/>
internal sealed class SessionErrorState(ICallStack callStack) : ISessionErrorState
{
    /// <inheritdoc/>
    public IVBRaisableError? Current { get; private set; }

    /// <inheritdoc/>
    public bool HasError => Number != 0;

    /// <inheritdoc/>
    public int Number { get; set; }

    /// <inheritdoc/>
    public string Description { get; set; } = string.Empty;

    /// <inheritdoc/>
    public string Source { get; set; } = string.Empty;

    /// <inheritdoc/>
    public string HelpFile { get; set; } = string.Empty;

    /// <inheritdoc/>
    public int HelpContext { get; set; }

    /// <inheritdoc/>
    public int LastDllError { get; private set; }

    /// <inheritdoc/>
    public void RecordDllError(int code) => LastDllError = code;

    /// <inheritdoc/>
    public VBStackTrace StackTrace { get; private set; } = VBStackTrace.Empty;

    /// <inheritdoc/>
    public long LineNumber { get; private set; }

    /// <inheritdoc/>
    public void Raise(IVBRaisableError error, long lineNumber = 0)
    {
        // the error a callee raised, reaching each caller it unwinds into: it is the one Err is already of - its line is the caller's now (Erl), and its
        // stack trace is the one captured where it was raised, all of it. Capturing the trace again at each caller would keep only the callers' part of it,
        // with the callee's location on the caller's frame, and cost as many frames again at each - quadratic in the depth of the stack it unwinds.
        var unwinding = ReferenceEquals(Current, error);
        Current = error;
        Number = error.ErrorId;
        Description = error.Description;
        LineNumber = lineNumber;
        if (!unwinding)
        {
            StackTrace = Capture(error);

            // an error the runtime raises has the help context of its message; Err.Raise has set the one it was given, or kept the one Err has.
            if (error is not VBApplicationErrorInfo)
            {
                HelpContext = HelpContextOf(error.ErrorId);
            }
        }
    }

    /// <summary>
    /// The help context ID of an error raised with no help context of its own: MS-VBAL §6.1.3.2.2.2's "implementation-defined VBA Help context ID for
    /// the error", which is MS-VBA's, 1000000 and the code of the message its description is - the error's own, for a VBA run-time error, the
    /// "Application-defined or object-defined error" message's (95) for another code, and the "Automation error" message's (440) for a code that is no
    /// standard error code (recorded in Excel: oracle\s56 G12, s57 H01).
    /// </summary>
    internal static int HelpContextOf(int number)
        => 1000000 + (number is < 0 or > 65535 ? (int)VBRuntimeErrorId.AutomationError : Enum.IsDefined(typeof(VBRuntimeErrorId), number) ? number : 95);

    /// <inheritdoc/>
    public bool Clear()
    {
        var had = HasError;

        Current = null;
        Number = 0;
        Description = string.Empty;
        Source = string.Empty;
        HelpFile = string.Empty;
        HelpContext = 0;
        LineNumber = 0;
        StackTrace = VBStackTrace.Empty;

        // MS-VBAL §6.1.3.2.1.1: "clears all property settings of the Err object", the one a DLL call sets among them.
        LastDllError = 0;

        return had;
    }

    /// <inheritdoc/>
    public IVBRaisableError? Untrappable { get; private set; }

    /// <inheritdoc/>
    public void RaiseUntrappable(IVBRaisableError error) => Untrappable ??= error;

    /// <inheritdoc/>
    public void ClearUntrappable() => Untrappable = null;

    // the faulting statement's location belongs to the innermost activation and only to it: a caller's
    // activation record does not say where in itself it is suspended. The GoSubs every activation has not
    // returned from are its own, and are captured now: by the time anyone reads the trace they are popped.
    private VBStackTrace Capture(IVBRaisableError error)
        => new(
        [
            .. callStack.Frames.Select((frame, depth)
                => new VBStackTraceFrame(frame.StaticSymbol.Name, depth == 0 ? error.Location : null, GoSubsOf(frame))),
        ]);

    private static ImmutableArray<SourceLocation> GoSubsOf(ICallStackFrame frame)
        => frame is CallStackFrame activation ? [.. activation.GoSubLocations().OfType<SourceLocation>()] : [];
}
