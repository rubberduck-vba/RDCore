using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.ProtocolExtensions;

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
/// The edits of the strings a library calls the symbol by are the user's to confirm (<see cref="RenameWorkspace.InString"/>): a client that takes change
/// annotations asks them itself; any other is asked for them (<c>window/showMessageRequest</c>) before it is answered - with the strings renamed, the strings
/// left, or nothing at all when the user does not choose.
/// </para>
/// </remarks>
internal sealed class RenameHandler(IRenameService rename, ILanguageServerFacade server) : RenameHandlerBase
{
    // what the client said it can do, as it initialized: the edits are sent in the form it applies.
    private ClientCapabilities? _client;

    public override async Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
    {
        var answer = await rename.RenameAsync(request.TextDocument.Uri.ToUri(), request.Position, request.NewName, cancellationToken);
        var edit = answer.ValueOrRefusal();
        if (answer.Question is { } question && !_client.TakesChangeAnnotations())
        {
            var renameThem = RefactoringTitles.RenameInStringRename();
            var leaveThem = RefactoringTitles.RenameInStringLeave();
            var chosen = await server.Window.ShowMessageRequest(new ShowMessageRequestParams
            {
                Type = MessageType.Warning,
                Message = question,
                Actions = new Container<MessageActionItem>(new MessageActionItem { Title = renameThem }, new MessageActionItem { Title = leaveThem }),
            }, cancellationToken);
            if (chosen?.Title == renameThem)
            {
                edit = Confirmed(edit);
            }
            else if (chosen?.Title != leaveThem)
            {
                return null;
            }
        }

        // an edit the client can't apply is refused as any other rename is, with why: one it would apply none of would say nothing.
        try
        {
            return edit.For(_client);
        }
        catch (WorkspaceEditNotSupportedException refused)
        {
            throw new RpcErrorException(RenameAnswer<WorkspaceEdit>.RequestFailedCode, error: null!, refused.Message);
        }
    }

    // the edit as the user confirmed it: what needed confirming no longer does, and is sent as the other edits are.
    private static WorkspaceEdit Confirmed(WorkspaceEdit edit) => edit with
    {
        ChangeAnnotations = edit.ChangeAnnotations?.ToDictionary(annotation => annotation.Key, annotation => annotation.Value with { NeedsConfirmation = false }),
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
    public override async Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken)
        => new((await rename.PrepareAsync(request.TextDocument.Uri.ToUri(), request.Position, cancellationToken)).ValueOrRefusal());

    // the prepare request is the rename request's own: it registers as the rename does.
    protected override RenameRegistrationOptions CreateRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities)
        => new() { PrepareProvider = true };
}
