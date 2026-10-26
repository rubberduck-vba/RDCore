using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Serves <c>textDocument/rename</c> (<strong>LSP 3.17</strong> §Rename Request): renames the symbol named at a position, in every document that refers to it.
/// </summary>
/// <remarks>
/// A rename that is refused - a keyword for a new name, a name that already means something where the symbol is used, a symbol the workspace does not
/// declare - is answered with an error that says why, which a client shows its user.
/// </remarks>
internal sealed class RenameHandler(IRenameService rename) : RenameHandlerBase
{
    public override async Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
        => (await rename.RenameAsync(request.TextDocument.Uri.ToUri(), request.Position, request.NewName, cancellationToken)).ValueOrRefusal();

    protected override RenameRegistrationOptions CreateRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities)
        => new() { PrepareProvider = true };
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
