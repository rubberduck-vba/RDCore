using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Refactoring;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Navigation;

/// <summary>
/// Serves <c>textDocument/declaration</c> (<strong>LSP 3.17</strong> §Goto Declaration Request): the one place that declares the symbol named at a position,
/// in the document of the workspace that declares it (<see cref="SymbolNavigation.DeclarationAt"/>).
/// </summary>
/// <remarks>
/// Where <c>textDocument/definition</c> leads to each definition that is compiled - each branch of an <c>#If</c>, each procedure of a property - this leads to
/// the first written, whichever are compiled. It is answered as <see cref="DefinitionHandler"/> answers: links for a client that reads them, a location
/// otherwise, and nothing for a name the workspace does not declare.
/// </remarks>
internal sealed class DeclarationHandler(IRenameService workspace) : DeclarationHandlerBase
{
    private bool _linkSupport;

    public override async Task<LocationOrLocationLinks?> Handle(DeclarationParams request, CancellationToken cancellationToken)
    {
        if (await workspace.ComposeAsync(cancellationToken) is not { } composed)
        {
            return new LocationOrLocationLinks();
        }

        var links = composed.Declarations(request.TextDocument.Uri.ToUri(), request.Position);
        return _linkSupport
            ? new LocationOrLocationLinks(links.Select(link => new LocationOrLocationLink(link)))
            : new LocationOrLocationLinks(links.Select(link => new LocationOrLocationLink(new Location { Uri = link.TargetUri, Range = link.TargetSelectionRange })));
    }

    protected override DeclarationRegistrationOptions CreateRegistrationOptions(DeclarationCapability capability, ClientCapabilities clientCapabilities)
    {
        _linkSupport = capability.LinkSupport;
        return new DeclarationRegistrationOptions();
    }
}
