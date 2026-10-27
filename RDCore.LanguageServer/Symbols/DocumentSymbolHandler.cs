using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Serves <c>textDocument/documentSymbol</c>, projecting the requested document's module and its
/// declared members into a hierarchical <see cref="DocumentSymbol"/> tree.
/// </summary>
/// <remarks>
/// Resolves the document against the workspace and parses through <see cref="IParsingClientService"/>,
/// the same way <see cref="Folding.FoldingRangeHandler"/> does — see its remarks for why a workspace-cold
/// request goes through <c>ParseDocumentAsync</c> rather than the parse cache. A document that is not
/// part of the workspace, or one with no syntax tree at all, answers an empty container rather than
/// <c>null</c>; a module with syntax errors is still projected, the same way an outline is worth most
/// on a document that does not currently compile.
/// </remarks>
internal sealed class DocumentSymbolHandler(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    ISymbolResolver resolver,
    IOptions<SdkAppOptions> options) : DocumentSymbolHandlerBase
{
    public override async Task<SymbolInformationOrDocumentSymbolContainer?> Handle(DocumentSymbolParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();

        if (Resolve(uri) is not { } document)
        {
            return new SymbolInformationOrDocumentSymbolContainer();
        }

        return await DocumentOutline.OfAsync(document, parsing, resolver, options.Value, cancellationToken) is { } outline
            ? new SymbolInformationOrDocumentSymbolContainer(SymbolInformationOrDocumentSymbol.Create(outline))
            : new SymbolInformationOrDocumentSymbolContainer();
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability, ClientCapabilities clientCapabilities)
        => new();

    private WorkspaceDocument? Resolve(Uri documentUri)
        => documents.GetAllDocuments().FirstOrDefault(document => document.Id.Uri.ToUri().Equals(documentUri));
}
