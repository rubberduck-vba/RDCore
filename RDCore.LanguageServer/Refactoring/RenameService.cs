using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using RDCore.SDK.Forms;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.SDK.Model.Symbols;
using RDCore.Symbols;
using RDCore.Runtime.LanguageCore.Refactoring;
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
    /// <param name="client">
    /// What the client said it can do, as it initialized: a UserForm's control is renamed only for a client that takes the replacement of the form's .frx
    /// (<see cref="WorkspaceEditExtensions.TakesFileReplacement"/>). None when it is not known, which is a client that does not.
    /// </param>
    /// <returns>The name, or why it is not renamed.</returns>
    Task<RenameAnswer<PlaceholderRange>> PrepareAsync(Uri documentUri, Position position, CancellationToken token, ClientCapabilities? client = null);

    /// <summary>
    /// The edits that rename the symbol named at a position of a document.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">A position in the name, in the text of the document.</param>
    /// <param name="newName">The new name.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <param name="client">What the client said it can do, as it initialized, as <see cref="PrepareAsync"/> reads it.</param>
    /// <returns>
    /// The edits, of every document whose text changes, or why there are none. A UserForm's control is renamed in the form's .frx too: the new file is
    /// staged beside it (<see cref="StagedFiles"/>), and the edit puts it in place before it edits any text.
    /// </returns>
    Task<RenameAnswer<WorkspaceEdit>> RenameAsync(
        Uri documentUri, Position position, string newName, CancellationToken token, ClientCapabilities? client = null);
}

/// <summary>
/// What a rename request is answered with: the answer, or why there is none, said for the reader.
/// </summary>
/// <param name="Value">The answer.</param>
/// <param name="Refusal">Why there is none, in the language of the reader.</param>
/// <param name="Questions">
/// What a client that does not take change annotations asks its user before it is sent the edits that need confirming, one question for each annotation
/// (<see cref="RenameWorkspace.InString"/>, <see cref="RenameWorkspace.LateBoundBacked"/>, <see cref="RenameWorkspace.LateBound"/>); none when there are none.
/// </param>
internal sealed record class RenameAnswer<T>(T? Value, string? Refusal, IReadOnlyList<RenameQuestion>? Questions = null) where T : class
{
    /// <summary>
    /// Whether the user is asked to confirm the edits of an annotation.
    /// </summary>
    /// <param name="annotation">The annotation of the edits.</param>
    public bool Asks(ChangeAnnotationIdentifier annotation) => Questions?.Any(question => question.Annotation == annotation) == true;

    /// <summary>JSON-RPC "Request Failed" (<strong>LSP 3.17</strong> <c>ErrorCodes.RequestFailed</c>): the request was valid, and is refused.</summary>
    public const int RequestFailedCode = -32803;

    /// <summary>The answer, or the error response that says why there is none.</summary>
    public T ValueOrRefusal() => Value ?? throw new RpcErrorException(RequestFailedCode, error: null!, Refusal ?? string.Empty);
}

/// <summary>
/// What a user is asked before the edits of one annotation of a rename are sent, by a client that does not ask it itself.
/// </summary>
/// <param name="Annotation">The annotation of the edits.</param>
/// <param name="Message">The question, in the language of the reader.</param>
/// <param name="Rename">The answer that renames the name there too.</param>
/// <param name="Leave">The answer that renames the symbol and leaves those edits out.</param>
internal sealed record class RenameQuestion(ChangeAnnotationIdentifier Annotation, string Message, string Rename, string Leave);

