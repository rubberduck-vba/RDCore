using OmniSharp.Extensions.LanguageServer.Server;

namespace RDCore.SDK.Server;

/// <summary>
/// How an RDCore server schedules the requests and notifications it receives.
/// </summary>
public static class ServerRequestScheduling
{
    /// <summary>
    /// Has the server answer every request it receives, whatever notifications arrive while the request runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OmniSharp runs a request such as <c>textDocument/diagnostic</c> in parallel, and a notification such as <c>textDocument/didOpen</c> in order. By
    /// default, a notification that arrives while requests run abandons them, and answers them <c>ContentModified</c> (-32801). In OmniSharp 0.19.9 a
    /// request whose handling starts just as that happens is answered nothing at all, and its client waits forever, with the server idle: HexIDE's client
    /// saw it on a quick run of <c>didOpen</c>s, each followed by its diagnostics pull (findings desk #31).
    /// </para>
    /// <para>
    /// With this, a request runs to its end and is answered. What it answers for a document that has moved on in the meantime is its handler's to decide:
    /// the diagnostics pull drops a report of a version that is no longer the document's.
    /// </para>
    /// </remarks>
    /// <param name="options">The server's options.</param>
    /// <returns>The same options.</returns>
    public static LanguageServerOptions WithEveryRequestAnswered(this LanguageServerOptions options)
    {
        options.WithContentModifiedSupport(false);
        return options;
    }
}
