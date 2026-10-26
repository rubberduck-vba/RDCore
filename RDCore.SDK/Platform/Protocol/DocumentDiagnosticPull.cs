using MediatR;
using Newtonsoft.Json;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// <c>textDocument/diagnostic</c> (<strong>LSP 3.17</strong> §Document Diagnostics), as a client of the platform sends it: the request an editor pulls the
/// diagnostics of a document with, whose report is read as a <see cref="DocumentDiagnosticPullReport"/>.
/// </summary>
/// <remarks>
/// OmniSharp 0.19.9 writes a <see cref="RelatedDocumentDiagnosticReport"/> and does not read one: its converter throws <see cref="NotImplementedException"/>, so a
/// client that sends <see cref="DocumentDiagnosticParams"/> gets an exception for an answer. This is the same request, whose answer is read as the record it is.
/// </remarks>
[Method("textDocument/diagnostic", Direction.ClientToServer)]
public sealed record class DocumentDiagnosticPullParams : IRequest, IRequest<DocumentDiagnosticPullReport?>
{
    /// <summary>The document whose diagnostics are pulled.</summary>
    [JsonProperty("textDocument")]
    public TextDocumentIdentifier TextDocument { get; init; } = null!;

    /// <summary>The identifier of the last report the client has, which the server answers is unchanged when it is; <see langword="null"/> for none.</summary>
    [JsonProperty("previousResultId", NullValueHandling = NullValueHandling.Ignore)]
    public string? PreviousResultId { get; init; }
}

/// <summary>
/// The report a <see cref="DocumentDiagnosticPullParams"/> is answered with: a full one, with the diagnostics, or an unchanged one, without.
/// </summary>
public sealed record class DocumentDiagnosticPullReport
{
    /// <summary><c>full</c>, or <c>unchanged</c> when the client has the diagnostics of the report it named already.</summary>
    [JsonProperty("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>The identifier of the report, which the client names when it pulls again.</summary>
    [JsonProperty("resultId")]
    public string? ResultId { get; init; }

    /// <summary>The diagnostics of a full report; none for an unchanged one.</summary>
    [JsonProperty("items")]
    public Diagnostic[] Items { get; init; } = [];
}
