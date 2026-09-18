using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;

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
    ISymbolResolver resolver) : DocumentSymbolHandlerBase
{
    public override async Task<SymbolInformationOrDocumentSymbolContainer?> Handle(DocumentSymbolParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();

        if (Resolve(uri) is not { } document)
        {
            return new SymbolInformationOrDocumentSymbolContainer();
        }

        var parseResult = await parsing.ParseDocumentAsync(uri, cancellationToken);

        if (parseResult.SyntaxTree is not { } module)
        {
            return new SymbolInformationOrDocumentSymbolContainer();
        }

        var workspaceRoot = new Uri(document.WorkspaceRoot);
        var moduleName = module.GetDeclaredName() ?? ModuleName.FromFileName(document.RelativePath);
        var moduleUri = new UriBuilder(workspaceRoot) { Fragment = moduleName }.Uri;
        var moduleType = ParsingClientService.ModuleTypeOf(document);
        var moduleKind = moduleType == ModuleType.ClassModule ? SymbolKindExt.Class : SymbolKindExt.Module;

        var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, moduleType, parseResult, resolver).ProvideSymbols();
        var (moduleRange, moduleSelectionRange) = ModuleRangesOf(module);

        var documentSymbol = DocumentSymbolProjector.Project(moduleUri, moduleName, moduleKind, moduleRange, moduleSelectionRange, symbols);

        return new SymbolInformationOrDocumentSymbolContainer(SymbolInformationOrDocumentSymbol.Create(documentSymbol));
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability, ClientCapabilities clientCapabilities)
        => new();

    /// <summary>
    /// The module's own declaration range and selection range. <c>ModuleNode</c> itself carries no
    /// valid range (see <see cref="Folding.FoldingRangeProjector"/>'s remarks), so the declaration
    /// range is the span from the module's first declared child to its last — the whole meaningful
    /// source text — and the selection range is the module-level <c>Attribute VB_Name</c> directive
    /// when the module declares one, falling back to a zero-width range at the start of that span.
    /// </summary>
    private static (SourceRange Range, SourceRange SelectionRange) ModuleRangesOf(ModuleNode module)
    {
        if (module.Children.IsDefaultOrEmpty)
        {
            return (SourceRange.Empty, SourceRange.Empty);
        }

        var range = new SourceRange(module.Children[0].SourceLocation.Range.Start, module.Children[^1].SourceLocation.Range.End);

        var nameAttribute = module.Children.OfType<AttributeDirectiveNode>()
            .FirstOrDefault(attribute => attribute.Binding is null && string.Equals(attribute.Name, Tokens.VB_Name, StringComparison.OrdinalIgnoreCase));
        var selectionRange = nameAttribute?.SourceLocation.Range ?? new SourceRange(range.Start, range.Start);

        return (range, selectionRange);
    }

    private WorkspaceDocument? Resolve(Uri documentUri)
        => documents.GetAllDocuments().FirstOrDefault(document => document.Id.Uri.ToUri().Equals(documentUri));
}
