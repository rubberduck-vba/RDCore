using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/session/analyze</c>: a client asks the language server to analyze a module
/// it supplies and report what is wrong with it, without running anything.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="ExecuteSessionParams"/> for a client with no editor: an editor gets
/// this through LSP's own <c>textDocument/diagnostic</c> pull against a document it has open, and a
/// shell has neither a document nor a pull — but wants the same answer, from the same providers, for
/// the code in its buffer.
/// </remarks>
[Method(RDCorePlatformProtocol.SessionAnalyze, Direction.ClientToServer)]
public record class AnalyzeSessionParams : IRequest, IRequest<AnalyzeSessionResult>
{
    /// <summary>
    /// The complete source of the module to analyze.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// The module's programmatic name, which is how the analysis addresses it.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;
}

/// <summary>
/// What the platform found in the module.
/// </summary>
public record class AnalyzeSessionResult
{
    /// <summary>
    /// The diagnostics, in source order.
    /// </summary>
    public IReadOnlyList<SessionDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>
    /// How many diagnostics providers answered. <c>0</c> means the platform was assembled without
    /// any, which is different from a module that nothing is wrong with.
    /// </summary>
    public int Providers { get; init; }
}

/// <summary>
/// How bad a <see cref="SessionDiagnostic"/> is. Mirrors LSP's own severities so a client that has an
/// editor and a client that has a prompt rank the same finding the same way.
/// </summary>
public enum SessionDiagnosticSeverity
{
    /// <summary>Something is wrong and the code will not work.</summary>
    Error = 1,

    /// <summary>Something is suspect, but the code will run.</summary>
    Warning = 2,

    /// <summary>Worth knowing.</summary>
    Information = 3,

    /// <summary>A suggestion.</summary>
    Hint = 4,
}

/// <summary>
/// One finding, flattened for a client that renders lines of text rather than editor decorations.
/// </summary>
public record class SessionDiagnostic
{
    /// <summary>
    /// The diagnostic's code (e.g. <c>VBC09319</c>), or an empty string when the provider gave none.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>How bad it is.</summary>
    public SessionDiagnosticSeverity Severity { get; init; } = SessionDiagnosticSeverity.Error;

    /// <summary>What is wrong, in one line.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// The one-based physical line of the analyzed source it was reported against.
    /// </summary>
    /// <remarks>
    /// Physical, not logical: the client supplied the source, so only the client knows what its own
    /// lines mean — a shell whose buffer is numbered maps this back to the line number the user typed.
    /// </remarks>
    public int Line { get; init; }

    /// <summary>The one-based column on <see cref="Line"/>.</summary>
    public int Column { get; init; }
}
