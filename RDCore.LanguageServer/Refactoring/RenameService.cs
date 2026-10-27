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
    /// The workspace as a rename works it out: every document the server holds, parsed and composed. What is asked of it is answered for the text of each
    /// document as it is now: a request that asks several things composes it once.
    /// </summary>
    /// <param name="token">A token that cancels the parses.</param>
    /// <returns>The workspace; <see langword="null"/> when the server holds none.</returns>
    Task<RenameWorkspace?> ComposeAsync(CancellationToken token);

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
/// as the symbols of the workspace are (<see cref="WorkspaceSymbolResolver"/>).
/// </remarks>
internal sealed class RenameService(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    ISymbolResolver resolver,
    IReferencedLibraryService referencedLibraries,
    IOptions<SdkAppOptions> options,
    ILogger<RenameService> logger,
    WorkspaceCompositionCache? compositions = null) : IRenameService
{
    // the workspace as it was composed last, for as long as no document changed: a prepared rename, the rename and the code actions of a line are of one.
    private readonly WorkspaceCompositionCache _compositions = compositions ?? new();

    public async Task<RenameAnswer<PlaceholderRange>> PrepareAsync(Uri documentUri, Position position, CancellationToken token)
        => await ComposeAsync(token) is { } workspace ? workspace.Prepare(documentUri, position) : RenameWorkspace.NoName<PlaceholderRange>();

    public async Task<RenameAnswer<WorkspaceEdit>> RenameAsync(Uri documentUri, Position position, string newName, CancellationToken token)
    {
        var answer = await ComposeAsync(token) is { } workspace ? workspace.Rename(documentUri, position, newName) : RenameWorkspace.NoName<WorkspaceEdit>();
        if (answer.Value is { } edit && logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("✏️ Renamed to '{newName}': {count} name(s) in {documents} document(s).",
                newName, edit.DocumentChanges?.Sum(change => change.TextDocumentEdit?.Edits.Count() ?? 0), edit.DocumentChanges?.Count());
        }

        return answer;
    }

    public async Task<RenameWorkspace?> ComposeAsync(CancellationToken token)
    {
        if (string.IsNullOrEmpty(documents.WorkspaceRoot))
        {
            return null;
        }

        var workspaceRoot = new Uri(documents.WorkspaceRoot);
        var modules = new List<RenameWorkspace.DocumentModule>();
        foreach (var document in documents.GetAllDocuments())
        {
            var parse = await parsing.ParseDocumentAsync(document.Id.Uri.ToUri(), token);
            if (parse.SyntaxTree is null)
            {
                continue;
            }

            var name = parse.SyntaxTree.GetDeclaredName() ?? document.Name;
            modules.Add(new(document, new UriBuilder(workspaceRoot) { Fragment = name }.Uri, ParsingClientService.ModuleTypeOf(document), parse,
                ParsingClientService.ModuleSourceOf(document)));
        }

        var composition = _compositions.GetOrCompose(
            workspaceRoot, [.. modules.Select(module => (module.Module, module.Type, module.Parse))], resolver,
            implicitScope: options.Value.Workspace.SupportedLanguage.ImplicitDeclarationScope,
            libraries: referencedLibraries.SymbolsUnder(workspaceRoot));

        var context = new RenameContext(composition.Resolver, composition.ScopeTree,
            [.. modules.Select(module => new RenameModule(module.Module, module.Parse.SyntaxTree!, module.Source))],
            new StaticEnvironment(options.Value.Environment.Is64Bit));
        return new RenameWorkspace(context, modules);
    }
}

/// <summary>
/// The workspace, composed for renames (<see cref="IRenameService.ComposeAsync"/>), and the document of each of its modules: what a client asks of it is in
/// the text of the document, which for a program of the platform's BASIC is its lines and not the module they are the body of (<see cref="BasicProgramText"/>).
/// </summary>
internal sealed class RenameWorkspace
{
    private readonly RenameContext _context;
    private readonly IReadOnlyList<DocumentModule> _modules;

    internal RenameWorkspace(RenameContext context, IReadOnlyList<DocumentModule> modules)
    {
        _context = context;
        _modules = modules;
    }

    internal static RenameAnswer<T> NoName<T>() where T : class => new(null, new RenameRefusal(RenameRefusalKind.NoName, string.Empty).ToMessage());

