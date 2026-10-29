using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Server.ProtocolExtensions;

/// <summary>
/// A <see cref="WorkspaceEdit"/> as a client can apply it (<strong>LSP 3.17</strong> §WorkspaceEdit): its <c>documentChanges</c> to a client that said it
/// takes them (<c>workspace.workspaceEdit.documentChanges</c>), and its <c>changes</c> to any other.
/// </summary>
/// <remarks>
/// A client that did not say it takes <c>documentChanges</c> reads <c>changes</c> alone, and applies none of an edit that has none: what a server sends it
/// is the edits of each document by its URI. Those cannot say which version of a document they are edits of, which the spec allows; such a client applies
/// them to the text it has.
/// </remarks>
public static class WorkspaceEditExtensions
{
    /// <summary>
    /// Whether the client said it takes the <c>documentChanges</c> of a <see cref="WorkspaceEdit"/>.
    /// </summary>
    /// <param name="client">What the client said it can do, as it initialized; none when it is not known.</param>
    public static bool TakesDocumentChanges(this ClientCapabilities? client)
        => client?.Workspace?.WorkspaceEdit is { IsSupported: true, Value.DocumentChanges: true };

    /// <summary>
    /// The edit as the client can apply it: as it is, to a client that takes <c>documentChanges</c>; to any other, the text edits of its
    /// <c>documentChanges</c> as <c>changes</c>, by document, in the order they were written, with what was in <c>changes</c> already.
    /// </summary>
    /// <param name="edit">The edit, as the server made it.</param>
    /// <param name="client">What the client said it can do, as it initialized; none when it is not known, which is a client that does not take
    /// <c>documentChanges</c>.</param>
    /// <returns>The edit to send. An edit that creates, renames or deletes a file is sent as it is: <c>changes</c> cannot say it, and no client that does not
    /// take <c>documentChanges</c> can apply it.</returns>
    public static WorkspaceEdit For(this WorkspaceEdit edit, ClientCapabilities? client)
    {
        if (client.TakesDocumentChanges() || edit.DocumentChanges is not { } documentChanges || documentChanges.Any(change => !change.IsTextDocumentEdit))
        {
            return edit;
        }

        var changes = new Dictionary<DocumentUri, List<TextEdit>>();
        foreach (var (uri, edits) in edit.Changes ?? new Dictionary<DocumentUri, IEnumerable<TextEdit>>())
        {
            EditsOf(changes, uri).AddRange(edits);
        }

        foreach (var document in documentChanges.Select(change => change.TextDocumentEdit!))
        {
            EditsOf(changes, document.TextDocument.Uri).AddRange(document.Edits);
        }

        return edit with
        {
            DocumentChanges = null,
            Changes = changes.ToDictionary(entry => entry.Key, entry => (IEnumerable<TextEdit>)entry.Value),
        };
    }

    private static List<TextEdit> EditsOf(Dictionary<DocumentUri, List<TextEdit>> changes, DocumentUri uri)
    {
        if (!changes.TryGetValue(uri, out var edits))
        {
            changes[uri] = edits = [];
        }

        return edits;
    }
}
