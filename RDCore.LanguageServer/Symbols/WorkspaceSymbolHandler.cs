using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.LanguageServer.Services.Refactoring;
using LspWorkspaceSymbol = OmniSharp.Extensions.LanguageServer.Protocol.Models.WorkspaceSymbol;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Serves <c>workspace/symbol</c> (<strong>LSP 3.17</strong> §Workspace Symbols Request): the symbols of every document of the workspace whose name matches
/// what a client asks, the closest matches first (<see cref="SymbolSearch"/>).
/// </summary>
/// <remarks>
/// A symbol is what the outline of its document says (<see cref="DocumentOutline"/>): each module, what it declares, and the members of its <c>Enum</c>s and
/// <c>Type</c>s, each named with what it is declared in (<c>containerName</c>) and located at the name it declares (<see cref="IWorkspaceSymbolSearch"/>).
/// At most <see cref="Limit"/> symbols are answered: a client asks again as its user types.
/// </remarks>
internal sealed class WorkspaceSymbolHandler(IWorkspaceSymbolSearch search) : WorkspaceSymbolsHandlerBase
{
    /// <summary>The most symbols an answer has.</summary>
    internal const int Limit = 500;

    public override async Task<Container<LspWorkspaceSymbol>?> Handle(WorkspaceSymbolParams request, CancellationToken cancellationToken)
        => new Container<LspWorkspaceSymbol>((await search.SearchAsync(request.Query ?? string.Empty, cancellationToken))
            .Take(Limit)
            .Select(entry => new LspWorkspaceSymbol
            {
                Name = entry.Name,
                Kind = entry.Kind,
                ContainerName = entry.ContainerName,
                Location = new Location { Uri = entry.Uri, Range = entry.SelectionRange },
            }));

    protected override WorkspaceSymbolRegistrationOptions CreateRegistrationOptions(WorkspaceSymbolCapability capability, ClientCapabilities clientCapabilities)
        => new();
}
