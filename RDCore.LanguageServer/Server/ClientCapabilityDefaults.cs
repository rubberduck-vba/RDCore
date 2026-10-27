using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;

namespace RDCore.LanguageServer.Server;

/// <summary>
/// The capabilities of a client, completed with those the server serves whether or not the client says it supports them.
/// </summary>
/// <remarks>
/// <para>
/// What the client said of each capability is kept: the handlers read it - whether a client reads links, which formats its hovers are in, whether it is
/// asked to pull its diagnostics again. A capability it did not send is given a value of its own, never a <see langword="null"/> one. A handler reads the
/// capability it is registered with as the server answers <c>initialize</c>, and a value-less <c>new(true)</c> made it throw there, which left
/// <c>initialize</c> unanswered for a client that sends few capabilities.
/// </para>
/// <para>
/// A handler whose capability is none of these, and that the client did not send, is still given none, and reads it as having none.
/// </para>
/// </remarks>
internal static class ClientCapabilityDefaults
{
    /// <summary>
    /// Completes the capabilities of a client, in place.
    /// </summary>
    /// <param name="capabilities">What the client said it supports.</param>
    public static void Complete(ClientCapabilities capabilities)
    {
        var textDocument = capabilities.TextDocument ??= new TextDocumentClientCapabilities();
        textDocument.Declaration = Kept(textDocument.Declaration);
        textDocument.Definition = Kept(textDocument.Definition);
        textDocument.Diagnostic = Kept(textDocument.Diagnostic);
        textDocument.DocumentSymbol = Kept(textDocument.DocumentSymbol);
        textDocument.FoldingRange = Kept(textDocument.FoldingRange);
        textDocument.SemanticTokens = Kept(textDocument.SemanticTokens);
        textDocument.Synchronization = Kept(textDocument.Synchronization);
        textDocument.PublishDiagnostics = Kept(textDocument.PublishDiagnostics);

        var window = capabilities.Window ??= new WindowClientCapabilities();
        window.ShowMessage = KeptOrNew(window.ShowMessage);
        window.WorkDoneProgress = window.WorkDoneProgress.IsSupported ? window.WorkDoneProgress : new Supports<bool>(true);

        var workspace = capabilities.Workspace ??= new WorkspaceClientCapabilities();
        workspace.Diagnostics = KeptOrNew(workspace.Diagnostics);
        workspace.Symbol = Kept(workspace.Symbol);
    }

    // the client's own, when it said it supports the capability; one of the server's otherwise.
    private static Supports<T?> Kept<T>(Supports<T?> client) where T : class, new()
        => client is { IsSupported: true, Value: not null } ? client : new Supports<T?>(true, new T());

    // the same, for a capability whose value the protocol types as never null - which a client that does not send it leaves null all the same.
    private static Supports<T> KeptOrNew<T>(Supports<T> client) where T : class, new()
        => client is { IsSupported: true, Value: not null } ? client : new Supports<T>(true, new T());
}
