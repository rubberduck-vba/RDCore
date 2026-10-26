using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Renames a symbol of the workspace (<strong>LSP 3.17</strong> §Rename Request, §Prepare Rename Request): the names that refer to it in every document, and no
/// other name (<see cref="SymbolRename"/>).
/// </summary>
internal interface IRenameService
{
    /// <summary>
    /// What a rename at a position of a document would rename: the name there, and the name it is declared with.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">A position in the name, in the text of the document.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The name, or why it is not renamed.</returns>
    Task<RenameAnswer<PlaceholderRange>> PrepareAsync(Uri documentUri, Position position, CancellationToken token);

    /// <summary>
    /// The edits that rename the symbol named at a position of a document.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">A position in the name, in the text of the document.</param>
    /// <param name="newName">The new name.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The edits, of every document whose text changes, or why there are none.</returns>
    Task<RenameAnswer<WorkspaceEdit>> RenameAsync(Uri documentUri, Position position, string newName, CancellationToken token);
}

/// <summary>
/// What a rename request is answered with: the answer, or why there is none, said for the reader.
/// </summary>
/// <param name="Value">The answer.</param>
/// <param name="Refusal">Why there is none, in the language of the reader.</param>
internal sealed record class RenameAnswer<T>(T? Value, string? Refusal) where T : class
{
    /// <summary>JSON-RPC "Request Failed" (<strong>LSP 3.17</strong> <c>ErrorCodes.RequestFailed</c>): the request was valid, and is refused.</summary>
    public const int RequestFailedCode = -32803;

    /// <summary>The answer, or the error response that says why there is none.</summary>
    public T ValueOrRefusal() => Value ?? throw new RpcErrorException(RequestFailedCode, error: null!, Refusal ?? string.Empty);
}

