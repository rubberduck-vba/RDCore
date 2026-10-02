using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace RDCore.LanguageServer.Diagnostics;

/// <summary>
/// Pushes diagnostics to the client (<strong>LSP 3.17</strong> <c>textDocument/publishDiagnostics</c>).
/// </summary>
/// <remarks>
/// A diagnostic is produced by the platform, and independently of how it reaches the client: this only carries what was produced. A client that pulls
/// diagnostics (<c>textDocument/diagnostic</c>) is not pushed to as well, for it would show a finding twice, and one that does neither - a shell - has nothing to
/// receive them with.
/// </remarks>
internal interface IDiagnosticsPublisher
{
    /// <summary>
    /// Connects the publisher to the client.
    /// </summary>
    /// <param name="server">The language server, which is what sends to the client.</param>
    /// <param name="enabled">Whether to push: <see langword="false"/> for a client that pulls.</param>
    void Attach(ILanguageServer server, bool enabled);

    /// <summary>
    /// Whether anything is pushed.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Publishes the diagnostics of a document, which replace whatever was published for it before.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="version">The version of its text the diagnostics are of.</param>
    /// <param name="diagnostics">What was found; none clears what was there.</param>
    void Publish(Uri documentUri, int? version, IReadOnlyList<Diagnostic> diagnostics);

    /// <summary>
    /// Clears what was published for a document: nothing is wrong with a document that is not there.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    void Clear(Uri documentUri);
}

internal sealed class DiagnosticsPublisher(ILogger<DiagnosticsPublisher> logger) : IDiagnosticsPublisher
{
    private ILanguageServer? _server;

    public bool IsEnabled { get; private set; }

    public void Attach(ILanguageServer server, bool enabled)
    {
        _server = server;
        IsEnabled = enabled;
        logger.LogInformation("📣 Diagnostics are {mode}.", enabled ? "pushed (textDocument/publishDiagnostics)" : "not pushed: the client pulls them");
    }

    public void Publish(Uri documentUri, int? version, IReadOnlyList<Diagnostic> diagnostics)
    {
        if (!IsEnabled || _server is null)
        {
            return;
        }

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.From(documentUri),
            Version = version,
            Diagnostics = new Container<Diagnostic>(diagnostics),
        });

        logger.LogInformation("📣 {uri} v{version}: {count} diagnostic(s) published.", documentUri, version, diagnostics.Count);
    }

    public void Clear(Uri documentUri) => Publish(documentUri, version: null, []);
}
