using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;
using RDCore.Runtime.LanguageCore.Errors;

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

            // an error has the help context of its message - one an Error statement, an automation object or a RegExp raises too, whatever one Err had
            // (a Dictionary's 457 is 1000457, recorded in Excel: oracle\s58 H05) - unless Err.Raise gave it the one it was given, or kept the one Err had.
            HelpContext = error is VBApplicationErrorInfo { HelpContext: { } given } ? given : HelpContextOf(error.ErrorId);

            // and the source of the project whose code runs - a run-time error, one an Error statement or an automation object raises, whatever source Err
            // had, in a class as in a standard module (recorded in Excel: oracle\s61 H07-H12) - unless Err.Raise gave it one, or kept the one Err had (H10).
            Source = error is VBApplicationErrorInfo { Source: { Length: > 0 } source } ? source : ProjectName;
        }
    }

    /// <inheritdoc/>
    public string ProjectName { get; set; } = RDCoreProject.DefaultName;

    /// <summary>
    /// The help context ID of an error raised with no help context of its own: MS-VBAL §6.1.3.2.2.2's "implementation-defined VBA Help context ID for
    /// the error", which is MS-VBA's, 1000000 and the code of the message its description is - the error's own, for a VBA run-time error, the
    /// "Application-defined or object-defined error" message's (95) for another code, and the "Automation error" message's (440) for a code that is no
    /// standard error code (recorded in Excel: oracle\s56 G12, s57 H01). An error of RD-VBA's own, from <see cref="VBRuntimeErrorId.NobodyToAnswer"/> on, is
    /// none of MS-VBA's messages: 95 (<c>Err.Raise 50001</c> is 1000095, oracle\s58 H04).
    /// </summary>
    internal static int HelpContextOf(int number)
        => 1000000 + (number is < 0 or > 65535 ? (int)VBRuntimeErrorId.AutomationError
            : number < (int)VBRuntimeErrorId.NobodyToAnswer && Enum.IsDefined(typeof(VBRuntimeErrorId), number) ? number : 95);

    /// <inheritdoc/>
    public bool Clear(bool keepLineNumber = false)
    {
        var had = HasError;

        Current = null;
        Number = 0;
        Description = string.Empty;
        Source = string.Empty;
        HelpFile = string.Empty;
        HelpContext = 0;
        LineNumber = keepLineNumber ? LineNumber : 0;
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

    // the innermost activation is located where the error was raised, at the statement or the expression that faulted; each caller at the call it is
    // suspended at, the call expression or call statement that was running. The GoSubs every activation has not returned from are its own, and are
    // captured now: by the time anyone reads the trace they are popped. So is the module each is in, which a position alone does not say: the position is
    // in the module's source, and the module is the parent of the procedure the activation is of.
    private VBStackTrace Capture(IVBRaisableError error)
        => new(
        [
            .. callStack.Frames.Select((frame, depth)
                => new VBStackTraceFrame(frame.StaticSymbol.Name, depth == 0 ? error.Location : (frame as CallStackFrame)?.CurrentLocation, GoSubsOf(frame))
                {
                    Module = frame is CallStackFrame { Procedure: { } procedure } ? SymbolUri.ModuleNameOf(procedure.ParentUri) : string.Empty,
                }),
        ]);

    private static ImmutableArray<SourceLocation> GoSubsOf(ICallStackFrame frame)
        => frame is CallStackFrame activation ? [.. activation.GoSubLocations().OfType<SourceLocation>()] : [];
}
