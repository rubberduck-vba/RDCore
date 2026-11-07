using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Errors;

/// <summary>
/// One activation of the call stack a run-time error was raised on.
/// </summary>
/// <param name="ProcedureName">The name of the procedure the activation is of.</param>
/// <param name="Location">
/// Where in that procedure execution stood, when it is known. Known for the activation the error was
/// raised in (that is the faulting statement), and not for its callers, whose activation records do
/// not carry the location of the call they are suspended at.
/// </param>
/// <param name="GoSubs">
/// Where the <c>GoSub</c> statements the activation had not <c>Return</c>ed from were, innermost first: how it got to where it was, within the procedure. It is the
/// activation's GoSub Resumption List (<strong>MS-VBAL §5.4.2.14</strong>) when the error was raised, and is empty for an activation in no <c>GoSub</c>.
/// </param>
public readonly record struct VBStackTraceFrame(string ProcedureName, SourceLocation? Location, ImmutableArray<SourceLocation> GoSubs = default)
{
    /// <summary>
    /// The frame as one line: the procedure's name, and the 1-based line and column of the faulting
    /// statement when that is known, then the <c>GoSub</c> statements it came from.
    /// </summary>
    public override string ToString()
    {
        var frame = Location is { Uri: not null } location
            ? $"{ProcedureName} ({location.Range.Start.Line + 1}:{location.Range.Start.Character + 1})"
            : ProcedureName;
        return GoSubs.IsDefaultOrEmpty
            ? frame
            : frame + string.Concat(GoSubs.Select(gosub => $" from ({gosub.Range.Start.Line + 1}:{gosub.Range.Start.Character + 1})"));
    }
}

/// <summary>
/// The call stack a run-time error was raised on, innermost activation first.
/// </summary>
/// <remarks>
/// 🎯 <strong>RDCore</strong>'s own, not MS-VBAL's: VBA has never been able to say where an error came
/// from, only what it was, which is why <c>Err.Description</c> in a deep call chain tells you so little.
/// <c>ErrObject</c> carries this alongside the error's own properties, captured at the moment the error
/// is raised (the one point every run-time error passes through) rather than reconstructed afterwards,
/// by which time the frames are gone.
/// </remarks>
/// <param name="Frames">The activations, innermost (where the error was raised) first.</param>
public sealed record class VBStackTrace(ImmutableArray<VBStackTraceFrame> Frames)
{
    /// <summary>
    /// No stack trace: what a session with no current error has.
    /// </summary>
    public static VBStackTrace Empty { get; } = new([]);

    /// <summary>
    /// The trace as VBA source reads it through <c>Err.StackTrace</c>: one indented line per activation,
    /// innermost first, separated by <c>vbCrLf</c>.
    /// </summary>
    /// <remarks>
    /// 👉 <c>vbCrLf</c> rather than the host's own line separator: a VBA <c>String</c>'s line break is
    /// CrLf whatever platform the environment host runs on.
    /// </remarks>
    public override string ToString() => string.Join("\r\n", Frames.Select(frame => $"  at {frame}"));
}
