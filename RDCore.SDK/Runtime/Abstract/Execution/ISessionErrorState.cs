using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Errors;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The error state of a session — one per session, which is what makes an <c>Err</c> object possible.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §6.1.3.2</strong>'s <c>Err</c> is "a singleton that is referenced by the <c>Err</c>
/// global/static symbol": one object, for the whole session, whose <c>Number</c>, <c>Description</c> and
/// <c>Source</c> describe the most recent run-time error. Until now the only record of an error was
/// <see cref="Shared.ErrorHandlerState.ActiveError"/>, which belongs to one <em>activation</em> and dies
/// with its frame — so there was nowhere for such an object to read from, and nothing that outlived a
/// procedure call could report what had gone wrong in it.
/// <para>
/// The two are not redundant. An activation's <c>ActiveError</c> answers "may this activation
/// <c>Resume</c>, and where" — a question about control flow in one frame. This answers "what went
/// wrong in this session", which is what source code asks when it reads <c>Err.Number</c>, and which
/// outlives the frame that raised it.
/// </para>
/// <para>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </remarks>
public interface ISessionErrorState
{
    /// <summary>
    /// The most recent run-time error, or <c>null</c> when there is none — the state <c>Err.Number</c>
    /// reports as <c>0</c>.
    /// </summary>
    IVBRaisableError? Current { get; }

    /// <summary>
    /// Whether an error is current. <c>Err.Number &lt;&gt; 0</c>, in source terms — and exactly that, so
    /// source assigning <see cref="Number"/> makes an error current the same way raising one does.
    /// </summary>
    bool HasError { get; }

    /// <summary>
    /// <strong>MS-VBAL §6.1.3.2.2.5</strong> <c>Err.Number</c>: the code of the current error, <c>0</c>
    /// when there is none.
    /// </summary>
    int Number { get; set; }

    /// <summary>
    /// <strong>MS-VBAL §6.1.3.2.2.1</strong> <c>Err.Description</c>: what went wrong, in words.
    /// </summary>
    /// <remarks>
    /// A raise sets it to the raised error's own description. Source sets it both before an
    /// <c>Err.Raise</c> that omits one, and from inside a handler.
    /// </remarks>
    string Description { get; set; }

    /// <summary>
    /// <strong>MS-VBAL §6.1.3.2.2.6</strong> <c>Err.Source</c>: the object or application that
    /// originally generated the error.
    /// </summary>
    string Source { get; set; }

    /// <summary>
    /// <strong>MS-VBAL §6.1.3.2.2.3</strong> <c>Err.HelpFile</c>.
    /// </summary>
    /// <remarks>
    /// ℹ️ Carried so that source can round-trip what it sets. The help system it names is a legacy
    /// proprietary one, and is out of scope of this implementation.
    /// </remarks>
    string HelpFile { get; set; }

    /// <summary>
    /// <strong>MS-VBAL §6.1.3.2.2.2</strong> <c>Err.HelpContext</c>.
    /// </summary>
    /// <remarks>
    /// ℹ️ Carried so that source can round-trip what it sets. The help system it indexes is a legacy
    /// proprietary one, and is out of scope of this implementation.
    /// </remarks>
    int HelpContext { get; set; }

    /// <summary>
    /// <strong>MS-VBAL §6.1.3.2.2.4</strong> <c>Err.LastDllError</c>: the system error code of the last
    /// call into a dynamic-link library. Read-only, and <c>0</c> for as long as a <c>Declare</c>d
    /// procedure cannot be executed at all.
    /// </summary>
    int LastDllError { get; }

    /// <summary>
    /// 🎯 <strong>RD-VBAL</strong> The <em>line number</em> the current error was raised at - the nearest
    /// line-number label at or before the faulting statement, or <c>0</c> when none precedes it. What
    /// <c>Erl</c> reports.
    /// </summary>
    /// <remarks>
    /// 🎯 <c>long</c> rather than MS-VBA's own resolution, deliberately: MS-VBA reports <c>Erl</c> as a
    /// <c>ushort</c> and wraps around on anything that does not fit, so a program numbered past 65535 is told
    /// it faulted somewhere it did not. RD-VBA widens it so that every legal line number label is
    /// representable. A <em>named</em> label never sets it; only a label spelled as decimal digits does.
    /// </remarks>
    long LineNumber { get; }

    /// <summary>
    /// 🎯 The call stack the current error was raised on, captured at the raise.
    /// <see cref="VBStackTrace.Empty"/> when no error is current, or when source made one current by
    /// assigning <see cref="Number"/> rather than by raising it.
    /// </summary>
    /// <remarks>
    /// RDCore's own, not MS-VBAL's — VBA can say what an error was but never where it came from. It has
    /// to be captured rather than derived on demand: by the time a handler reads it, the activations it
    /// describes have been unwound.
    /// </remarks>
    VBStackTrace StackTrace { get; }

    /// <summary>
    /// Records <paramref name="error"/> as the session's current error, replacing any earlier one, and
    /// captures the call stack it was raised on.
    /// </summary>
    /// <remarks>
    /// Called for every run-time error the interpreter raises, whether or not anything goes on to
    /// handle it: <c>Err</c> is set by the error, not by the handling of it. The error's own number and
    /// description become <see cref="Number"/> and <see cref="Description"/>, overwriting whatever
    /// source had set them to — a new error is a new error.
    /// </remarks>
    /// <param name="error">The error that was raised.</param>
    /// <param name="lineNumber">
    /// The <see cref="LineNumber"/> to report for it: the line number in effect where it was raised, or
    /// <c>0</c> when no line-number label precedes that statement.
    /// </param>
    void Raise(IVBRaisableError error, long lineNumber = 0);

    /// <summary>
    /// Clears the current error.
    /// </summary>
    /// <remarks>
    /// <strong>MS-VBAL §6.1.3.2.1</strong> lists what does this besides <c>Err.Clear</c> itself: a
    /// <c>Resume</c> statement, <c>Exit Sub</c>/<c>Exit Function</c>/<c>Exit Property</c>, and an
    /// <c>On Error</c> statement.
    /// </remarks>
    /// <returns><c>true</c> if there was an error to clear.</returns>
    bool Clear();
}
