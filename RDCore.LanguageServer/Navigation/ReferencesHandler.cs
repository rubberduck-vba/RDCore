using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Refactoring;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Navigation;

/// <summary>
/// Serves <c>textDocument/references</c> (<strong>LSP 3.17</strong> §Find References Request): each name, in every document of the workspace, that refers to
/// the symbol named at a position (<see cref="SymbolNavigation.ReferencesAt"/>) - what a rename of the symbol would rename, and the type names that name it.
/// </summary>
/// <remarks>
/// The names its declarations declare are references when the client asks for them (<c>context.includeDeclaration</c>). No name, or a name whose meaning is
/// not known, has no reference.
/// </remarks>
internal sealed class ReferencesHandler(IRenameService workspace) : ReferencesHandlerBase
{
    public override async Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
        => await workspace.ComposeAsync(cancellationToken) is { } composed
            ? new LocationContainer(composed.References(request.TextDocument.Uri.ToUri(), request.Position, request.Context?.IncludeDeclaration ?? true))
            : new LocationContainer();

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