/// <summary>
/// A lens above the name of a member a document declares, in the text of the document (<see cref="SymbolLenses.In(RenameContext, RenameModule)"/>).
/// </summary>
/// <param name="Range">The name of the member.</param>
/// <param name="Kind">What the lens counts or says.</param>
/// <param name="Implemented">
/// Of an <see cref="SymbolLensKind.Implements"/> lens, the interface and the member the procedure implements, and where the member is declared.
/// </param>
internal sealed record class WorkspaceLens(LspRange Range, SymbolLensKind Kind, (string Interface, string Member, Location Declaration)? Implemented = null);

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
    IWorkspaceProjects workspaceProjects,
    WorkspaceCompositionCache? compositions = null,
    StagedFiles? stagedFiles = null,
    ModuleLiveBranches? liveBranches = null) : IRenameService
{
    // the workspace as it was composed last, for as long as no document changed: a prepared rename, the rename and the code actions of a line are of one.
    private readonly WorkspaceCompositionCache _compositions = compositions ?? new();

    public async Task<RenameAnswer<PlaceholderRange>> PrepareAsync(
        Uri documentUri, Position position, CancellationToken token, ClientCapabilities? client = null)
        => await ComposeAsync(token) is { } workspace
            ? workspace.Prepare(documentUri, position, client.TakesFileReplacement())
            : RenameWorkspace.NoName<PlaceholderRange>();

    public async Task<RenameAnswer<WorkspaceEdit>> RenameAsync(Uri documentUri, Position position, string newName, CancellationToken token,
        ClientCapabilities? client = null)
    {
        var answer = await ComposeAsync(token) is { } workspace
            ? workspace.Rename(documentUri, position, newName, client.TakesFileReplacement())
            : RenameWorkspace.NoName<WorkspaceEdit>();
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
        List<RenameWorkspace.ProjectPart> parts = [await ComposeProjectAsync(documents.WorkspaceRoot, primary: true, workspaceProjects.Projects.FirstOrDefault()?.Project.ProjectInfo.Name, token)];
        foreach (var other in workspaceProjects.Projects.Skip(1))
        {
            parts.Add(await ComposeProjectAsync(other.Root, primary: false, other.Project.ProjectInfo.Name, token));
        }

        return new RenameWorkspace(parts)
        {
            IsOpen = documents.IsOpen, ProjectFileEdit = ProjectFileEditOf, Stage = stagedFiles is null ? null : stagedFiles.Stage,
        };
    }

    // the edit that renames the workspace's project of a name in its project file, where the text writes the name; none when the file cannot be read, or
    // writes no name.
    private TextDocumentEdit? ProjectFileEditOf(string project, string newName)
    {
        if (workspaceProjects.Projects.FirstOrDefault(candidate => string.Equals(candidate.Project.ProjectInfo.Name, project, StringComparison.OrdinalIgnoreCase)) is not { } owner)
        {
            return null;
        }

        var path = Path.Combine(owner.Root, ProjectFile.FileName);
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "📁 The project file {Path} could not be read: the project is renamed in its code only.", path);
            return null;
        }

        return ProjectFile.NameRangeIn(text) is { } range
            ? new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.FromFileSystemPath(path) },
                Edits = new TextEditContainer(new TextEdit
                {
                    Range = new LspRange(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character),
                    NewText = newName,
                }),
            }
            : null;
    }

    private async Task<RenameWorkspace.ProjectPart> ComposeProjectAsync(string root, bool primary, string? name, CancellationToken token)
    {
        var workspaceRoot = SymbolUri.Root(root);
        var modules = new List<RenameWorkspace.DocumentModule>();
        foreach (var document in documents.GetProjectDocuments(root))
        {
            var parse = await parsing.ParseDocumentAsync(document.Id.Uri.ToUri(), token);
            if (parse.SyntaxTree is null)
            {
                continue;
            }

            // the document's modules: its own, then each block's, each with its part of the parse and the document's text, whose positions they have.
            var source = ParsingClientService.ModuleSourceOf(document);
            foreach (var module in ParsedModules.Of(workspaceRoot, document.Id.Uri.ToUri(), parse, document.Name, ParsingClientService.ModuleTypeOf(document)))
            {
                if (module.Parse.SyntaxTree is not null)
                {
                    modules.Add(new(document, module.Uri, module.Kind, module.Parse, source) { Range = module.Range });
                }
            }
        }

        // the libraries a project references are the primary project's: another project sees the standard library, and its own modules.
        var projectName = string.IsNullOrEmpty(name) ? null : name;
        var implicitScope = options.Value.Workspace.SupportedLanguage.ImplicitDeclarationScope;
        var libraries = primary ? referencedLibraries.SymbolsUnder(workspaceRoot) : null;
        var documentHosts = modules.Where(module => module.Document.Super is not null && module.Range is null)
            .GroupBy(module => SymbolUri.ModuleNameOf(module.Module), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Document.Super!.Value, StringComparer.OrdinalIgnoreCase);
        var composition = _compositions.GetOrCompose(
            workspaceRoot, [.. modules.Select(module => (module.Module, module.Type, module.Parse))], resolver,
            projectName: projectName, implicitScope: implicitScope, libraries: libraries, documentHosts: documentHosts);

        // the project again, some of its modules parsed from other texts (by the absolute URI of each): what an edit of them would be composed as, which the
        // cache does not keep.
        WorkspaceComposition Recompose(IReadOnlyDictionary<string, SDK.Model.AST.ModuleParseResult> edited) => WorkspaceSymbolResolver.ComposeWithScopes(
            workspaceRoot, [.. modules.Select(module => (module.Module, module.Type, edited.GetValueOrDefault(module.Module.AbsoluteUri) ?? module.Parse))], resolver,
            projectName: projectName, implicitScope: implicitScope, libraries: libraries, documentHosts: documentHosts);

        var context = new RenameContext(composition.Resolver, composition.ScopeTree,
            [.. modules.Select(module => new RenameModule(module.Module, module.Parse.SyntaxTree!, module.Source, module.Parse.Designer))],
            new StaticEnvironment(options.Value.Environment.Is64Bit))
        {
            DeadTestOf = parse => liveBranches?.DeadTestFor(parse),
        };
        return new RenameWorkspace.ProjectPart(context, modules, Recompose);
    }
}

/// <summary>
/// The workspace, composed for renames (<see cref="IRenameService.ComposeAsync"/>), and the document of each of its modules: what a client asks of it is in
/// the text of the document, which for a program of the platform's BASIC is its lines and not the module they are the body of (<see cref="BasicProgramText"/>).
/// </summary>
internal sealed class RenameWorkspace
{
    /// <summary>
    /// A project, composed: what its modules resolve in, the document of each, and the project composed again with some of its modules parsed from other
    /// texts, by the absolute URI of each (<see langword="null"/> for a project that is composed of nothing else).
    /// </summary>
    internal sealed record class ProjectPart(
        RenameContext Context, IReadOnlyList<DocumentModule> Modules, Func<IReadOnlyDictionary<string, SDK.Model.AST.ModuleParseResult>, WorkspaceComposition>? Recompose = null);

