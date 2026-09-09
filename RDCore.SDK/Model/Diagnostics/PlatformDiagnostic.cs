using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.Diagnostics;

/// <summary>
/// The severity of a <see cref="PlatformDiagnostic"/>.
/// </summary>
/// <remarks>
/// The numeric values match the LSP <c>DiagnosticSeverity</c> scale, so the language server can cast
/// straight across when it maps a platform diagnostic onto an LSP <c>Diagnostic</c>.
/// </remarks>
public enum DiagnosticSeverity
{
    /// <summary>
    /// Reports an error.
    /// </summary>
    Error = 1,

    /// <summary>
    /// Reports a warning.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Reports an informational message.
    /// </summary>
    Information = 3,

    /// <summary>
    /// Reports a hint.
    /// </summary>
    Hint = 4,
}

/// <summary>
/// One diagnostic a provider extension emits for a document, in a transport-agnostic shape.
/// </summary>
/// <remarks>
/// A diagnostics-provider extension returns these over <c>rdcore/diagnostics/document</c>; the
/// language server aggregates them and maps each one onto an LSP <c>Diagnostic</c> when it answers a
/// <c>textDocument/diagnostic</c> pull. Nothing in this model layer depends on the LSP types. A single
/// positional constructor keeps the type round-trippable through <see cref="System.Text.Json"/>.
/// </remarks>
/// <param name="Code">The numeric error id — <see cref="Errors.VBSyntaxErrorInfo.ErrorId"/> for a syntax error.</param>
/// <param name="Source">The emitting provider's name, e.g. <c>"RDCore.Diagnostics"</c>.</param>
/// <param name="Severity">How severe the condition is.</param>
/// <param name="Location">Where in the document the diagnostic applies.</param>
/// <param name="Message">The human-readable message.</param>
/// <param name="Verbose">Optional detail (a faulted token's semantics, a stack); <c>null</c> when there is none.</param>
public record class PlatformDiagnostic(
    int Code,
    string Source,
    DiagnosticSeverity Severity,
    SourceLocation Location,
    string Message,
    string? Verbose);
