using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Answers <c>rdcore/semantics/describe</c> and <c>rdcore/semantics/outline</c>: what the language model and the environment host's static pass know of
/// what is written at a position of a document, and of what a module declares.
/// </summary>
internal interface ISemanticsDescriptionService
{
    /// <summary>
    /// The symbol the name at a position refers to, and the innermost expression written there.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the document's text.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<DescribeResult> DescribeAsync(Uri documentUri, Position position, CancellationToken token);

    /// <summary>
    /// The module a document is, and what it declares.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The outline; <see langword="null"/> for a document that is not of the workspace, or has no syntax tree at all.</returns>
    Task<ModuleOutline?> OutlineAsync(Uri documentUri, CancellationToken token);
}

internal sealed class SemanticsDescriptionService(
    IRenameService workspace,
    IDocumentDiagnosticsService diagnostics,
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    ISymbolResolver resolver,
    IOptions<SdkAppOptions> options) : ISemanticsDescriptionService
{
    public async Task<DescribeResult> DescribeAsync(Uri documentUri, Position position, CancellationToken token)
    {
        if (await workspace.ComposeAsync(token) is not { } composed || composed.DocumentOf(documentUri) is not { Module: var module })
        {
            return new DescribeResult();
        }

        var semantics = await diagnostics.SemanticsAsync(documentUri, token);
        var expression = semantics is null ? null : ExpressionDescription.At(semantics, module.ToModulePosition(position));
        expression = expression is null ? null : expression with { Range = module.ToTextRange(expression.Range).ToSourceRange() };

        if (composed.SymbolAt(documentUri, position) is not { } named)
        {
            return new DescribeResult { Expression = expression, Range = expression?.Range };
        }

        // the facts of a declaration are the host's of the module it is written in, which is the document's own when the definition is there.
        var definition = composed.Definitions(documentUri, position).FirstOrDefault();
        var declaring = definition is null || definition.TargetUri.ToUri().AbsoluteUri == documentUri.AbsoluteUri
            ? semantics
            : await diagnostics.SemanticsAsync(definition.TargetUri.ToUri(), token);
        var fact = declaring?.Declarations.FirstOrDefault(declaration => new SemanticId(declaration.Symbol) == named.Symbol.SemanticId);

        var hover = SymbolHover.Of(named.Symbol);
        var symbol = DescribedSymbols.Describe(named.Symbol, hover.Declaration).WithFacts(fact) with
        {
            Container = hover.Container,
            ControlArrayOf = hover.ControlArrayOf,
            Location = definition is null ? null : new SourceLocation(definition.TargetUri.ToUri(), definition.TargetSelectionRange.ToSourceRange()),
        };

        return new DescribeResult { Symbol = symbol, Expression = expression, Range = named.Range.ToSourceRange() };
    }

    public async Task<ModuleOutline?> OutlineAsync(Uri documentUri, CancellationToken token)
    {
        if (!documents.TryGetModule(documentUri, out var document)
            || await DocumentOutline.SymbolsOfAsync(document, parsing, resolver, options.Value, token) is not { } declared)
        {
            return null;
        }

        var semantics = await diagnostics.SemanticsAsync(documentUri, token);
        var isProgram = BasicProgramText.IsProgram(document.Id.Uri.GetFileSystemPath());
        var entries = DescribedSymbols.OutlineOf(declared.Module, declared.Symbols, symbol => SymbolHover.Of(symbol).Declaration);
        return new ModuleOutline
        {
            Uri = documentUri,
            Name = declared.Name,
            ModuleType = declared.Type,
            HasDesigner = FormDesigner.Read(document.RelativePath, document.Text, document.Companion) is not null,
            Language = semantics?.Language,
            OptionExplicit = semantics?.OptionExplicit,
            Entries = isProgram ? [.. entries.Select(InProgramText)] : entries,
        };
    }

    // a program is parsed as the body of a module whose header it does not have: its ranges are moved to its lines.
    private static OutlineEntry InProgramText(OutlineEntry entry) => entry with
    {
        Range = InProgramText(entry.Range),
        SelectionRange = InProgramText(entry.SelectionRange),
        Children = [.. entry.Children.Select(InProgramText)],
    };

    private static SourceRange InProgramText(SourceRange range) => BasicProgramText.ToTextRange(range.ToLsp()).ToSourceRange();
}