/// <inheritdoc cref="IRenameService"/>
/// <remarks>
/// The workspace is composed from the parse of each document as the server holds it - the text the client last sent - with the libraries the project references,
/// as the symbols of the workspace are (<see cref="WorkspaceSymbolResolver"/>). A program of the platform's BASIC is the module its lines are the body of, and
/// its positions are those of its lines (<see cref="BasicProgramText"/>).
/// </remarks>
internal sealed class RenameService(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    ISymbolResolver resolver,
    IReferencedLibraryService referencedLibraries,
    IOptions<SdkAppOptions> options,
    ILogger<RenameService> logger) : IRenameService
{
    public async Task<RenameAnswer<PlaceholderRange>> PrepareAsync(Uri documentUri, Position position, CancellationToken token)
    {
        if (await ComposeAsync(token) is not { } workspace || workspace.ModuleOf(documentUri) is not { } module)
        {
            return new(null, new RenameRefusal(RenameRefusalKind.NoName, string.Empty).ToMessage());
        }

        var preparation = SymbolRename.Prepare(workspace.Context, module.Module, module.ToModulePosition(position));
        if (preparation.Target is not { } target)
        {
            return new(null, workspace.Say(preparation.Refusal!));
        }

        return new(new PlaceholderRange { Range = module.ToTextRange(target.Range), Placeholder = target.Name }, null);
    }

    public async Task<RenameAnswer<WorkspaceEdit>> RenameAsync(Uri documentUri, Position position, string newName, CancellationToken token)
    {
        if (await ComposeAsync(token) is not { } workspace || workspace.ModuleOf(documentUri) is not { } module)
        {
            return new(null, new RenameRefusal(RenameRefusalKind.NoName, string.Empty).ToMessage());
        }

        var preparation = SymbolRename.Prepare(workspace.Context, module.Module, module.ToModulePosition(position));
        if (preparation.Target is not { } target)
        {
            return new(null, workspace.Say(preparation.Refusal!));
        }

        var plan = SymbolRename.Plan(workspace.Context, target, newName);
        if (!plan.IsValid)
        {
            return new(null, workspace.Say(plan.Refusal!));
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("✏️ '{name}' renamed '{newName}': {count} name(s) in {modules} module(s).",
                target.Name, newName, plan.Edits.Length, plan.Edits.Select(edit => edit.Module.AbsoluteUri).Distinct().Count());
        }

        // each document's edits name the version they are edits of, so that a client applies them to that text and to no other.
        var changes = plan.Edits
            .GroupBy(edit => edit.Module.AbsoluteUri)
            .Select(group => workspace.Modules.First(candidate => candidate.Module.AbsoluteUri == group.Key))
            .Select(edited => new WorkspaceEditDocumentChange(new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = edited.Document.Id.Uri, Version = edited.Document.Version },
                Edits = new TextEditContainer(plan.Edits
                    .Where(edit => edit.Module.AbsoluteUri == edited.Module.AbsoluteUri)
                    .Select(edit => new TextEdit { Range = edited.ToTextRange(edit.Range), NewText = edit.NewText })),
            }));

        return new(new WorkspaceEdit { DocumentChanges = new Container<WorkspaceEditDocumentChange>(changes) }, null);
    }

    // every document of the workspace, parsed as the server holds it, and composed.
    private async Task<ComposedWorkspace?> ComposeAsync(CancellationToken token)
    {
        if (string.IsNullOrEmpty(documents.WorkspaceRoot))
        {
            return null;
        }

        var workspaceRoot = new Uri(documents.WorkspaceRoot);
        var modules = new List<DocumentModule>();
        foreach (var document in documents.GetAllDocuments())
        {
            var parse = await parsing.ParseDocumentAsync(document.Id.Uri.ToUri(), token);
            if (parse.SyntaxTree is null)
            {
                continue;
            }

            var name = parse.SyntaxTree.GetDeclaredName() ?? document.Name;
            modules.Add(new DocumentModule(document, new UriBuilder(workspaceRoot) { Fragment = name }.Uri, ParsingClientService.ModuleTypeOf(document), parse,
                ParsingClientService.ModuleSourceOf(document)));
        }

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(
            workspaceRoot, [.. modules.Select(module => (module.Module, module.Type, module.Parse))], resolver,
            implicitScope: options.Value.Workspace.SupportedLanguage.ImplicitDeclarationScope,
            libraries: referencedLibraries.SymbolsUnder(workspaceRoot));

        var context = new RenameContext(composition.Resolver, composition.ScopeTree,
            [.. modules.Select(module => new RenameModule(module.Module, module.Parse.SyntaxTree!, module.Source))],
            new StaticEnvironment(options.Value.Environment.Is64Bit));
        return new ComposedWorkspace(context, modules);
    }

    // the workspace composed, and the document of each of its modules.
    private sealed record class ComposedWorkspace(RenameContext Context, IReadOnlyList<DocumentModule> Modules)
    {
        public DocumentModule? ModuleOf(Uri documentUri)
            => Modules.FirstOrDefault(module => module.Document.Id.Uri.ToUri().AbsoluteUri == documentUri.AbsoluteUri);

        // a position is named the way the document names it: its file, and the line of its text - for a program of the BASIC, the number it labels the line with.
        public string Say(RenameRefusal refusal) => refusal.ToMessage((module, position) =>
            Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is { } document
                ? document.Locate(position)
                : $"{module.Fragment.TrimStart('#')} {position.Line + 1}:{position.Character + 1}");
    }

    // a document, and the module it is.
    private sealed record class DocumentModule(WorkspaceDocument Document, Uri Module, ModuleType Type, SDK.Model.AST.ModuleParseResult Parse, string Source)
    {
        private bool IsProgram => BasicProgramText.IsProgram(Document.Id.Uri.GetFileSystemPath());

        public SourcePosition ToModulePosition(Position position)
            => new(IsProgram ? position.Line + BasicProgramText.HeaderLines : position.Line, position.Character);

        public LspRange ToTextRange(SourceRange range)
        {
            var lsp = new LspRange(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);
            return IsProgram ? BasicProgramText.ToTextRange(lsp) : lsp;
        }

        public string Locate(SourcePosition position)
        {
            var text = ToTextRange(new SourceRange(position, position)).Start;
            if (IsProgram)
            {
                var line = Document.Text.Split('\n').ElementAtOrDefault(text.Line) ?? string.Empty;
                var number = new string([.. line.TakeWhile(char.IsDigit)]);
                if (number.Length > 0)
                {
                    return $"{Document.FileName} {number}";
                }
            }

            return $"{Document.FileName} {text.Line + 1}:{text.Character + 1}";
        }
    }
}