    /// <summary>What a rename at a position of a document would rename.</summary>
    public RenameAnswer<PlaceholderRange> Prepare(Uri documentUri, Position position)
    {
        if (ModuleOf(documentUri) is not { } module)
        {
            return NoName<PlaceholderRange>();
        }

        var preparation = SymbolRename.Prepare(_context, module.Module, module.ToModulePosition(position));
        return preparation.Target is { } target
            ? new(new PlaceholderRange { Range = module.ToTextRange(target.Range), Placeholder = target.Name }, null)
            : new(null, Say(preparation.Refusal!));
    }

    /// <summary>The edits that rename the symbol named at a position of a document to <paramref name="newName"/>.</summary>
    public RenameAnswer<WorkspaceEdit> Rename(Uri documentUri, Position position, string newName)
    {
        if (ModuleOf(documentUri) is not { } module)
        {
            return NoName<WorkspaceEdit>();
        }

        var preparation = SymbolRename.Prepare(_context, module.Module, module.ToModulePosition(position));
        if (preparation.Target is not { } target)
        {
            return new(null, Say(preparation.Refusal!));
        }

        var plan = SymbolRename.Plan(_context, target, newName);
        if (!plan.IsValid)
        {
            return new(null, Say(plan.Refusal!));
        }

        // each document's edits name the version they are edits of, so that a client applies them to that text and to no other.
        var changes = plan.Edits
            .GroupBy(edit => edit.Module.AbsoluteUri)
            .Select(group => _modules.First(candidate => candidate.Module.AbsoluteUri == group.Key))
            .Select(edited => new WorkspaceEditDocumentChange(new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = edited.Document.Id.Uri, Version = edited.Document.Version },
                Edits = new TextEditContainer(plan.Edits
                    .Where(edit => edit.Module.AbsoluteUri == edited.Module.AbsoluteUri)
                    .Select(edit => new TextEdit { Range = edited.ToTextRange(edit.Range), NewText = edit.NewText })),
            }));

        return new(new WorkspaceEdit { DocumentChanges = new Container<WorkspaceEditDocumentChange>(changes) }, null);
    }

    /// <summary>
    /// What can be renamed in a range of a document (<see cref="SymbolRename.TargetsIn"/>): each name, and where it begins in the text of the document.
    /// </summary>
    public IReadOnlyList<(string Name, Position Position)> Renamable(Uri documentUri, LspRange range)
    {
        if (ModuleOf(documentUri) is not { } module)
        {
            return [];
        }

        var start = module.ToModulePosition(range.Start);
        var end = module.ToModulePosition(range.End);
        return [.. SymbolRename.TargetsIn(_context, module.Module, new SourceRange(start, end))
            .Select(target => (target.Name, module.ToTextRange(target.Range).Start))];
    }

    /// <summary>
    /// Where the symbol named at a position of a document is defined (<see cref="SymbolNavigation.DefinitionsAt"/>): each place, in the text of the document
    /// that declares it, with the name the navigation starts from.
    /// </summary>
    /// <returns>Each place; none where no name is written, where what the name refers to is not known, or where no document of the workspace declares it.</returns>
    public IReadOnlyList<LocationLink> Definitions(Uri documentUri, Position position)
    {
        if (ModuleOf(documentUri) is not { } module
            || SymbolNavigation.DefinitionsAt(_context, module.Module, module.ToModulePosition(position)) is not { } target)
        {
            return [];
        }

        var origin = module.ToTextRange(target.Origin);
        return [.. target.Sites
            .Select(site => (Site: site, Document: _modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == site.Module.AbsoluteUri)))
            .Where(found => found.Document is not null)
            .Select(found => new LocationLink
            {
                OriginSelectionRange = origin,
                TargetUri = found.Document!.Document.Id.Uri,
                TargetRange = found.Document.ToTextRange(found.Site.Range),
                TargetSelectionRange = found.Document.ToTextRange(found.Site.SelectionRange),
            })];
    }

    // a document is the same document however its URI was written: a client writes the drive and its colon as it likes (c%3A, C:), as LSP lets it.
    private DocumentModule? ModuleOf(Uri documentUri)
    {
        var document = DocumentUri.From(documentUri);
        return _modules.FirstOrDefault(module => module.Document.Id.Uri == document);
    }

    // a position is named the way the document names it: its file, and the line of its text - for a program of the BASIC, the number it labels the line with.
    private string Say(RenameRefusal refusal) => refusal.ToMessage((module, position) =>
        _modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is { } document
            ? document.Locate(position)
            : $"{module.Fragment.TrimStart('#')} {position.Line + 1}:{position.Character + 1}");

    /// <summary>A document, and the module it is.</summary>
    internal sealed record class DocumentModule(WorkspaceDocument Document, Uri Module, ModuleType Type, SDK.Model.AST.ModuleParseResult Parse, string Source)
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
