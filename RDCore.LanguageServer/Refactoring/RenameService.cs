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
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.SDK.Model.Symbols;
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
    WorkspaceCompositionCache? compositions = null,
    IWorkspaceProjects? workspaceProjects = null) : IRenameService
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

        // the project the server runs, then the others: each is composed of its own modules, so that a name resolves in the project of the module that writes it.
        List<RenameWorkspace.ProjectPart> parts = [await ComposeProjectAsync(documents.WorkspaceRoot, primary: true, name: null, token)];
        foreach (var other in workspaceProjects?.Projects.Skip(1) ?? [])
        {
            parts.Add(await ComposeProjectAsync(other.Root, primary: false, other.Project.ProjectInfo.Name, token));
        }

        return new RenameWorkspace(parts);
    }

    private async Task<RenameWorkspace.ProjectPart> ComposeProjectAsync(string root, bool primary, string? name, CancellationToken token)
    {
        var workspaceRoot = new Uri(root);
        var modules = new List<RenameWorkspace.DocumentModule>();
        foreach (var document in documents.GetProjectDocuments(root))
        {
            var parse = await parsing.ParseDocumentAsync(document.Id.Uri.ToUri(), token);
            if (parse.SyntaxTree is null)
            {
                continue;
            }

            var declared = parse.SyntaxTree.GetDeclaredName() ?? document.Name;
            modules.Add(new(document, SymbolUri.Module(workspaceRoot, declared), ParsingClientService.ModuleTypeOf(document), parse,
                ParsingClientService.ModuleSourceOf(document)));
        }

        // the libraries a project references are the primary project's: another project sees the standard library, and its own modules.
        var composition = _compositions.GetOrCompose(
            workspaceRoot, [.. modules.Select(module => (module.Module, module.Type, module.Parse))], resolver,
            projectName: primary || string.IsNullOrEmpty(name) ? null : name,
            implicitScope: options.Value.Workspace.SupportedLanguage.ImplicitDeclarationScope,
            libraries: primary ? referencedLibraries.SymbolsUnder(workspaceRoot) : null);

        var context = new RenameContext(composition.Resolver, composition.ScopeTree,
            [.. modules.Select(module => new RenameModule(module.Module, module.Parse.SyntaxTree!, module.Source))],
            new StaticEnvironment(options.Value.Environment.Is64Bit));
        return new RenameWorkspace.ProjectPart(context, modules);
    }
}

/// <summary>
/// The workspace, composed for renames (<see cref="IRenameService.ComposeAsync"/>), and the document of each of its modules: what a client asks of it is in
/// the text of the document, which for a program of the platform's BASIC is its lines and not the module they are the body of (<see cref="BasicProgramText"/>).
/// </summary>
internal sealed class RenameWorkspace
{
    /// <summary>A project, composed: what its modules resolve in, and the document of each.</summary>
    internal sealed record class ProjectPart(RenameContext Context, IReadOnlyList<DocumentModule> Modules);

    // the projects of the workspace, the primary one first; a document is asked of the first that has it, which is the project that loaded it first. A file that
    // several projects list is a module of each, and each project's copy of it resolves in that project.
    private readonly IReadOnlyList<ProjectPart> _parts;

    internal RenameWorkspace(RenameContext context, IReadOnlyList<DocumentModule> modules) : this([new ProjectPart(context, modules)])
    {
    }

    internal RenameWorkspace(IReadOnlyList<ProjectPart> parts)
    {
        _parts = parts;
    }

    // the project a module of a document is in, which its names resolve in.
    private ProjectPart PartOf(DocumentModule module) => _parts.First(part => part.Modules.Any(candidate => ReferenceEquals(candidate, module)));

    // the project a module (by its address, root#Name) is in.
    private ProjectPart? PartOf(Uri module) => _parts.FirstOrDefault(part => part.Modules.Any(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri));

    internal static RenameAnswer<T> NoName<T>() where T : class => new(null, new RenameRefusal(RenameRefusalKind.NoName, string.Empty).ToMessage());

