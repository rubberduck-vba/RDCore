using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.Errors.Abstract;

/// <summary>
/// An error that can be <em>raised at run time</em>: one the runtime semantics layer reports, or one
/// workspace source raises for itself with <c>Error</c> or <c>Err.Raise</c>.
/// </summary>
/// <remarks>
/// The two are one thing to the interpreter — both interrupt the current activation, both reach the
/// session's <c>Err</c> object, and both may be caught by an <c>On Error</c> handler — and two things to
/// the editor, because <strong>RD-VBAL §2.6.3</strong> gives them different diagnostic code families:
/// <c>VBR</c> for what the runtime semantics report, <c>VBA</c> for what the workspace raised. This is
/// what the interpreter's own error channel carries, so that the family travels with the error rather
/// than being decided by whatever static type a carrier happened to be declared as.
/// <para>
/// It deliberately does not admit a compile-time or syntax error. Those are <see cref="VBErrorInfo"/>s
/// too, but nothing raises one at run time, and a carrier that accepted one would be describing a state
/// the interpreter has no handling for.
/// </para>
/// </remarks>
public interface IVBRaisableError
{
    /// <summary>
    /// The numeric error code — <c>Err.Number</c>, in source terms.
    /// </summary>
    int ErrorId { get; }

    /// <summary>
    /// The document location of whatever raised the error.
    /// </summary>
    SourceLocation Location { get; }

    /// <summary>
    /// What went wrong, in words — <c>Err.Description</c>.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// A detailed message identifying the faulted node and detailing its semantics.
    /// </summary>
    string Verbose { get; }

    /// <summary>
    /// The <strong>RD-VBAL §2.6.3</strong> diagnostic code of this error, prefix and all.
    /// </summary>
    /// <remarks>
    /// 👉 A member rather than an extension, deliberately. An extension binds to the <em>static</em> type
    /// of what it is called on, so an application error held in a variable declared as anything more
    /// general would silently report the wrong family — which is exactly how an error travels through the
    /// interpreter.
    /// </remarks>
    string ToDiagnosticCode();

    /// <summary>
    /// This error as the diagnostic source metadata it is.
    /// </summary>
    /// <remarks>
    /// Every raisable error <em>is</em> a <see cref="VBErrorInfo"/> — that is the constraint C# has no way
    /// to state on an interface, so it is stated here instead, and implemented as <c>this</c>. It is what
    /// lets a carrier hold the interface, keeping the diagnostic family with the error, and still hand the
    /// error to the diagnostics pipeline, which is generic over the record rather than over this.
    /// </remarks>
    VBErrorInfo AsErrorInfo { get; }
}
