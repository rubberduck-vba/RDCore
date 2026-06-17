using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;

namespace RDCore.SDK.Semantics.Diagnostics;

/// <summary>
/// Encapsulates additional metadata specifically for error diagnostics.
/// </summary>
/// <remarks>
/// Applicable to diagnostics issued from <c>SyntaxErrorInfo</c>, <c>CompileErrorInfo</c>, <c>RuntimeErrorInfo</c>, and <c>ApplicationErrorInfo</c>.
/// </remarks>
/// <param name="ErrorCode">The standardized error code for this error.</param>
/// <param name="Verbose">The verbose trace message associated with this error.</param>
/// <param name="StackTrace">The execution stack trace associated with this error.</param>
public readonly record struct ErrorDiagnosticMetadata(int ErrorCode, string Verbose, object? StackTrace = default) { }

public interface ICoreDiagnosticsFactory
{
    /// <summary>
    /// Creates a new <c>Diagnostic</c> from the provided <c>VBSyntaxErrorInfo</c> representing a <em>semantic (static/CST)</em> syntax error.
    /// </summary>
    /// <param name="info">The CST compile-time error metadata.</param>
    /// <remarks>
    /// MS-VBAL does not differentiate compile-time errors thrown in static <em>token/concrete syntax tree</em> (CST) semantics 
    /// from errors thrown in static <em>abstract syntax tree</em> (AST) semantics.
    /// </remarks>
    /// <returns>A coded <c>VBC00000</c> (Visual Basic Compilation) error-level diagnostic in the range <c>[00001-00999]</c>.</returns>
    Diagnostic FromVBSyntaxError(VBSyntaxErrorInfo info);
    /// <summary>
    /// Creates a new <c>Diagnostic</c> from the provided <c>VBCompileErrorInfo</c> representing a <em>semantic (static/AST)</em> compile-time error.
    /// </summary>
    /// <param name="info">The AST compile-time error metadata</param>
    /// <returns>A coded <c>VBC00000</c> (Visual Basic Compilation) error-level diagnostic in the range <c>[09300-09999]</c>.</returns>
    Diagnostic FromVBCompileError(VBCompileErrorInfo info);
    /// <summary>
    /// Creates a new <c>Diagnostic</c> from the provided <c>VBRuntimeErrorInfo</c> representing a <em>semantic (runtime)</em> run-time error.
    /// </summary>
    /// <param name="info">The run-time error metadata.</param>
    /// <returns>A coded <c>VBR00000</c> (Visual Basic Runtime) error-level diagnostic. The numeric portion of the diagnostic code matches the corresponding MS-VBA run-time error code.</returns>
    Diagnostic FromVBRuntimeError(VBRuntimeErrorInfo info);
    /// <summary>
    /// Creates a new <c>Diagnostic</c> from the provided <c>VBApplicationErrorInfo</c> representing an application-level custom run-time error explicitly raised by <c>Error</c> or <c>Err.Raise</c> staements in the workspace source code.
    /// </summary>
    /// <param name="info">The domain or application run-time error metadata.</param>
    /// <returns>A pseudo-coded <c>VBA00000</c> error-level diagnostic.</returns>
    /// <remarks>
    /// MS-VBAL does not differentiate a custom <em>application error</em> from a <em>semantic run-time error</em>. The numeric portion of the diagnostic code matches the corresponding application-supplied error code.
    /// </remarks>
    Diagnostic FromVBApplicationError(VBApplicationErrorInfo info);
}

public class DiagnosticFactory : ICoreDiagnosticsFactory
{
    public Diagnostic FromVBSyntaxError(VBSyntaxErrorInfo info) => CreateDiagnostic(info);
    public Diagnostic FromVBCompileError(VBCompileErrorInfo info) => CreateDiagnostic(info);
    public Diagnostic FromVBRuntimeError(VBRuntimeErrorInfo info) => CreateDiagnostic(info);
    public Diagnostic FromVBApplicationError(VBApplicationErrorInfo info) => CreateDiagnostic(info);

    private static Uri CreateCodeDescriptionUri(string code) =>
        new($"{RDCoreUrl.RDCoreDiagnosticCodeDescriptionBaseWebUrl}/{code.ToLowerInvariant()}");

    private static CodeDescription CreateCodeDescription(string code) =>
        new() { Href = CreateCodeDescriptionUri(code) };

    private static ErrorDiagnosticMetadata CreateErrorMetadata(int code, string verbose, object? stackTrace = default) =>
        new(code, verbose, stackTrace);

    private Diagnostic CreateDiagnostic(VBSyntaxErrorInfo error)
    {
        var code = error.ToDiagnosticCode();
        return new()
        {
            Code = new DiagnosticCode(code),
            CodeDescription = CreateCodeDescription(code),
            Message = error.Description,
            Severity = DiagnosticSeverity.Error,
            Source = nameof(RDCore),
            Range = error.Location.Range,
            Data = JToken.FromObject(CreateErrorMetadata(error.ErrorId, error.Verbose))
        };
    }

    private Diagnostic CreateDiagnostic(VBCompileErrorInfo error)
    {
        var code = error.ToDiagnosticCode();
        return new()
        {
            Code = new DiagnosticCode(code),
            CodeDescription = CreateCodeDescription(code),
            Message = error.Description,
            Severity = DiagnosticSeverity.Error,
            Source = nameof(RDCore),
            Range = error.Location.Range,
            Data = JToken.FromObject(CreateErrorMetadata(error.ErrorId, error.Verbose))
        };
    }

    private Diagnostic CreateDiagnostic(VBRuntimeErrorInfo error)
    {
        var code = error.ToDiagnosticCode();
        return new()
        {
            Code = new DiagnosticCode(code),
            CodeDescription = CreateCodeDescription(code),
            Message = error.Description,
            Severity = DiagnosticSeverity.Error,
            Source = nameof(RDCore),
            Range = error.Location.Range,
            Data = JToken.FromObject(CreateErrorMetadata(error.ErrorId, error.Verbose))
        };
    }

    private Diagnostic CreateDiagnostic(VBApplicationErrorInfo error)
    {
        var code = error.ToDiagnosticCode();
        return new()
        {
            Code = new DiagnosticCode(code),
            CodeDescription = default,
            Message = error.Description,
            Severity = DiagnosticSeverity.Error,
            Source = nameof(RDCore),
            Range = error.Location.Range,
            Data = JToken.FromObject(CreateErrorMetadata(error.CustomErrorCode, error.Verbose))
        };
    }
}