    /// <summary>What a rename at a position of a document would rename.</summary>
    public RenameAnswer<PlaceholderRange> Prepare(Uri documentUri, Position position)
    {
        if (ModuleOf(documentUri) is not { } module)
        {
            return NoName<PlaceholderRange>();
        }

        var part = PartOf(module);
        var preparation = SymbolRename.Prepare(part.Context, module.Module, module.ToModulePosition(position));
        return preparation.Target is { } target
            ? new(new PlaceholderRange { Range = module.ToTextRange(target.Range), Placeholder = target.Name }, null)
            : new(null, Say(part, preparation.Refusal!));
    }

    /// <summary>The edits that rename the symbol named at a position of a document to <paramref name="newName"/>.</summary>
    public RenameAnswer<WorkspaceEdit> Rename(Uri documentUri, Position position, string newName)
    {
        if (ModuleOf(documentUri) is not { } module)
        {
            return NoName<WorkspaceEdit>();
        }

        var part = PartOf(module);
        var preparation = SymbolRename.Prepare(part.Context, module.Module, module.ToModulePosition(position));
        if (preparation.Target is not { } target)
        {
            return new(null, Say(part, preparation.Refusal!));
        }

        var plan = SymbolRename.Plan(part.Context, target, newName);
        if (!plan.IsValid)
        {
            return new(null, Say(part, plan.Refusal!));
        }

        // each document's edits name the version they are edits of, so that a client applies them to that text and to no other.
        var changes = plan.Edits
            .GroupBy(edit => edit.Module.AbsoluteUri)
            .Select(group => part.Modules.First(candidate => candidate.Module.AbsoluteUri == group.Key))
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
        return [.. SymbolRename.TargetsIn(PartOf(module).Context, module.Module, new SourceRange(start, end))
            .Select(target => (target.Name, module.ToTextRange(target.Range).Start))];
    }

    /// <summary>
    /// The names of the parameters the positional arguments of the calls in a range of a document are given to (<see cref="SDK.Semantics.Refactoring.ParameterHints"/>),
    /// each where its argument begins in the text of the document.
    /// </summary>
    public IReadOnlyList<(string Name, Position Position)> ParameterHints(Uri documentUri, LspRange range)
    {
        if (ModuleOf(documentUri) is not { } module)
        {
            return [];
        }

        var start = module.ToModulePosition(range.Start);
        var end = module.ToModulePosition(range.End);
        return [.. SDK.Semantics.Refactoring.ParameterHints.In(PartOf(module).Context, module.Module, new SourceRange(start, end))
            .Select(hint => (hint.Name, module.ToTextRange(new SourceRange(hint.Position, hint.Position)).Start))];
    }

    /// <summary>
    /// Where the symbol named at a position of a document is defined (<see cref="SymbolNavigation.DefinitionsAt"/>): each place, in the text of the document
    /// that declares it, with the name the navigation starts from.
    /// </summary>
    /// <returns>Each place; none where no name is written, where what the name refers to is not known, or where no document of the workspace declares it.</returns>
    public IReadOnlyList<LocationLink> Definitions(Uri documentUri, Position position) => Navigate(documentUri, position, SymbolNavigation.DefinitionsAt);

    /// <summary>
    /// Where the symbol named at a position of a document is declared (<see cref="SymbolNavigation.DeclarationAt"/>): the one place, in the text of the
    /// document that declares it, with the name the navigation starts from.
    /// </summary>
    /// <returns>The place; none where no name is written, where what the name refers to is not known, or where no document of the workspace declares it.</returns>
    public IReadOnlyList<LocationLink> Declarations(Uri documentUri, Position position) => Navigate(documentUri, position, SymbolNavigation.DeclarationAt);

    /// <summary>
    /// Where the type of the symbol named at a position of a document is defined (<see cref="SymbolNavigation.TypeDefinitionAt"/>).
    /// </summary>
    /// <returns>Each place; none for an intrinsic type or a type of a library, where no name is written, or where what the name refers to is not known.</returns>
    public IReadOnlyList<LocationLink> TypeDefinitions(Uri documentUri, Position position) => Navigate(documentUri, position, SymbolNavigation.TypeDefinitionAt);

    /// <summary>
    /// Where the symbol named at a position of a document is implemented (<see cref="SymbolNavigation.ImplementationsAt"/>).
    /// </summary>
    /// <returns>Each place; none for what no class implements, where no name is written, or where what the name refers to is not known.</returns>
    public IReadOnlyList<LocationLink> Implementations(Uri documentUri, Position position) => Navigate(documentUri, position, SymbolNavigation.ImplementationsAt);

