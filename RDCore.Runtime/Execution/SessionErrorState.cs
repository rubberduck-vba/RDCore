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
        Current = error;
        Number = error.ErrorId;
        Description = error.Description;
        LineNumber = lineNumber;
        StackTrace = Capture(error);
    }

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