    /// <summary>
    /// The project and the module of a document; <see langword="null"/> for a document of no project.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    internal (ProjectPart Part, DocumentModule Module)? DocumentOf(Uri documentUri) => ModuleOf(documentUri) is { } module ? (PartOf(module), module) : null;

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

    // whether the client has the document open, and so knows its version: an edit of a document it has not opened names no version.
    internal Func<TextDocumentIdentifier, bool> IsOpen { get; init; } = static _ => true;

    // the edit that renames the workspace's project of a name in its project file, given the project and its new name; none by default.
    internal Func<string, string, TextDocumentEdit?> ProjectFileEdit { get; init; } = static (_, _) => null;

    // writes a file's new content beside it, given the file, what it was made of and the content, for the edit to put in its place; none for a workspace
    // that writes no file, which renames no UserForm control.
    internal StageFile? Stage { get; init; }

    internal delegate StagedFile? StageFile(DocumentUri target, ReadOnlySpan<byte> original, ReadOnlySpan<byte> content);

    // the project a module of a document is in, which its names resolve in.
    private ProjectPart PartOf(DocumentModule module) => _parts.First(part => part.Modules.Any(candidate => ReferenceEquals(candidate, module)));

    // the project a module (by its address, root#Name) is in.
    private ProjectPart? PartOf(Uri module) => _parts.FirstOrDefault(part => part.Modules.Any(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri));

    internal static RenameAnswer<T> NoName<T>() where T : class => new(null, new RenameRefusal(RenameRefusalKind.NoName, string.Empty).ToMessage());

    /// <summary>What a rename at a position of a document would rename.</summary>
    public RenameAnswer<PlaceholderRange> Prepare(Uri documentUri, Position position, bool replacesFiles = false)
    {
        if (ModuleAt(documentUri, position) is not { } module)
        {
            return NoName<PlaceholderRange>();
        }

        var part = PartOf(module);
        var preparation = Prepared(part, module, position, replacesFiles);
        return preparation.Target is { } target
            ? new(new PlaceholderRange { Range = module.ToTextRange(target.Range), Placeholder = target.Name }, null)
            : new(null, Say(part, preparation.Refusal!));
    }

    // what a rename at a position renames: a UserForm's control only for a client that puts its .frx back when the edit fails.
    private static RenamePreparation Prepared(ProjectPart part, DocumentModule module, Position position, bool replacesFiles)
    {
        var preparation = SymbolRename.Prepare(part.Context, module.Module, module.ToModulePosition(position));
        if (preparation.Target is not { } target)
        {
            return preparation;
        }

        // a block's module is not a rename target: it is named in its header, and the VB_Name attribute and the name of the file are its file's own module's.
        if (part.Modules.Any(candidate => candidate.Range is not null && target.Symbols.Contains(new SemanticId(candidate.Module))))
        {
            return new(null, new RenameRefusal(RenameRefusalKind.NoName, target.Name));
        }

        return !replacesFiles && SymbolRename.IsUserFormControl(part.Context, target)
            ? new(null, new RenameRefusal(RenameRefusalKind.FileChangeNotUndoable, target.Name))
            : preparation;
    }