    /// <summary>
    /// The item of the call hierarchy, or of the type hierarchy, named at a position of a document (<see cref="SymbolHierarchy.PrepareCalls"/>,
    /// <see cref="SymbolHierarchy.PrepareTypes"/>).
    /// </summary>
    /// <returns>The item; <see langword="null"/> where no procedure, or no class, of the workspace is named.</returns>
    public HierarchyItem? PrepareHierarchy(Uri documentUri, Position position, bool types)
        => ModuleOf(documentUri) is { } module
            ? types
                ? SymbolHierarchy.PrepareTypes(PartOf(module).Context, module.Module, module.ToModulePosition(position))
                : SymbolHierarchy.PrepareCalls(PartOf(module).Context, module.Module, module.ToModulePosition(position))
            : null;

    /// <summary>
    /// The item of a hierarchy whose symbol has the identity a client sent back (<see cref="SymbolHierarchy.ItemOf(RenameContext, SemanticId)"/>): in the
    /// project the symbol is in.
    /// </summary>
    public HierarchyItem? HierarchyItemOf(Uri symbol) => _parts.Select(part => SymbolHierarchy.ItemOf(part.Context, new SemanticId(symbol))).FirstOrDefault(item => item is not null);

    /// <summary>The procedures that call an item (<see cref="SymbolHierarchy.IncomingCalls"/>).</summary>
    public IReadOnlyList<HierarchyCall> IncomingCalls(HierarchyItem item) => ContextOf(item) is { } context ? SymbolHierarchy.IncomingCalls(context, item) : [];

    /// <summary>What an item calls (<see cref="SymbolHierarchy.OutgoingCalls"/>).</summary>
    public IReadOnlyList<HierarchyCall> OutgoingCalls(HierarchyItem item) => ContextOf(item) is { } context ? SymbolHierarchy.OutgoingCalls(context, item) : [];

    /// <summary>The classes an item's class implements (<see cref="SymbolHierarchy.Supertypes"/>).</summary>
    public IReadOnlyList<HierarchyItem> Supertypes(HierarchyItem item) => ContextOf(item) is { } context ? SymbolHierarchy.Supertypes(context, item) : [];

    /// <summary>The classes that implement an item's class (<see cref="SymbolHierarchy.Subtypes"/>).</summary>
    public IReadOnlyList<HierarchyItem> Subtypes(HierarchyItem item) => ContextOf(item) is { } context ? SymbolHierarchy.Subtypes(context, item) : [];

    private RenameContext? ContextOf(HierarchyItem item) => PartOf(item.Module)?.Context;

