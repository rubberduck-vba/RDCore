using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
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
    /// The outline of a document: one root for each of its modules, the file's own first and then each block's, as LSP allows a document several.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="parsing">What parses it, with the text it holds.</param>
    /// <param name="resolver">What resolves the types its declarations name.</param>
    /// <param name="options">The options of the server: the language, which says the scope of an implicit declaration.</param>
    /// <param name="token">A token that cancels the parse.</param>
    /// <returns>The roots; none for a document with no syntax tree at all.</returns>
    public static async Task<IReadOnlyList<DocumentSymbol>> OfAsync(
        WorkspaceDocument document, IParsingClientService parsing, ISymbolResolver resolver, SdkAppOptions options, CancellationToken token)
    {
        var isProgram = BasicProgramText.IsProgram(document.Id.Uri.GetFileSystemPath());
        return [.. (await ModulesOfAsync(document, parsing, resolver, options, token)).Select(declared =>
        {
            var moduleKind = declared.Type.IsClass() ? SymbolKindExt.Class : SymbolKindExt.Module;
            var (moduleRange, moduleSelectionRange) = declared.Block is { } block ? BlockRangesOf(block) : ModuleRangesOf(declared.Tree);
            var outline = DocumentSymbolProjector.Project(declared.Module, declared.Name, moduleKind, moduleRange, moduleSelectionRange, declared.Symbols);
            return isProgram ? InProgramText(outline) : outline;
        })];
    }

    /// <summary>
    /// The file's own module of a document, and the symbols of what it declares, as an outline has them: implicit declarations included.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="parsing">What parses it, with the text it holds.</param>
    /// <param name="resolver">What resolves the types its declarations name.</param>
    /// <param name="options">The options of the server: the language, which says the scope of an implicit declaration.</param>
    /// <param name="token">A token that cancels the parse.</param>
    /// <returns>The module and its symbols, in the module's text; <see langword="null"/> for a document with no syntax tree at all.</returns>
    public static async Task<DeclaredSymbols?> SymbolsOfAsync(
        WorkspaceDocument document, IParsingClientService parsing, ISymbolResolver resolver, SdkAppOptions options, CancellationToken token)
        => (await ModulesOfAsync(document, parsing, resolver, options, token)).FirstOrDefault();

    // each module of a document, its own first and then each block's (ParsedModules), and the symbols of what each declares.
    private static async Task<IReadOnlyList<DeclaredSymbols>> ModulesOfAsync(
        WorkspaceDocument document, IParsingClientService parsing, ISymbolResolver resolver, SdkAppOptions options, CancellationToken token)
    {
        var file = document.Id.Uri.ToUri();
        var parseResult = await parsing.ParseDocumentAsync(file, token);
        if (parseResult.SyntaxTree is null)
        {
            return [];
        }

        var workspaceRoot = document.ProjectRoot;
        var modules = ParsedModules.Of(workspaceRoot, file, parseResult, ModuleName.FromFileName(document.RelativePath),
            ParsingClientService.ModuleTypeOf(document));
        return [.. modules.Select(module =>
        {
            var symbols = new SyntaxTreeSymbolProvider(
                workspaceRoot, module.Uri, module.Kind, module.Parse, resolver,
                withImplicitDeclarations: true, options.Workspace.SupportedLanguage.ImplicitDeclarationScope).ProvideSymbols();
            var block = module.Range is null ? null : parseResult.BlockModules.First(block => block.Range == module.Range);
            return new DeclaredSymbols(module.Uri, module.Name, module.Kind, module.Parse.SyntaxTree!, [.. symbols]) { Block = block };
        })];
    }

    /// <summary>
    /// A module, and the symbols of what it declares.
    /// </summary>
    /// <param name="Module">The module's address (<see cref="SymbolUri.Module(Uri, string)"/>).</param>
    /// <param name="Name">The module's name.</param>
    /// <param name="Type">What module it is.</param>
    /// <param name="Tree">Its syntax tree.</param>
    /// <param name="Symbols">The symbols of its declarations, and of its members' declarations.</param>
    internal sealed record class DeclaredSymbols(Uri Module, string Name, ModuleType Type, ModuleNode Tree, IReadOnlyList<Symbol> Symbols)
    {
        // the block whose module it is; null for the file's own module.
        public BlockModule? Block { get; init; }
    }

    // a block's module is written from its header to its end, and named in its header: where the header writes the name, or else the header's start.
    private static (SourceRange Range, SourceRange SelectionRange) BlockRangesOf(BlockModule block)
        => (block.Range, block.NameRange ?? new SourceRange(block.Range.Start, block.Range.Start));

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
    internal static (SourceRange Range, SourceRange SelectionRange) ModuleRangesOf(ModuleNode module)
    {
        if (module.Children.IsDefaultOrEmpty)
        {
            return (SourceRange.Empty, SourceRange.Empty);
        }

        var range = new SourceRange(module.Children[0].SourceLocation.Range.Start, module.Children[^1].SourceLocation.Range.End);

        var selectionRange = module.GetDeclaredNameDirective()?.SourceLocation.Range ?? new SourceRange(range.Start, range.Start);

        return (range, selectionRange);
    }
}
