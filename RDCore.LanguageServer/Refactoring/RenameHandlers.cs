using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Serves <c>textDocument/rename</c> (<strong>LSP 3.17</strong> §Rename Request): renames the symbol named at a position, in every document that refers to it.
/// </summary>
/// <remarks>
/// <para>
/// A rename that is refused - a keyword for a new name, a name that already means something where the symbol is used, a symbol the workspace does not
/// declare - is answered with an error that says why, which a client shows its user.
/// </para>
/// <para>
/// The edits of the strings a library calls the symbol by (<see cref="RenameWorkspace.InString"/>), and of the members accessed late-bound that may be the
/// symbol (<see cref="RenameWorkspace.LateBoundBacked"/>, <see cref="RenameWorkspace.LateBound"/>), are the user's to confirm: a client that takes change
/// annotations asks them itself; any other is asked for them (<c>window/showMessageRequest</c>), one question for each annotation, before it is answered:
/// with those edits, without them, or nothing at all when the user does not choose.
/// </para>
/// <para>
/// A client with nobody to ask (<see cref="SDK.Client.Unattended"/>) is sent the rest of the rename, without any of the edits that need confirming.
/// </para>
/// </remarks>
internal sealed class RenameHandler(IRenameService rename, ILanguageServerFacade server, IServiceProvider services) : RenameHandlerBase
{
    // what the client said it can do, as it initialized: the edits are sent in the form it applies.
    private ClientCapabilities? _client;

    public override async Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
    {
        var answer = await rename.RenameAsync(request.TextDocument.Uri.ToUri(), request.Position, request.NewName, cancellationToken, _client);
        var edit = answer.ValueOrRefusal();
        foreach (var question in answer.Questions ?? [])
        {
            // an edit that no user can confirm is not made: what a string names, and which member a late-bound access calls, are only known when the code runs.
            if (IsUnattended())
            {
                edit = Without(edit, question.Annotation);
                continue;
            }

            if (_client.TakesChangeAnnotations())
            {
                continue;
            }

            var chosen = await server.Window.ShowMessageRequest(new ShowMessageRequestParams
            {
                Type = MessageType.Warning,
                Message = question.Message,
                Actions = new Container<MessageActionItem>(new MessageActionItem { Title = question.Rename }, new MessageActionItem { Title = question.Leave }),
            }, cancellationToken);
            if (chosen?.Title == question.Rename)
            {
                edit = Confirmed(edit, question.Annotation);
            }
            else if (chosen?.Title != question.Leave)
            {
                Discard();
                return null;
            }
        }

        // a module is named by its VB_Name: a client that does not rename files keeps the file's name, and is sent the rest of the edit. The edit of a file
        // once it is renamed (a designer's text naming its renamed binary file) goes with the rename.
        if (!TakesFileRenames(_client) && edit.DocumentChanges is { } documentChanges && documentChanges.Any(change => change.IsRenameFile))
        {
            var renamedTo = documentChanges.Where(change => change.IsRenameFile).Select(change => change.RenameFile!.NewUri.ToString()).ToHashSet(StringComparer.Ordinal);
            edit = edit with
            {
                DocumentChanges = new Container<WorkspaceEditDocumentChange>(documentChanges.Where(change
                    => !change.IsRenameFile && !(change.IsTextDocumentEdit && renamedTo.Contains(change.TextDocumentEdit!.TextDocument.Uri.ToString())))),
            };
        }

        // an edit the client can't apply is refused as any other rename is, with why: one it would apply none of would say nothing.
        try
        {
            return edit.For(_client);
        }
        catch (WorkspaceEditNotSupportedException refused)
        {
            Discard();
            throw new RpcErrorException(RenameAnswer<WorkspaceEdit>.RequestFailedCode, error: null!, refused.Message);
        }
    }

    // a rename the client is not sent puts no staged file in place: the files staged for it are deleted.
    private void Discard() => (services.GetService(typeof(StagedFiles)) as StagedFiles)?.Discard();

    private static bool TakesFileRenames(ClientCapabilities? client)
        => client.TakesDocumentChanges()
            && client?.Workspace?.WorkspaceEdit is { IsSupported: true, Value.ResourceOperations: { } kinds } && kinds.Contains(ResourceOperationKind.Rename);

    // whether the client said it runs with no user to ask (the platform handshake).
    private bool IsUnattended()
        => services.GetService(typeof(IPlatformClientCapabilitiesService)) is IPlatformClientCapabilitiesService expected
            && expected.Expects(capabilities => capabilities.Unattended);

    // the edit as the user confirmed it: the edits of the annotation no longer need confirming, and are sent as the other edits are.
    private static WorkspaceEdit Confirmed(WorkspaceEdit edit, ChangeAnnotationIdentifier confirmed) => edit with
    {
        ChangeAnnotations = edit.ChangeAnnotations?.ToDictionary(annotation => annotation.Key,
            annotation => annotation.Key == confirmed ? annotation.Value with { NeedsConfirmation = false } : annotation.Value),
    };

    // the edit without the edits of the annotation, nor the annotation.
    private static WorkspaceEdit Without(WorkspaceEdit edit, ChangeAnnotationIdentifier left) => edit with
    {
        DocumentChanges = edit.DocumentChanges is { } changes
            ? new Container<WorkspaceEditDocumentChange>(changes.Select(change => change.IsTextDocumentEdit
                ? new WorkspaceEditDocumentChange(change.TextDocumentEdit! with
                {
                    Edits = new TextEditContainer(change.TextDocumentEdit.Edits.Where(each => each is not AnnotatedTextEdit { AnnotationId: var id } || id != left)),
                })
                : change))
            : null,
        ChangeAnnotations = edit.ChangeAnnotations?.Where(annotation => annotation.Key != left).ToDictionary() is { Count: > 0 } kept ? kept : null,
    };

    protected override RenameRegistrationOptions CreateRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities)
    {
        _client = clientCapabilities;
        return new() { PrepareProvider = true };
    }
}

/// <summary>
/// Serves <c>textDocument/prepareRename</c> (<strong>LSP 3.17</strong> §Prepare Rename Request): the name a rename at a position would rename, and the name it
/// is declared with, which a client offers to edit.
/// </summary>
internal sealed class PrepareRenameHandler(IRenameService rename) : PrepareRenameHandlerBase
{
    // what the client said it can do, as it initialized: a UserForm's control is renamed only for a client that puts its .frx back when the edit fails.
    private ClientCapabilities? _client;

    public override async Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken)
        => new((await rename.PrepareAsync(request.TextDocument.Uri.ToUri(), request.Position, cancellationToken, _client)).ValueOrRefusal());

    // the prepare request is the rename request's own: it registers as the rename does.
    protected override RenameRegistrationOptions CreateRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities)
    {
        _client = clientCapabilities;
        return new() { PrepareProvider = true };
    }
}
