using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.SDK.Model.Symbols;
using RDCore.Symbols;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// The outline of a document of the workspace: its module, and what the module declares, as a <see cref="DocumentSymbol"/> tree
/// (<see cref="DocumentSymbolProjector"/>) in the text of the document - for a program of the platform's BASIC, its lines and not the module they are the
/// body of (<see cref="BasicProgramText"/>).
/// </summary>
/// <remarks>
/// <c>textDocument/documentSymbol</c> answers it, and <c>workspace/symbol</c> searches the outline of every document: the two say the same thing of a symbol.
/// </remarks>
internal static class DocumentOutline
{
    /// <summary>
    /// The outline of a document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="parsing">What parses it, with the text it holds.</param>
    /// <param name="resolver">What resolves the types its declarations name.</param>
    /// <param name="options">The options of the server: the language, which says the scope of an implicit declaration.</param>
    /// <param name="token">A token that cancels the parse.</param>
    /// <returns>The outline; <see langword="null"/> for a document with no syntax tree at all.</returns>
    public static async Task<DocumentSymbol?> OfAsync(
        WorkspaceDocument document, IParsingClientService parsing, ISymbolResolver resolver, SdkAppOptions options, CancellationToken token)
    {
        var parseResult = await parsing.ParseDocumentAsync(document.Id.Uri.ToUri(), token);
        if (parseResult.SyntaxTree is not { } module)
        {
            return null;
        }

        var workspaceRoot = document.ProjectRoot;
        var moduleName = module.GetDeclaredName() ?? ModuleName.FromFileName(document.RelativePath);
        var moduleUri = SymbolUri.Module(workspaceRoot, moduleName);
        var moduleType = ParsingClientService.ModuleTypeOf(document);
        var moduleKind = moduleType.IsClass() ? SymbolKindExt.Class : SymbolKindExt.Module;

        var symbols = new SyntaxTreeSymbolProvider(
            workspaceRoot, moduleUri, moduleType, parseResult, resolver,
            withImplicitDeclarations: true, options.Workspace.SupportedLanguage.ImplicitDeclarationScope).ProvideSymbols();
        var (moduleRange, moduleSelectionRange) = ModuleRangesOf(module);

        var outline = DocumentSymbolProjector.Project(moduleUri, moduleName, moduleKind, moduleRange, moduleSelectionRange, symbols);
        return BasicProgramText.IsProgram(document.Id.Uri.GetFileSystemPath()) ? InProgramText(outline) : outline;
    }

    // a program is parsed as the body of a module whose header it does not have: its ranges are moved to its lines.
    private static DocumentSymbol InProgramText(DocumentSymbol symbol) => new()
    {
        Name = symbol.Name,
        Detail = symbol.Detail,
        Kind = symbol.Kind,
        Tags = symbol.Tags,
        Range = BasicProgramText.ToTextRange(symbol.Range),
        SelectionRange = BasicProgramText.ToTextRange(symbol.SelectionRange),
        Children = symbol.Children is null ? null : new Container<DocumentSymbol>(symbol.Children.Select(InProgramText)),
    };

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
}