    /// <summary>The edits that rename the symbol named at a position of a document to <paramref name="newName"/>.</summary>
    public RenameAnswer<WorkspaceEdit> Rename(Uri documentUri, Position position, string newName, bool replacesFiles = false)
    {
        if (ModuleAt(documentUri, position) is not { } module)
        {
            return NoName<WorkspaceEdit>();
        }

        var part = PartOf(module);
        var preparation = Prepared(part, module, position, replacesFiles);
        if (preparation.Target is not { } target)
        {
            return new(null, Say(part, preparation.Refusal!));
        }

        var plan = SymbolRename.Plan(part.Context, target, newName);
        if (!plan.IsValid)
        {
            return new(null, Say(part, plan.Refusal!));
        }

        // a UserForm's control is named in its form's .frx, which the edit replaces before it edits any text: the file is set aside, the new one staged beside
        // it is put in its place, and the file set aside is deleted once every other change is made (StagedFile). No step renames a file over another, so a
        // client that undoes a failed edit puts the .frx back.
        StagedFile? frx = null;
        if (plan.Control is { } control)
        {
            var (staged, refusal) = ReplacedFrxOf(part, control.Form, control.Name, newName);
            if (staged is null)
            {
                return new(null, Say(part, refusal!));
            }

            frx = staged;
        }

        var changes = frx is null ? DocumentChangesOf(part, plan.Edits) : frx.Installing().Concat(DocumentChangesOf(part, plan.Edits));

        // the user confirms what a string says, and which member a late-bound access calls: a client that takes change annotations asks them; one that does
        // not is asked for, before it is sent the edit.
        var annotations = new Dictionary<ChangeAnnotationIdentifier, ChangeAnnotation>();
        var questions = new List<RenameQuestion>();
        var locate = Locator(part);
        if (plan.Edits.Where(edit => edit.IsInString).ToList() is [var firstString, ..] inStrings)
        {
            annotations[InString] = new ChangeAnnotation
            {
                Label = RefactoringTitles.RenameInStringLabel(),
                Description = RefactoringTitles.RenameInStringDescription(target.Name),
                NeedsConfirmation = true,
            };
            questions.Add(new(InString, RefactoringTitles.RenameInStringQuestion(target.Name, inStrings.Count, locate(firstString.Module, firstString.Range.Start)),
                RefactoringTitles.RenameInStringRename(), RefactoringTitles.RenameInStringLeave()));
        }

        // a late-bound access that what is known of the object backs is told apart from one that is only possible. A label, which is no symbol, has none.
        foreach (var (annotation, isBacked) in new[] { (LateBoundBacked, true), (LateBound, false) })
        {
            if (plan.Edits.Where(edit => edit.IsLateBound && edit.IsBacked == isBacked).ToList() is [var firstAccess, ..] lateBound
                && target.Symbols is [var renamed, ..])
            {
                var className = SymbolUri.ModuleNameOf(renamed.Uri);
                annotations[annotation] = new ChangeAnnotation
                {
                    Label = RefactoringTitles.RenameLateBoundLabel(className, isBacked),
                    Description = RefactoringTitles.RenameLateBoundDescription(target.Name, className, isBacked),
                    NeedsConfirmation = true,
                };
                var question = RefactoringTitles.RenameLateBoundQuestion(target.Name, className, isBacked, lateBound.Count,
                    locate(firstAccess.Module, firstAccess.Range.Start));
                questions.Add(new(annotation, question, RefactoringTitles.RenameLateBoundRename(), RefactoringTitles.RenameLateBoundLeave()));
            }
        }

        // a module's file named after it is renamed with it, after its text is edited (LSP 3.17 RenameFile); the module is named by its VB_Name, so a client
        // that does not rename files is sent the rest of the edit (RenameHandler).
        if (plan.Module is { } renamedModule && part.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == renamedModule.AbsoluteUri) is { } file
            && FileRenameOf(file, target.Name, newName) is { } renameFile)
        {
            // a designer's binary file (Dialog.frx), which its text names, is renamed with it, then its text names the new one. Neither file is renamed over
            // one that exists: the client refuses the edit, and the text does not name a file it was not renamed to.
            if (CompanionOf(file, newName) is { } companion)
            {
                renameFile = renameFile with { Options = new RenameFileOptions { Overwrite = false, IgnoreIfExists = false } };
                changes = changes
                    .Append(new WorkspaceEditDocumentChange(renameFile))
                    .Append(new WorkspaceEditDocumentChange(companion.Rename))
                    .Append(new WorkspaceEditDocumentChange(new TextDocumentEdit
                    {
                        TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = renameFile.NewUri },
                        Edits = new TextEditContainer(companion.Names.Select(range => new TextEdit { Range = file.ToTextRange(range), NewText = companion.NewName })),
                    }));
            }
            else
            {
                changes = changes.Append(new WorkspaceEditDocumentChange(renameFile));
            }
        }

        // a project is renamed in its project file too, where the file writes its name (ProjectFile.NameRangeIn).
        if (plan.Project is { } project && ProjectFileEdit(project, newName) is { } projectFile)
        {
            changes = changes.Append(new WorkspaceEditDocumentChange(projectFile));
        }

        if (frx is not null)
        {
            changes = changes.Append(frx.Removing());
        }

        var workspaceEdit = new WorkspaceEdit
        {
            DocumentChanges = new Container<WorkspaceEditDocumentChange>(changes),
            ChangeAnnotations = annotations.Count == 0 ? null : annotations,
        };
        return new(workspaceEdit, null, questions.Count == 0 ? null : questions);
    }

    // the file of that name in the folder of a document, as the client spells the document's URI (its drive letter, its escapes).
    private static DocumentUri Sibling(DocumentUri document, string fileName)
        => DocumentUri.From(new Uri(document.ToUri(), Uri.EscapeDataString(fileName)));

    // the file of a module, when its name is the module's, renamed to the new name; a file of that name that is there already is left as it is, and so is the
    // module's file.
    private static RenameFile? FileRenameOf(DocumentModule module, string oldName, string newName)
    {
        var path = module.Document.Id.Uri.GetFileSystemPath();
        if (!string.Equals(Path.GetFileNameWithoutExtension(path), oldName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new RenameFile
        {
            OldUri = module.Document.Id.Uri,
            NewUri = Sibling(module.Document.Id.Uri, newName + Path.GetExtension(path)),
            Options = new RenameFileOptions { Overwrite = false, IgnoreIfExists = true },
        };
    }

    // the form's .frx with the control renamed in it (MSFormsWriter), staged beside it; or why it is not: a workspace that writes no file, a .frx the form
    // does not name beside it or the server could not read, one that changed since it was read, or one the writer does not rename the control in.
    private (StagedFile? Staged, RenameRefusal? Refusal) ReplacedFrxOf(ProjectPart part, Uri form, string name, string newName)
    {
        var module = part.Modules.First(candidate => candidate.Module.AbsoluteUri == form.AbsoluteUri);
        var reference = CompanionNamedBy(module.Source);
        if (Stage is not { } stage || reference is null || Path.GetFileName(reference.FileName) != reference.FileName || module.Document.Companion.IsEmpty)
        {
            return (null, new(RenameRefusalKind.UserFormControl, name));
        }

        byte[] renamed;
        try
        {
            renamed = MSFormsWriter.RenameControl(module.Document.Companion.Span, name, newName);
        }
        catch (Exception refused) when (refused is ArgumentException or NotSupportedException or MSFormsFormatException)
        {
            return (null, new(RenameRefusalKind.UserFormControlNotWritten, name, refused.Message));
        }

        return stage(Sibling(module.Document.Id.Uri, reference.FileName), module.Document.Companion.Span, renamed) is { } staged
            ? (staged, null)
            : (null, new(RenameRefusalKind.UserFormControl, name));
    }

    // the binary file of a designer's file (a form's .frx, a user control's .ctx), when the designer's text names it as the file of the same name
    // (OleObjectBlob = "Dialog.frx":0000): its rename, and where the text names it, in the text of the module.
    private static (RenameFile Rename, IReadOnlyList<SDK.Model.Source.SourceRange> Names, string NewName)? CompanionOf(DocumentModule module, string newName)
    {
        var path = module.Document.Id.Uri.GetFileSystemPath();
        if (!FormDesigner.CompanionExtensions.TryGetValue(Path.GetExtension(path), out var extension)
            || CompanionNamedBy(module.Source) is not { } named
            || !string.Equals(named.FileName, Path.GetFileNameWithoutExtension(path) + extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var companion = named.FileName;
        var names = new List<SDK.Model.Source.SourceRange>();
        var lines = module.Source.Split('\n');
        for (var line = 0; line < lines.Length && !lines[line].TrimStart().StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase); line++)
        {
            for (var at = lines[line].IndexOf($"\"{companion}\"", StringComparison.OrdinalIgnoreCase); at >= 0;
                at = lines[line].IndexOf($"\"{companion}\"", at + companion.Length + 2, StringComparison.OrdinalIgnoreCase))
            {
                names.Add(new SDK.Model.Source.SourceRange(line, at + 1, line, at + 1 + companion.Length));
            }
        }

        if (names.Count == 0)
        {
            return null;
        }

        return (new RenameFile
        {
            OldUri = Sibling(module.Document.Id.Uri, companion),
            NewUri = Sibling(module.Document.Id.Uri, newName + extension),
            Options = new RenameFileOptions { Overwrite = false, IgnoreIfExists = false },
        }, names, newName + extension);
    }

    // a designer block that is not terminated names no companion a rename can change.
    private static FormDesignerReference? CompanionNamedBy(string source)
    {
        try
        {
            return FormDesigner.CompanionOf(source);
        }
        catch (FormDesignerFormatException)
        {
            return null;
        }
    }

    // each document's edits name the version they are edits of, so that a client that takes documentChanges applies them to that text and to no other; one
    // that does not is sent them as changes, which name no version (WorkspaceEditExtensions.For). A document the client has not opened has no version the
    // client knows, and its edits name none: they are of the file as it is on disk (LSP 3.17 OptionalVersionedTextDocumentIdentifier).
    // The edits of all the modules of a document are one edit of it: two edits that name one document and version are a hazard to a client.
    private IEnumerable<WorkspaceEditDocumentChange> DocumentChangesOf(ProjectPart part, IReadOnlyList<RenameEdit> edits) => edits
        .Select(edit => (Edit: edit, Module: part.Modules.First(candidate => candidate.Module.AbsoluteUri == edit.Module.AbsoluteUri)))
        .GroupBy(edited => edited.Module.Document.Id.Uri)
        .Select(group => new WorkspaceEditDocumentChange(new TextDocumentEdit
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
            {
                Uri = group.First().Module.Document.Id.Uri,
                Version = IsOpen(group.First().Module.Document.Id) ? group.First().Module.Document.Version : null,
            },
            Edits = new TextEditContainer(group
                .Select(edited => AnnotationOf(edited.Edit) is { } annotation
                    ? new AnnotatedTextEdit { Range = edited.Module.ToTextRange(edited.Edit.Range), NewText = edited.Edit.NewText, AnnotationId = annotation }
                    : new TextEdit { Range = edited.Module.ToTextRange(edited.Edit.Range), NewText = edited.Edit.NewText })),
        }));

    /// <summary>
    /// The parameter declared at a position of a document, and the edits that remove it with its argument from every call (<see cref="ParameterRemoval"/>), or
    /// why they are not made; <see langword="null"/> where no parameter is declared.
    /// </summary>
    public (string Name, RenameAnswer<WorkspaceEdit> Answer)? RemoveParameter(Uri documentUri, Position position)
    {
        if (ModuleAt(documentUri, position) is not { } module)
        {
            return null;
        }

        var part = PartOf(module);
        if (ParameterRemoval.Prepare(part.Context, module.Module, module.ToModulePosition(position)).Target is not { } target)
        {
            return null;
        }

        var plan = ParameterRemoval.Plan(part.Context, target);
        return (target.Name, plan.IsValid
            ? new(new WorkspaceEdit { DocumentChanges = new Container<WorkspaceEditDocumentChange>(DocumentChangesOf(part, plan.Edits)) }, null)
            : new(null, plan.Refusal!.ToMessage(Locator(part))));
    }

    private static ChangeAnnotationIdentifier? AnnotationOf(RenameEdit edit)
        => edit.IsInString ? InString : edit.IsLateBound ? edit.IsBacked ? LateBoundBacked : LateBound : null;

    /// <summary>
    /// The change annotation of a rename's edits of the strings a library calls the symbol by (<see cref="RenameEdit.IsInString"/>), which the user confirms; a
    /// client with nobody to ask is sent none of them.
    /// </summary>
    internal static readonly ChangeAnnotationIdentifier InString = "rdcore.rename.inString";

    /// <summary>
    /// The change annotation of a rename's edits of the members accessed late-bound that may be the symbol (<see cref="RenameEdit.IsLateBound"/>), where
    /// nothing known of the object rules the symbol's class out, which the user confirms; a client with nobody to ask is sent none of them.
    /// </summary>
    internal static readonly ChangeAnnotationIdentifier LateBound = "rdcore.rename.lateBound";

    /// <summary>
    /// The change annotation of a rename's edits of the members accessed late-bound where what is known of the object backs the symbol
    /// (<see cref="RenameEdit.IsBacked"/>), which the user confirms; a client with nobody to ask is sent none of them.
    /// </summary>
    internal static readonly ChangeAnnotationIdentifier LateBoundBacked = "rdcore.rename.lateBound.backed";


    /// <summary>
    /// What can be renamed in a range of a document (<see cref="SymbolRename.TargetsIn"/>): each name, and where it begins in the text of the document.
    /// </summary>
    public IReadOnlyList<(string Name, Position Position)> Renamable(Uri documentUri, LspRange range)
        => [.. ModulesOf(documentUri).SelectMany(module =>
            SymbolRename.TargetsIn(PartOf(module).Context, module.Module,
                new SourceRange(module.ToModulePosition(range.Start), module.ToModulePosition(range.End)))
                .Select(target => (target.Name, module.ToTextRange(target.Range).Start)))];

    /// <summary>
    /// The inlay hints of a range of a document that the settings show (<see cref="SourceInlayHints"/>), each at its place in the text of the document; a
    /// hint of what the module of a program adds to its text, which the document does not have, is not one of them.
    /// </summary>
    public IReadOnlyList<InlayHint> InlayHints(Uri documentUri, LspRange range, SdkInlayHintOptions sites)
        => [.. ModulesOf(documentUri).SelectMany(module => global::RDCore.LanguageServer.Services.Refactoring.InlayHints
            .In(PartOf(module).Context, module.Module, new SourceRange(module.ToModulePosition(range.Start), module.ToModulePosition(range.End)), sites)
            .Where(hint => module.IsInText(hint.Position))
            .Select(hint => hint.ToInlayHint(module.ToTextRange(new SourceRange(hint.Position, hint.Position)).Start)))];

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
        => ModuleAt(documentUri, position) is { } module
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
    {
        if (PartOf(item.Module)?.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == item.Module.AbsoluteUri) is not { } document)
        {
            return null;
        }

        // a module declares itself nowhere in its text: it is the whole of it, named by its VB_Name, as the document's outline has it.
        var (range, selection) = item.Symbol is VBModuleSymbol && document.Parse.SyntaxTree is { } tree
            ? DocumentOutline.ModuleRangesOf(tree)
            : (item.Range, item.SelectionRange);
        return (document.Document.Id.Uri, document.ToTextRange(range), document.ToTextRange(selection));
    }

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
    /// <returns>
    /// Each place; none where no name is written, or where what the name refers to is not known. Incomplete: what the user is told when a name whose meaning is
    /// not known could refer to the symbol too (<see cref="SymbolNavigationTarget.IncompleteMessage"/>), with its place named as a rename names it.
    /// </returns>
    public (IReadOnlyList<Location> Locations, string? Incomplete) References(Uri documentUri, Position position, bool includeDeclaration)
    {
        if (ModuleAt(documentUri, position) is not { } module
            || SymbolNavigation.ReferencesAt(PartOf(module).Context, module.Module, module.ToModulePosition(position), includeDeclaration) is not { } target)
        {
            return ([], null);
        }

        var part = PartOf(module);
        return ([.. Links(module, target).Select(link => new Location { Uri = link.TargetUri, Range = link.TargetSelectionRange })], target.IncompleteMessage(Locator(part)));
    }

    /// <summary>
    /// The lenses of the members a document declares (<see cref="SymbolLenses.In(RenameContext, RenameModule)"/>): each at the member's name, in the text of
    /// the document.
    /// </summary>
    /// <returns>Each lens; none in a program of the platform's BASIC, whose procedure is the one its lines are the body of.</returns>
    public IReadOnlyList<WorkspaceLens> Lenses(Uri documentUri)
    {
        var lenses = new List<WorkspaceLens>();
        foreach (var module in ModulesOf(documentUri))
        {
            if (BasicProgramText.IsProgram(module.Document.Id.Uri.GetFileSystemPath())
                || PartOf(module).Context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.Module.AbsoluteUri) is not { } source)
            {
                continue;
            }

            foreach (var lens in SymbolLenses.In(PartOf(module).Context, source))
            {
                if (lens.Implemented is { } implemented)
                {
                    // a member of an interface no document of the workspace holds has nowhere to go to.
                    if (LocationsOf([implemented.Declaration]) is [var declaration])
                    {
                        lenses.Add(new WorkspaceLens(module.ToTextRange(lens.NameRange), lens.Kind, (implemented.Interface, implemented.Member, declaration)));
                    }

                    continue;
                }

                lenses.Add(new WorkspaceLens(module.ToTextRange(lens.NameRange), lens.Kind));
            }
        }

        return lenses;
    }

    /// <summary>
    /// The names that use the member whose name is written at a position of a document (<see cref="SymbolLenses.ReferencesOf"/>), its declarations not
    /// included, nor the result a Function or a Property Get assigns or reads in its own body: each name, in the text of its document.
    /// </summary>
    public IReadOnlyList<Location> LensReferences(Uri documentUri, Position position)
        => ModuleAt(documentUri, position) is { } module
            ? LocationsOf(SymbolLenses.ReferencesOf(PartOf(module).Context, module.Module, module.ToModulePosition(position)))
            : [];

    /// <summary>
    /// The procedures that implement the member whose name is written at a position of a document (<see cref="SymbolLenses.ImplementationsOf"/>), as Go to
    /// Implementation finds them: each name, in the text of its document.
    /// </summary>
    public IReadOnlyList<Location> LensImplementations(Uri documentUri, Position position)
        => ModuleAt(documentUri, position) is { } module
            ? LocationsOf(SymbolLenses.ImplementationsOf(PartOf(module).Context, module.Module, module.ToModulePosition(position)))
            : [];

    // each site at its name, in the text of its document; none of a document no project of the workspace holds.
    private IReadOnlyList<Location> LocationsOf(IEnumerable<SymbolSite> sites)
        => [.. sites
            .Select(site => (Site: site,
                Document: PartOf(site.Module)?.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == site.Module.AbsoluteUri)))
            .Where(found => found.Document is not null)
            .Select(found => new Location { Uri = found.Document!.Document.Id.Uri, Range = found.Document.ToTextRange(found.Site.SelectionRange) })];

    /// <summary>
    /// The names in a document that refer to the symbol named at a position of it (<strong>LSP 3.17</strong> §Document Highlights Request): what a rename of
    /// it would rename there (<see cref="SymbolNavigation.OccurrencesAt"/>), every procedure of a property included. Each is a <see cref="DocumentHighlightKind.Text"/>: which name reads the symbol and which assigns it
    /// is not told apart yet.
    /// </summary>
    public IReadOnlyList<DocumentHighlight> Highlights(Uri documentUri, Position position)
    {
        if (ModuleAt(documentUri, position) is not { } module
            || SymbolNavigation.OccurrencesAt(PartOf(module).Context, module.Module, module.ToModulePosition(position)) is not { } target)
        {
            return [];
        }

        // the names of the symbol in every module of the document, a block's as well as its own.
        var modules = ModulesOf(documentUri);
        return [.. target.Sites
            .Select(site => (Site: site, Module: modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == site.Module.AbsoluteUri)))
            .Where(found => found.Module is not null)
            .Select(found => new DocumentHighlight { Range = found.Module!.ToTextRange(found.Site.Range), Kind = DocumentHighlightKind.Text })];
    }

    /// <summary>
    /// The variables of a range of a document whose values a client shows while the program is stopped (<strong>LSP 3.17</strong> §Inline Value Request): the
    /// names of variables written in the procedure the program is stopped in (<see cref="InlineValueLookups.In"/>), each at its place in the text of the
    /// document, and none of what the module of a program adds to its text.
    /// </summary>
    public IReadOnlyList<InlineValueVariableLookup> InlineValues(Uri documentUri, LspRange range, LspRange stoppedLocation)
    {
        // the procedure the program is stopped in is in the module the stop is in.
        if (ModuleAt(documentUri, stoppedLocation.End) is not { } module)
        {
            return [];
        }

        // the line a client shows the values at is the one the range of the stop ends on.
        var within = new SourceRange(module.ToModulePosition(range.Start), module.ToModulePosition(range.End));
        return [.. InlineValueLookups.In(PartOf(module).Context, module.Module, within, module.ToModulePosition(stoppedLocation.End))
            .Where(lookup => module.IsInText(lookup.Range.Start))
            .Select(lookup => new InlineValueVariableLookup { Range = module.ToTextRange(lookup.Range), VariableName = lookup.Name, CaseSensitiveLookup = false })];
    }

    /// <summary>
    /// The names a client edits as one while the user types the name at a position of a document (<strong>LSP 3.17</strong> §Linked Editing Range): each
    /// place its procedure writes the local variable, the parameter or the line label named there (<see cref="LinkedEditingLookups.At"/>).
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the document's text.</param>
    /// <returns>The ranges; none where no such name is written, or where one of them is not in the text of the document.</returns>
    public LinkedEditingRanges LinkedEditing(Uri documentUri, Position position)
    {
        if (ModuleAt(documentUri, position) is not { } module)
        {
            return new LinkedEditingRanges { Ranges = new Container<LspRange>() };
        }

        var ranges = LinkedEditingLookups.At(PartOf(module).Context, module.Module, module.ToModulePosition(position));
        return new LinkedEditingRanges
        {
            Ranges = ranges.All(range => module.IsInText(range.Start))
                ? new Container<LspRange>(ranges.Select(range => module.ToTextRange(range)))
                : new Container<LspRange>(),
        };
    }

    /// <summary>
    /// What a hover over the name at a position of a document says of its symbol (<see cref="SymbolHover"/>), and where the name is written.
    /// </summary>
    /// <returns>The hover; <see langword="null"/> where no name is written, or where what the name refers to is not known.</returns>
    public (SymbolHover Hover, LspRange Range)? Hover(Uri documentUri, Position position)
        => SymbolAt(documentUri, position) is { } named ? (SymbolHover.Of(named.Symbol), named.Range) : null;

    /// <summary>
    /// The symbol the name at a position of a document refers to, and where the name is written.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the document's text.</param>
    /// <returns>The symbol; <see langword="null"/> where no name is written, or where what the name refers to is not known.</returns>
    public (Symbol Symbol, LspRange Range)? SymbolAt(Uri documentUri, Position position)
        => ModuleAt(documentUri, position) is { } module
            && RenameBinding.SymbolAt(PartOf(module).Context, module.Module, module.ToModulePosition(position)) is { } named
            ? (named.Symbol, module.ToTextRange(named.Origin))
            : null;

    private IReadOnlyList<LocationLink> Navigate(
        Uri documentUri, Position position, Func<RenameContext, Uri, SourcePosition, SymbolNavigationTarget?> navigate)
    {
        if (ModuleAt(documentUri, position) is not { } module
            || navigate(PartOf(module).Context, module.Module, module.ToModulePosition(position)) is not { } target)
        {
            return [];
        }

        return Links(module, target);
    }

    private IReadOnlyList<LocationLink> Links(DocumentModule module, SymbolNavigationTarget target)
    {
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

    // a document is the same document however its URI was written: a client writes the drive and its colon as it likes (c%3A, C:), as LSP lets it. A
    // document's modules are its own first, then each block's.
    private DocumentModule? ModuleOf(Uri documentUri) => ModulesOf(documentUri).FirstOrDefault();

    private IReadOnlyList<DocumentModule> ModulesOf(Uri documentUri)
    {
        var document = DocumentUri.From(documentUri);
        return _parts.Select(part => part.Modules.Where(module => module.Document.Id.Uri == document).ToList()).FirstOrDefault(found => found.Count > 0) ?? [];
    }

    // the module a position of a document is in: the block whose lines hold it, its header and end included, or else the document's own module.
    private DocumentModule? ModuleAt(Uri documentUri, Position position)
    {
        var modules = ModulesOf(documentUri);
        return modules.FirstOrDefault(module => module.Holds(module.ToModulePosition(position))) ?? modules.FirstOrDefault();
    }

    // a position is named the way the document names it: its file, and the line of its text - for a program of the BASIC, the number it labels the line with.
    private static string Say(ProjectPart part, RenameRefusal refusal) => refusal.ToMessage(Locator(part));

    internal static Func<Uri, SourcePosition, string> Locator(ProjectPart part) => (module, position) =>
        part.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is { } document
            ? document.Locate(position)
            : $"{SymbolUri.ModuleNameOf(module)} {position.Line + 1}:{position.Character + 1}";

    /// <summary>A document, and the module it is.</summary>
    internal sealed record class DocumentModule(WorkspaceDocument Document, Uri Module, ModuleType Type, SDK.Model.AST.ModuleParseResult Parse, string Source)
    {
        // the lines of a block's module, from its header to its end; null for the document's own module, which is every other line.
        public SourceRange? Range { get; init; }

        public bool Holds(SourcePosition position) => Range is { } range && position.Line >= range.Start.Line && position.Line <= range.End.Line;

        private bool IsProgram => BasicProgramText.IsProgram(Document.Id.Uri.GetFileSystemPath());

        public SourcePosition ToModulePosition(Position position)
            => new(IsProgram ? position.Line + BasicProgramText.HeaderLines : position.Line, position.Character);

        // whether a position of the module is in the text of the document: a program has no header and no footer of its own, which its module adds.
        public bool IsInText(SourcePosition position)
            => !IsProgram || position.Line >= BasicProgramText.HeaderLines && position.Line < Source.Count(character => character == '\n') - 1;

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
