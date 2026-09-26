using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Errors.Abstract;

namespace RDCore.SDK.Model.Errors;

/// <summary>
/// Encapsulates the <em>serializable error data</em> for an <em>application error</em>: a run-time error
/// workspace source raised for itself, with an <c>Error</c> statement
/// (<strong>MS-VBAL §5.4.4.3</strong>) or with <c>Err.Raise</c> (<strong>§6.1.3.2.1.2</strong>).
/// </summary>
/// <remarks>
/// 🧩 Errors should be used to generate <em>error diagnostics</em>.
/// <para>
/// MS-VBAL does not distinguish this from an error the runtime semantics report — both are run-time
/// errors, both interrupt the activation, both reach <c>Err</c>, both may be caught. <strong>RD-VBAL
/// §2.6.3</strong> does, in one respect: the diagnostic families differ, so the editor can say whether a
/// workspace raised its own error or tripped over one. The distinction is in <em>who raised it</em> and
/// nothing else — <c>Err.Raise 11</c> is an application error, not a division-by-zero, even though
/// <c>11</c> is a code MS-VBA defines.
/// </para>
/// </remarks>
/// <param name="CustomErrorCode">The custom workspace application error code. <strong>MS-VBAL §6.1.3.2.1.2</strong> reserves 0-512 for system errors and 513-65535 for user-defined ones.</param>
/// <param name="Location">The document location of what raised the error.</param>
/// <param name="Description">The error description. What <c>Err.Description</c> reports.</param>
/// <param name="Verbose">A detailed description of the error.</param>
public record class VBApplicationErrorInfo(int CustomErrorCode, SourceLocation Location, string Description, string Verbose)
    : VBErrorInfo(CustomErrorCode, Location, Description, Verbose), IVBRaisableError
{
    /// <inheritdoc/>
    /// <remarks>
    /// <c>VBA</c> (<strong>RD-VBAL §2.6.3</strong>) — a pseudo-code: the numeric portion is whatever the
    /// workspace supplied, so unlike the other families it names no documented condition.
    /// </remarks>
    public string ToDiagnosticCode() => $"VBA{ErrorId:00000}";

    /// <inheritdoc/>
    public VBErrorInfo AsErrorInfo => this;

    /// <summary>
    /// Creates the error an <c>Error</c> statement or an <c>Err.Raise</c> generates.
    /// </summary>
    /// <remarks>
    /// The description is the raiser's own when it supplied one. Unspecified, <strong>MS-VBAL
    /// §6.1.3.2.1.2</strong> says to use "the String that would be returned by the Error function" for
    /// <paramref name="errorNumber"/>, or "Application-defined or object-defined error" when it
    /// corresponds to no VBA error — which is what <see cref="VBRuntimeErrorInfo.GetErrorString"/> falls
    /// back to.
    /// </remarks>
    /// <param name="errorNumber">The error code the workspace raised.</param>
    /// <param name="location">The document location of the <c>Error</c> statement or <c>Err.Raise</c> invocation.</param>
    /// <param name="verbose">A detailed message that is optionally appended, depending on the current <em>server trace</em> configuration.</param>
    /// <param name="description">The description the raiser supplied, or <c>null</c> to derive one from <paramref name="errorNumber"/>.</param>
    public static VBApplicationErrorInfo Raised(int errorNumber, SourceLocation location, string verbose, string? description = null)
        => new(errorNumber, location, description ?? VBRuntimeErrorInfo.GetErrorString((VBRuntimeErrorId)errorNumber), verbose);
}
