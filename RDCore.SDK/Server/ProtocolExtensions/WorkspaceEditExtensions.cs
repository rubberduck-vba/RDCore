using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Server.ProtocolExtensions;

/// <summary>
/// A <see cref="WorkspaceEdit"/> as a client can apply it (<strong>LSP 3.17</strong> §WorkspaceEdit): its <c>documentChanges</c> to a client that said it
/// takes them (<c>workspace.workspaceEdit.documentChanges</c>), and its <c>changes</c> to any other; its change annotations to a client that said it takes
/// those (<c>changeAnnotationSupport</c>).
/// </summary>
/// <remarks>
/// <para>
/// A client that did not say it takes <c>documentChanges</c> reads <c>changes</c> alone, and applies none of an edit that has none. <c>changes</c> are the
/// edits of each document by its URI: they cannot say which version of a document they are edits of, which the spec allows - such a client applies them to
/// the text it has - nor that a file is created, renamed or deleted.
/// </para>
/// <para>
/// An edit that has both is giving its <c>changes</c> as the alternative of its <c>documentChanges</c>, for a client that does not take them: they are the
/// same edits, and one client is sent one of them.
/// </para>
/// </remarks>
public static class WorkspaceEditExtensions
{
    /// <summary>
    /// Whether the client said it takes the <c>documentChanges</c> of a <see cref="WorkspaceEdit"/>.
    /// </summary>
    /// <param name="client">What the client said it can do, as it initialized; none when it is not known.</param>
    /// <returns><see langword="true"/> when it said <c>workspace.workspaceEdit.documentChanges</c>; <see langword="false"/> when it did not, or is not known.</returns>
    public static bool TakesDocumentChanges(this ClientCapabilities? client)
        => client?.Workspace?.WorkspaceEdit is { IsSupported: true, Value.DocumentChanges: true };

    /// <summary>
    /// Whether the client said it takes the change annotations of a <see cref="WorkspaceEdit"/>: an edit it asks its user to confirm, a label it shows.
    /// </summary>
    /// <param name="client">What the client said it can do, as it initialized; none when it is not known.</param>
    /// <returns><see langword="true"/> when it said <c>workspace.workspaceEdit.changeAnnotationSupport</c>; <see langword="false"/> when it did not, or is not known.</returns>
    public static bool TakesChangeAnnotations(this ClientCapabilities? client)
        => client?.Workspace?.WorkspaceEdit is { IsSupported: true, Value.ChangeAnnotationSupport: not null };

    /// <summary>
    /// The edit as the client can apply it.
    /// </summary>
    /// <param name="edit">The edit, as the server made it.</param>
    /// <param name="client">What the client said it can do, as it initialized; none when it is not known, which is a client that takes neither
    /// <c>documentChanges</c> nor change annotations.</param>
    /// <returns>
    /// <para>
    /// To a client that takes <c>documentChanges</c>: the edit, when it says nothing the client did not say it takes. To any other: its <c>changes</c>
    /// alone, when it has them; otherwise the text edits of its <c>documentChanges</c> as <c>changes</c>, by document, in the order they were written.
    /// </para>
    /// <para>
    /// To a client that does not take change annotations - and in <c>changes</c>, which cannot carry them - an edit whose annotation asks the user to
    /// confirm it is left out, being one the user would only have applied if they chose to; the others are sent without their annotation.
    /// </para>
    /// </returns>
    /// <exception cref="WorkspaceEditNotSupportedException">The edit creates, renames or deletes a file, and the client did not say it does that
    /// (<c>documentChanges</c> and <c>resourceOperations</c>); or, to a client that does not take <c>documentChanges</c>, it edits one document in more than
    /// one <c>TextDocumentEdit</c> - the second's ranges are of the text the first made, which <c>changes</c> cannot say.</exception>
    public static WorkspaceEdit For(this WorkspaceEdit edit, ClientCapabilities? client)
    {
        var annotations = client.TakesChangeAnnotations() ? null : NeedingConfirmation(edit);
        if (client.TakesDocumentChanges())
        {
            if (edit.DocumentChanges is { } changes && changes.Any(change => !change.IsTextDocumentEdit && !TakesResourceOperation(client, change)))
            {
                throw new WorkspaceEditNotSupportedException(Exceptions.WorkspaceEdit_FileOperationNotSupported);
            }

            return annotations is null || !IsAnnotated(edit) ? edit : edit with
            {
                DocumentChanges = edit.DocumentChanges is { } documentChanges
                    ? new Container<WorkspaceEditDocumentChange>(documentChanges.Select(change => change.IsTextDocumentEdit
                        ? new WorkspaceEditDocumentChange(change.TextDocumentEdit! with { Edits = new TextEditContainer(Plain(change.TextDocumentEdit.Edits, annotations)) })
                        : change))
                    : null,
                Changes = Plain(edit.Changes, annotations),
                ChangeAnnotations = null,
            };
        }

        // changes cannot carry an annotation: what needs confirming is left out, whatever the client takes.
        annotations ??= NeedingConfirmation(edit);
        if (edit.Changes is { Count: > 0 })
        {
            return edit with { DocumentChanges = null, Changes = Plain(edit.Changes, annotations), ChangeAnnotations = null };
        }

        if (edit.DocumentChanges is not { } textChanges)
        {
            return edit;
        }

        if (textChanges.Any(change => !change.IsTextDocumentEdit))
        {
            throw new WorkspaceEditNotSupportedException(Exceptions.WorkspaceEdit_FileOperationNotSupported);
        }

        var byDocument = new Dictionary<DocumentUri, IEnumerable<TextEdit>>();
        foreach (var document in textChanges.Select(change => change.TextDocumentEdit!))
        {
            if (!byDocument.TryAdd(document.TextDocument.Uri, Plain(document.Edits, annotations)))
            {
                throw new WorkspaceEditNotSupportedException(string.Format(Exceptions.WorkspaceEdit_DocumentEditedTwice, document.TextDocument.Uri));
            }
        }

        return edit with { DocumentChanges = null, Changes = byDocument, ChangeAnnotations = null };
    }

