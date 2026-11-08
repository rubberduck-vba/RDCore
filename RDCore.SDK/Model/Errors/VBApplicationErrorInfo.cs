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
/// MS-VBAL does not distinguish this from an error the runtime semantics report: both are run-time
/// errors, both interrupt the activation, both reach <c>Err</c>, both may be caught. <strong>RD-VBAL
/// §2.6.3</strong> does, in one respect: the diagnostic families differ, so the editor can say whether a
/// workspace raised its own error or tripped over one. The distinction is in <em>who raised it</em> and
/// nothing else: <c>Err.Raise 11</c> is an application error, not a division-by-zero, even though
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
    /// <c>VBA</c> (<strong>RD-VBAL §2.6.3</strong>): a pseudo-code: the numeric portion is whatever the
    /// workspace supplied, so unlike the other families it names no documented condition.
    /// </remarks>
    public string ToDiagnosticCode() => $"VBA{ErrorId:00000}";

    /// <inheritdoc/>
    public VBErrorInfo AsErrorInfo => this;

    /// <summary>
    /// The help context ID <c>Err.Raise</c> gave the error - the one it was given, or the one <c>Err</c> had and kept (<strong>MS-VBAL §6.1.3.2.1.2</strong>) -
    /// or <see langword="null"/> for the one of its number's message, which an error the runtime raises has, and so does one an <c>Error</c> statement, an
    /// automation object or a <c>RegExp</c> raises.
    /// </summary>
    public int? HelpContext { get; init; }

    /// <summary>
    /// The source <c>Err.Raise</c> gave the error - the one it was given, or the one <c>Err</c> had and kept (<strong>MS-VBAL §6.1.3.2.1.2</strong>) -
    /// or <see langword="null"/> for the name of the project whose code runs (<see cref="Runtime.Abstract.Execution.ISessionErrorState.ProjectName"/>),
    /// which every other error has, one an <c>Error</c> statement raises among them (recorded in Excel: oracle\s61 H07-H10).
    /// </summary>
    public string? Source { get; init; }
}
