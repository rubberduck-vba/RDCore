using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Runtime.Abstract.Execution;

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

    // nothing can set this: no Declare'd procedure can be invoked yet. It is here because
    // Err.LastDllError has to read something, and 0 is what VBA reports when no DLL call has failed.
    /// <inheritdoc/>
    public int LastDllError => 0;

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

        return had;
    }

    // the faulting statement's location belongs to the innermost activation and only to it: a caller's
    // activation record does not say where in itself it is suspended.
    private VBStackTrace Capture(IVBRaisableError error)
        => new(
        [
            .. callStack.Frames.Select((frame, depth)
                => new VBStackTraceFrame(frame.StaticSymbol.Name, depth == 0 ? error.Location : null)),
        ]);
}
