using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Refactoring;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Navigation;

/// <summary>
/// Serves <c>textDocument/definition</c> (<strong>LSP 3.17</strong> §Goto Definition Request): where the symbol named at a position is defined, in the document
/// of the workspace that declares it (<see cref="SymbolNavigation"/>).
/// </summary>
/// <remarks>
/// The answer is made of links - the name the navigation starts from, the whole declaration, and the name it declares - for a client that reads them, and of
/// locations, the name it declares, for one that does not. A name the workspace does not declare, or whose meaning is not known, is answered with nothing.
/// </remarks>
internal sealed class DefinitionHandler(IRenameService workspace) : DefinitionHandlerBase
{
    private bool _linkSupport;

    public override async Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        if (await workspace.ComposeAsync(cancellationToken) is not { } composed)
        {
            return new LocationOrLocationLinks();
        }

        var links = composed.Definitions(request.TextDocument.Uri.ToUri(), request.Position);
        return _linkSupport
            ? new LocationOrLocationLinks(links.Select(link => new LocationOrLocationLink(link)))
            : new LocationOrLocationLinks(links.Select(link => new LocationOrLocationLink(new Location { Uri = link.TargetUri, Range = link.TargetSelectionRange })));
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities)
    {
        // a client that says nothing of it is given no capability at all: a throw here would leave initialize unanswered.
        _linkSupport = capability?.LinkSupport ?? false;
        return new DefinitionRegistrationOptions();
    }
}