    /// <summary>
    /// Where an item of a hierarchy is declared, in the text of its document: the document, the whole declaration, and the name.
    /// </summary>
    /// <returns><see langword="null"/> for an item of no document the server holds.</returns>
    public (DocumentUri Uri, LspRange Range, LspRange SelectionRange)? Locate(HierarchyItem item)
        => PartOf(item.Module)?.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == item.Module.AbsoluteUri) is { } document
            ? (document.Document.Id.Uri, document.ToTextRange(item.Range), document.ToTextRange(item.SelectionRange))
            : null;

    /// <summary>A range of the module of an item, in the text of its document.</summary>
    public LspRange InTextOf(HierarchyItem item, SourceRange range)
        => PartOf(item.Module)?.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == item.Module.AbsoluteUri) is { } document
            ? document.ToTextRange(range)
            : new LspRange(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);

    /// <summary>
    /// Where the symbol named at a position of a document is referred to (<see cref="SymbolNavigation.ReferencesAt"/>): each name, in the text of its document.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">A position in the name, in the text of the document.</param>
    /// <param name="includeDeclaration">Whether the names its declarations declare are references too.</param>
    /// <returns>Each place; none where no name is written, or where what the name refers to is not known.</returns>
    public IReadOnlyList<Location> References(Uri documentUri, Position position, bool includeDeclaration)
        => [.. Navigate(documentUri, position, (context, module, at) => SymbolNavigation.ReferencesAt(context, module, at, includeDeclaration))
            .Select(link => new Location { Uri = link.TargetUri, Range = link.TargetSelectionRange })];

    /// <summary>
    /// The members a document declares whose references a client shows (<see cref="SymbolLenses.In"/>): each name, in the text of the document.
    /// </summary>
    /// <returns>Each member; none in a program of the platform's BASIC, whose procedure is the one its lines are the body of.</returns>
    public IReadOnlyList<(string Name, LspRange Range)> Lenses(Uri documentUri)
    {
        if (ModuleOf(documentUri) is not { } module
            || BasicProgramText.IsProgram(module.Document.Id.Uri.GetFileSystemPath())
            || PartOf(module).Context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.Module.AbsoluteUri) is not { } source)
        {
            return [];
        }

        return [.. SymbolLenses.In(source).Select(lens => (lens.Name, module.ToTextRange(lens.NameRange)))];
    }

    /// <summary>
    /// The names that refer to the member whose name is written at a position of a document (<see cref="SymbolLenses.ReferencesOf"/>), its declarations not
    /// included: each name, in the text of its document.
    /// </summary>
    public IReadOnlyList<Location> LensReferences(Uri documentUri, Position position)
    {
        if (ModuleOf(documentUri) is not { } module)
        {
            return [];
        }

        var part = PartOf(module);
        return [.. SymbolLenses.ReferencesOf(part.Context, module.Module, module.ToModulePosition(position))
            .Select(site => (Site: site, Document: part.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == site.Module.AbsoluteUri)))
            .Where(found => found.Document is not null)
            .Select(found => new Location { Uri = found.Document!.Document.Id.Uri, Range = found.Document.ToTextRange(found.Site.SelectionRange) })];
    }

    /// <summary>
    /// The names in a document that refer to the symbol named at a position of it (<strong>LSP 3.17</strong> §Document Highlights Request): what a rename of
    /// it would rename there (<see cref="SymbolNavigation.OccurrencesAt"/>), every procedure of a property included. Each is a <see cref="DocumentHighlightKind.Text"/>: which name reads the symbol and which assigns it
    /// is not told apart yet.
    /// </summary>
    public IReadOnlyList<DocumentHighlight> Highlights(Uri documentUri, Position position)
    {
        if (ModuleOf(documentUri) is not { } module
            || SymbolNavigation.OccurrencesAt(PartOf(module).Context, module.Module, module.ToModulePosition(position)) is not { } target)
        {
            return [];
        }

        return [.. target.Sites
            .Where(site => site.Module.AbsoluteUri == module.Module.AbsoluteUri)
            .Select(site => new DocumentHighlight { Range = module.ToTextRange(site.Range), Kind = DocumentHighlightKind.Text })];
    }

    /// <summary>
    /// What a hover over the name at a position of a document says of its symbol (<see cref="SymbolHover"/>), and where the name is written.
    /// </summary>
    /// <returns>The hover; <see langword="null"/> where no name is written, or where what the name refers to is not known.</returns>
    public (SymbolHover Hover, LspRange Range)? Hover(Uri documentUri, Position position)
        => ModuleOf(documentUri) is { } module && SymbolNavigation.SymbolAt(PartOf(module).Context, module.Module, module.ToModulePosition(position)) is { } named
            ? (SymbolHover.Of(named.Symbol), module.ToTextRange(named.Origin))
            : null;

    private IReadOnlyList<LocationLink> Navigate(
        Uri documentUri, Position position, Func<RenameContext, Uri, SourcePosition, SymbolNavigationTarget?> navigate)
    {
        if (ModuleOf(documentUri) is not { } module
            || navigate(PartOf(module).Context, module.Module, module.ToModulePosition(position)) is not { } target)
        {
            return [];
        }

        var origin = module.ToTextRange(target.Origin);
        var modules = PartOf(module).Modules;
        return [.. target.Sites
            .Select(site => (Site: site, Document: modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == site.Module.AbsoluteUri)))
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
        return _parts.SelectMany(part => part.Modules).FirstOrDefault(module => module.Document.Id.Uri == document);
    }

    // a position is named the way the document names it: its file, and the line of its text - for a program of the BASIC, the number it labels the line with.
    private static string Say(ProjectPart part, RenameRefusal refusal) => refusal.ToMessage((module, position) =>
        part.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is { } document
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
