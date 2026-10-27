using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Refactoring;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Navigation;

/// <summary>
/// Serves <c>textDocument/typeDefinition</c> (<strong>LSP 3.17</strong> §Goto Type Definition Request): where the class, <c>Type</c> or <c>Enum</c> the
/// symbol named at a position is declared as is defined (<see cref="SymbolNavigation.TypeDefinitionAt"/>).
/// </summary>
internal sealed class TypeDefinitionHandler(IRenameService workspace) : TypeDefinitionHandlerBase
{
    private bool _linkSupport;

    public override async Task<LocationOrLocationLinks?> Handle(TypeDefinitionParams request, CancellationToken cancellationToken)
        => NavigationAnswer.Of(await workspace.ComposeAsync(cancellationToken) is { } composed
            ? composed.TypeDefinitions(request.TextDocument.Uri.ToUri(), request.Position)
            : [], _linkSupport);

    protected override TypeDefinitionRegistrationOptions CreateRegistrationOptions(TypeDefinitionCapability capability, ClientCapabilities clientCapabilities)
    {
        _linkSupport = capability.LinkSupport;
        return new TypeDefinitionRegistrationOptions();
    }
}

/// <summary>
/// Serves <c>textDocument/implementation</c> (<strong>LSP 3.17</strong> §Goto Implementation Request): the classes that implement the class named at a
/// position, or the procedures that implement the member named there (<see cref="SymbolNavigation.ImplementationsAt"/>).
/// </summary>
internal sealed class ImplementationHandler(IRenameService workspace) : ImplementationHandlerBase
{
    private bool _linkSupport;

    public override async Task<LocationOrLocationLinks?> Handle(ImplementationParams request, CancellationToken cancellationToken)
        => NavigationAnswer.Of(await workspace.ComposeAsync(cancellationToken) is { } composed
            ? composed.Implementations(request.TextDocument.Uri.ToUri(), request.Position)
            : [], _linkSupport);

    protected override ImplementationRegistrationOptions CreateRegistrationOptions(ImplementationCapability capability, ClientCapabilities clientCapabilities)
    {
        _linkSupport = capability.LinkSupport;
        return new ImplementationRegistrationOptions();
    }
}

/// <summary>
/// What a navigation is answered with: links for a client that reads them, and a location per link, at the name it leads to, for one that does not.
/// </summary>
internal static class NavigationAnswer
{
    public static LocationOrLocationLinks Of(IReadOnlyList<LocationLink> links, bool linkSupport)
        => linkSupport
            ? new LocationOrLocationLinks(links.Select(link => new LocationOrLocationLink(link)))
            : new LocationOrLocationLinks(links.Select(link => new LocationOrLocationLink(new Location { Uri = link.TargetUri, Range = link.TargetSelectionRange })));
}
