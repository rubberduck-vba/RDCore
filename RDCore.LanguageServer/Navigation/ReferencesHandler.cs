using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Runtime.LanguageCore.Refactoring;

namespace RDCore.LanguageServer.Navigation;

/// <summary>
/// Serves <c>textDocument/references</c> (<strong>LSP 3.17</strong> §Find References Request): each name, in every document of the workspace, that refers to
/// the symbol named at a position (<see cref="SymbolNavigation.ReferencesAt"/>) - what a rename of the symbol would rename, and the type names that name it.
/// </summary>
/// <remarks>
/// The names its declarations declare are references when the client asks for them (<c>context.includeDeclaration</c>). No name, or a name whose meaning is
/// not known, has no reference. Where a name whose meaning is not known could refer to the symbol - in an expression nested too deeply to be analyzed, a
/// member accessed late-bound - the user is told that the references may be incomplete (<c>window/showMessage</c>), as a rename of it is refused.
/// </remarks>
internal sealed class ReferencesHandler(IRenameService workspace, ILanguageServerFacade server) : ReferencesHandlerBase
{
    public override async Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
    {
        if (await workspace.ComposeAsync(cancellationToken) is not { } composed)
        {
            return new LocationContainer();
        }

        var (locations, incomplete) = composed.References(request.TextDocument.Uri.ToUri(), request.Position, request.Context?.IncludeDeclaration ?? true);
        if (incomplete is not null)
        {
            server.Window.ShowMessage(new ShowMessageParams { Type = MessageType.Info, Message = incomplete });
        }

        return new LocationContainer(locations);
    }

    protected override ReferenceRegistrationOptions CreateRegistrationOptions(ReferenceCapability capability, ClientCapabilities clientCapabilities) => new();
}

/// <summary>
/// Serves <c>textDocument/documentHighlight</c> (<strong>LSP 3.17</strong> §Document Highlights Request): the names in the document that refer to the symbol
/// named at a position, its declaration included - the references of <see cref="ReferencesHandler"/> that are in the document.
/// </summary>
internal sealed class DocumentHighlightHandler(IRenameService workspace) : DocumentHighlightHandlerBase
{
    public override async Task<DocumentHighlightContainer?> Handle(DocumentHighlightParams request, CancellationToken cancellationToken)
        => await workspace.ComposeAsync(cancellationToken) is { } composed
            ? new DocumentHighlightContainer(composed.Highlights(request.TextDocument.Uri.ToUri(), request.Position))
            : new DocumentHighlightContainer();

    protected override DocumentHighlightRegistrationOptions CreateRegistrationOptions(DocumentHighlightCapability capability, ClientCapabilities clientCapabilities)
        => new();
}
