using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Errors.Abstract;

namespace RDCore.SDK.Model.Errors;

/// <summary>
/// Encapsulates the <em>serializable error data</em> for a <em>runtime error</em>.
/// </summary>
/// <remarks>
/// A <em>runtime error</em> occurs while traversing the <em>abstract syntax tree</em> (AST) in the RDCore runtime.
/// </remarks>
/// <param name="ErrorId">The number of the error: <c>Err.Number</c>, and the number of its <c>VBR</c> diagnostic code.</param>
/// <param name="Location">The document location of the problematic <em>node</em>.</param>
/// <param name="Description">What the error is: <c>Err.Description</c>.</param>
/// <param name="Verbose">A detailed message that is optionally appended, depending on the current <em>server trace</em> configuration.</param>
public record class VBRuntimeErrorInfo(int ErrorId, SourceLocation Location, string Description, string Verbose)
    : VBErrorInfo(ErrorId, Location, Description, Verbose), IVBRaisableError
{
    /// <inheritdoc/>
    /// <remarks>
    /// <c>VBR</c> (<strong>RD-VBAL 2.6.3</strong>): the numeric portion matches the corresponding MS-VBA
    /// run-time error code.
    /// </remarks>
    public string ToDiagnosticCode() => $"VBR{ErrorId:00000}";

    /// <summary>
    /// Whether the error is raised whatever values the operation is given: decided by their types alone - a <c>Byte()</c> let-coerced to a <c>Long</c> is
    /// a type mismatch, whatever bytes it holds (<strong>MS-VBAL 5.5.1.2.6</strong>).
    /// </summary>
    /// <remarks>
    /// An analysis evaluates code with values it assumes, and states none of the errors those raise: what an assumed value raises is not known to happen.
    /// An error decided by the types is, which is why it is stated all the same - the evaluation of an operation on a value that is not known keeps it, and
    /// an instruction whose evaluation assumed something reports it.
    /// </remarks>
    public bool IsDecidedByTypes { get; init; }

    /// <inheritdoc/>
    public VBErrorInfo AsErrorInfo => this;
}