    // whether anything of the edit is annotated: its annotations, or a text edit that names one.
    private static bool IsAnnotated(WorkspaceEdit edit)
        => edit.ChangeAnnotations is { Count: > 0 }
            || edit.Changes?.Values.Any(edits => edits.Any(each => each is AnnotatedTextEdit)) == true
            || edit.DocumentChanges?.Any(change => change.IsTextDocumentEdit && change.TextDocumentEdit!.Edits.Any(each => each is AnnotatedTextEdit)) == true;

    // the annotations whose edits the user is asked to confirm.
    private static HashSet<ChangeAnnotationIdentifier> NeedingConfirmation(WorkspaceEdit edit)
        => [.. (edit.ChangeAnnotations ?? new Dictionary<ChangeAnnotationIdentifier, ChangeAnnotation>())
            .Where(annotation => annotation.Value.NeedsConfirmation == true)
            .Select(annotation => annotation.Key)];

    // the edits with no annotation: one that needs confirming is left out, and the others are what they say.
    private static List<TextEdit> Plain(IEnumerable<TextEdit> edits, HashSet<ChangeAnnotationIdentifier> needingConfirmation) => [.. edits
        .Where(edit => edit is not AnnotatedTextEdit { AnnotationId: var id } || !needingConfirmation.Contains(id))
        .Select(edit => edit is AnnotatedTextEdit ? new TextEdit { Range = edit.Range, NewText = edit.NewText } : edit)];

    private static IDictionary<DocumentUri, IEnumerable<TextEdit>>? Plain(IDictionary<DocumentUri, IEnumerable<TextEdit>>? changes,
        HashSet<ChangeAnnotationIdentifier> needingConfirmation)
        => changes?.ToDictionary(change => change.Key, change => (IEnumerable<TextEdit>)Plain(change.Value, needingConfirmation));

    // a file created, renamed or deleted, of a kind the client said it does.
    private static bool TakesResourceOperation(ClientCapabilities? client, WorkspaceEditDocumentChange change)
    {
        var kind = change.IsCreateFile ? ResourceOperationKind.Create : change.IsRenameFile ? ResourceOperationKind.Rename : ResourceOperationKind.Delete;
        return client?.Workspace?.WorkspaceEdit is { IsSupported: true, Value.ResourceOperations: { } kinds } && kinds.Contains(kind);
    }
}

/// <summary>
/// A <see cref="WorkspaceEdit"/> that says what the client it is for did not say it can apply (<see cref="WorkspaceEditExtensions.For"/>): what asked for it
/// is answered that it can't be done, and why, rather than an edit the client applies none of.
/// </summary>
/// <param name="message">Why, as the user is told.</param>
public sealed class WorkspaceEditNotSupportedException(string message) : Exception(message);
