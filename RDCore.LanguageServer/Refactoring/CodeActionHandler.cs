using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Serves <c>textDocument/codeAction</c> (<strong>LSP 3.17</strong> §Code Action Request): what can be done about the diagnostics of a range of a document, and
/// what can be renamed or extracted in it.
/// </summary>
/// <remarks>
/// <para>
/// A diagnostic whose fix is a rename (<see cref="DiagnosticFixes"/>) is offered as quick fixes: a rename to each name its analyzer suggests that can be made -
/// its edits are the action's - and a rename to a name the user chooses, which is a command the client runs (<see cref="RefactoringCommands.Rename"/>). A name
/// the range declares, or the name at the position of an empty range, is offered as a refactoring that renames it, the same command.
/// </para>
/// <para>
/// The statements of a selection that nothing refuses to extract are offered as a refactoring that extracts them as a procedure (<see cref="IExtractMethodService"/>).
/// A client that expects <see cref="SDK.Client.Refactoring"/> runs it as a command (<see cref="RefactoringCommands.Refactor"/>), and asks its user before it
/// applies it; any other is sent its edit, made with the defaults, when it changes nothing of what the code does that the user would have to accept.
/// </para>
/// <para>
/// A <c>GoSub</c> routine is offered, the same way, as a refactoring that converts it to a procedure: at a position on its label, on a <c>GoSub</c> that names
/// it or on one of its <c>Return</c>s, and for a selection whose extraction is refused for a <c>GoSub</c> to it or one of its <c>Return</c>s.
/// </para>
/// <para>
/// A workspace whose folder has a VB6 project and no project file of its own is offered, in every document, a source action that imports each VB6 project
/// (<c>rdcore.importProject</c>, <see cref="Workspace.ImportProjectCommand"/>).
/// </para>
/// <para>
/// An extraction and a conversion are the actions that are resolved (<c>resolveProvider = true</c>): a client that resolves an action's edit
/// (<strong>LSP 3.17</strong> §Code Action Resolve Request) is answered them without their edit, which is made and verified when it resolves the action, and
/// refused when the document has changed since. Any other client is answered every action complete.
/// </para>
/// </remarks>
internal sealed class CodeActionHandler(
    IRenameService rename, IWorkspaceDocumentService documents, IFileSystem fileSystem, IExtractMethodService extractMethod,
    IPlatformClientCapabilitiesService clientCapabilities, OmniSharp.Extensions.JsonRpc.ISerializer serializer) : CodeActionHandlerBase
{
    /// <summary>The kind of the action that renames a symbol: a refactoring (<c>refactor.rename</c>).</summary>
    public static readonly CodeActionKind RefactorRename = new("refactor.rename");

    /// <summary>
    /// The kind of the action that extracts the statements of a selection as a procedure (<see cref="RefactoringKinds.ExtractMethod"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorExtractMethod = new(RefactoringKinds.ExtractMethod);

    /// <summary>
    /// The kind of the action that converts a <c>GoSub</c> routine to a procedure (<see cref="RefactoringKinds.ConvertGoSub"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorConvertGoSub = new(RefactoringKinds.ConvertGoSub);

    /// <summary>The kind of the action that imports a VB6 project: a source action of the whole workspace (<c>source.rdcore.importProject</c>).</summary>
    public static readonly CodeActionKind SourceImportProject = new("source.rdcore.importProject");

    // LSP "RequestFailed": the request is valid, and is refused.
    private const int RequestFailedCode = -32803;

    // the member of an action's data that names the refactoring it resolves to.
    private const string ExtractionDataKey = "refactoring";

    // what the client said it can do, as it initialized: a rename's edits are sent in the form it applies.
    private ClientCapabilities? _client;

    // a client that sends an action's data back and resolves its edit is sent an extraction without its edit.
    private bool ResolvesEdits => _client?.TextDocument?.CodeAction.Value is { DataSupport: true, ResolveSupport.Properties: { } properties }
        && properties.Contains("edit");

    // a client that shows an action it cannot take, with why, is offered a refactoring that is refused.
    private bool ShowsDisabled => _client?.TextDocument?.CodeAction.Value is { DisabledSupport: true };

    public override async Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken cancellationToken)
    {
        var documentUri = request.TextDocument.Uri.ToUri();
        if (await rename.ComposeAsync(cancellationToken) is not { } workspace)
        {
            return new CommandOrCodeActionContainer();
        }

        var actions = new List<CodeAction>();
        var offered = new HashSet<Position>();
        foreach (var diagnostic in request.Context.Diagnostics)
        {
            var fixes = DiagnosticFixes.Of(diagnostic);

            // edits the analyzer made are the action's, as they are: they change only the document the diagnostic is in.
            if (fixes?.Edit is { Edits.Count: > 0 } editFix)
            {
                actions.Add(new CodeAction
                {
                    Title = editFix.Title,
                    Kind = CodeActionKind.QuickFix,
                    Diagnostics = new Container<Diagnostic>(diagnostic),
                    Edit = new WorkspaceEdit { Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>> { [request.TextDocument.Uri] = editFix.Edits } },
                    IsPreferred = true,
                });
            }

            if (fixes?.Rename is not { } fix || workspace.Prepare(documentUri, diagnostic.Range.Start).Value is not { } target)
            {
                continue;
            }

            foreach (var suggestion in fix.Suggestions)
            {
                // a rename whose edits of strings the user confirms is not offered, as an edit, to a client that can't ask them: the rename command, which the
                // server asks them for, still is. Its late-bound edits, which the user confirms too, are left out of the edit such a client is sent.
                if (workspace.Rename(documentUri, diagnostic.Range.Start, suggestion) is { Value: { } edit } answer
                    && (!answer.Asks(RenameWorkspace.InString) || _client.TakesChangeAnnotations())
                    && ForTheClient(edit) is { } applicable)
                {
                    actions.Add(new CodeAction
                    {
                        Title = RefactoringTitles.RenameTo(target.Placeholder, suggestion),
                        Kind = CodeActionKind.QuickFix,
                        Diagnostics = new Container<Diagnostic>(diagnostic),
                        Edit = applicable,
                        IsPreferred = true,
                    });
                }
            }

            actions.Add(RenameCommand(documentUri, target.Placeholder, target.Range.Start, CodeActionKind.QuickFix, diagnostic));
            offered.Add(target.Range.Start);
        }

        foreach (var (name, position) in workspace.Renamable(documentUri, request.Range).Where(renamable => !offered.Contains(renamable.Position)))
        {
            actions.Add(RenameCommand(documentUri, name, position, RefactorRename, diagnostic: null));
        }

        actions.AddRange(ImportProjectActions());

        // a client that asks for some kinds of actions only is answered those: a kind is asked for with the kinds under it.
        var only = request.Context.Only?.Select(kind => kind.ToString()).ToList();
        bool IsAsked(CodeActionKind kind) => only is not { Count: > 0 }
            || only.Any(asked => kind.ToString() == asked || kind.ToString().StartsWith(asked + ".", StringComparison.Ordinal));
        var range = new SourceRange(request.Range.Start.Line, request.Range.Start.Character, request.Range.End.Line, request.Range.End.Character);
        // the routine to convert: the one at the position of an empty range, or the first one a selection is refused for, for a GoSub to it or its Return.
        SourceRange? routine = range.IsEmpty ? range : null;
        if (!range.IsEmpty && (IsAsked(RefactorExtractMethod) || IsAsked(RefactorConvertGoSub)))
        {
            var (extraction, refused) = await RefactoringActionAsync(request.TextDocument.Uri, RefactoringKinds.ExtractMethod, range, cancellationToken);
            if (extraction is not null)
            {
                actions.Add(extraction);
            }

            string[] ofARoutine = [ExtractMethodService.RefusalCode(ExtractionRefusalKind.GoSubToOutside), ExtractMethodService.RefusalCode(ExtractionRefusalKind.ReturnOfAGoSubOutside)];
            routine = refused.FirstOrDefault(notice => ofARoutine.Contains(notice.Code))?.Location is { } location
                ? new SourceRange(location.Range.Start.Line, location.Range.Start.Character, location.Range.Start.Line, location.Range.Start.Character)
                : null;
        }

        if (routine is { } at && IsAsked(RefactorConvertGoSub)
            && (await RefactoringActionAsync(request.TextDocument.Uri, RefactoringKinds.ConvertGoSub, at, cancellationToken)).Action is { } conversion)
        {
            actions.Add(conversion);
        }

        return new CommandOrCodeActionContainer(actions.Where(action => IsAsked(action.Kind)).Select(action => new CommandOrCodeAction(action)));
    }

    // the action that extracts the statements of a selection, or converts a GoSub routine, offered when nothing refuses it, with the refusals when something
    // does. A client with a dialog of its own runs it as a command, which prepares the refactoring and applies it with its user's answers; any other is sent the
    // edit, with the defaults, when the refactoring changes nothing the user would have to accept. A refused one is offered disabled, with why, to a client that
    // shows it so, unless the range holds nothing it refactors.
    private async Task<(CodeAction? Action, IReadOnlyList<RefactorNotice> Refused)> RefactoringActionAsync(
        DocumentUri lspUri, string kind, SourceRange range, CancellationToken token)
    {
        var documentUri = lspUri.ToUri();
        var actionKind = new CodeActionKind(kind);
        var title = ExtractMethodService.Title(kind);

        // the version of the document the range belongs to, read before the refactoring is prepared: once the document changes, resolving it is refused.
        int? version = documents.TryGetDocument(documentUri, out var document) ? document.Version : null;
        var prepared = await extractMethod.PrepareAsync(kind, documentUri, range, token);
        if (prepared.Notices.Where(notice => notice.Kind is RefactorNoticeKind.Refusal).ToArray() is { Length: > 0 } refused)
        {
            return (ShowsDisabled && !ExtractMethodService.FindsNothing(refused[0])
                ? new CodeAction { Title = title, Kind = actionKind, Disabled = new CodeActionDisabled { Reason = refused[0].Message } }
                : null, refused);
        }

        if (clientCapabilities.Expects(capabilities => capabilities.Refactoring))
        {
            var prepare = new RefactorPrepareParams { Kind = kind, Uri = documentUri, Range = range };
            return (new CodeAction
            {
                Title = title,
                Kind = actionKind,
                Command = new Command { Name = RefactoringCommands.Refactor, Title = title, Arguments = new JArray(JToken.Parse(serializer.SerializeObject(prepare))) },
            }, []);
        }

        if (prepared.Notices.Any(notice => notice.Kind is RefactorNoticeKind.Change))
        {
            return (null, []);
        }

        if (ResolvesEdits)
        {
            return (new CodeAction
            {
                Title = title,
                Kind = actionKind,
                // a resolved action is recognized by its data: a client sends the action back as it holds it, and may drop a kind it does not know.
                Data = new JObject
                {
                    [ExtractionDataKey] = kind,
                    ["uri"] = lspUri.ToString(),
                    ["range"] = JToken.Parse(serializer.SerializeObject(new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                        range.Start.Line, range.Start.Character, range.End.Line, range.End.Character))),
                    ["version"] = version is { } number ? new JValue(number) : JValue.CreateNull(),
                },
            }, []);
        }

        var answer = await extractMethod.ApplyAsync(kind, documentUri, range, DefaultAnswers(), [], token);
        if (answer.Edit is null || ForTheClient(LspEditOf(answer)) is not { } applicable)
        {
            return (null, []);
        }

        return (new CodeAction { Title = title, Kind = actionKind, Edit = applicable }, []);
    }

    // the refactoring a client resolves: its edit, made with the defaults and verified, of the version of the document it was offered for. One that is refused,
    // or whose document has changed since, is answered an error that says why.
    private async Task<CodeAction> ResolveRefactoringAsync(CodeAction action, string kind, JObject offered, CancellationToken token)
    {
        if (offered["uri"]?.ToString() is not { Length: > 0 } uri || offered["range"] is not JObject offeredRange)
        {
            return action;
        }

        var lspRange = serializer.DeserializeObject<OmniSharp.Extensions.LanguageServer.Protocol.Models.Range>(offeredRange.ToString());
        var range = new SourceRange(lspRange.Start.Line, lspRange.Start.Character, lspRange.End.Line, lspRange.End.Character);
        int? version = offered["version"] is { Type: JTokenType.Integer } number ? number.Value<int>() : null;

        var answer = await extractMethod.ApplyAsync(kind, DocumentUri.From(uri).ToUri(), range, DefaultAnswers(), [], token);
        var refusal = answer.Edit is { } edit && edit.Documents.Any(document => document.Version != version) ? ExtractMethodService.DocumentChanged(kind) : answer.Refusal;
        if (refusal is not null || answer.Edit is null)
        {
            throw new RpcErrorException(RequestFailedCode, error: null!, refusal ?? string.Empty);
        }

        try
        {
            return new CodeAction { Title = action.Title, Kind = new CodeActionKind(kind), Data = action.Data, Edit = LspEditOf(answer).For(_client) };
        }
        catch (WorkspaceEditNotSupportedException unsupported)
        {
            throw new RpcErrorException(RequestFailedCode, error: null!, unsupported.Message);
        }
    }

    // what a client with no dialog extracts with: the defaults, but for the duplicates, which the user confirms each of where the client asks for change
    // annotations, and which are not replaced where it cannot.
    private IReadOnlyList<RefactorAnswer> DefaultAnswers()
        => _client.TakesChangeAnnotations() ? [] : [new RefactorAnswer { Id = ExtractMethodService.ReplaceDuplicatesChoice, Value = "false" }];

    // an extraction's edit as LSP writes it: the edits of each document, of the version the client said it last; those that replace a duplicate of the
    // selection are annotated as changes the user confirms, one annotation for each duplicate.
    private static WorkspaceEdit LspEditOf(ExtractMethodAnswer answer)
    {
        ChangeAnnotationIdentifier? AnnotationOf(Uri document, SourceRange range)
            => answer.Replacements.FirstOrDefault(replacement => replacement.Document.AbsoluteUri == document.AbsoluteUri && replacement.Ranges.Contains(range)) is { } replaced
                ? (ChangeAnnotationIdentifier)replaced.Id
                : null;

        return new WorkspaceEdit
        {
            DocumentChanges = new Container<WorkspaceEditDocumentChange>(answer.Edit!.Documents.Select(document => new WorkspaceEditDocumentChange(new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.From(document.Uri!), Version = document.Version },
                Edits = new TextEditContainer(document.Edits.Select(each =>
                {
                    var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(each.Range.Start.Line, each.Range.Start.Character, each.Range.End.Line, each.Range.End.Character);
                    return AnnotationOf(document.Uri!, each.Range) is { } annotation
                        ? new AnnotatedTextEdit { Range = range, NewText = each.NewText, AnnotationId = annotation }
                        : new TextEdit { Range = range, NewText = each.NewText };
                })),
            }))),
            ChangeAnnotations = answer.Replacements.Count == 0
                ? null
                : answer.Replacements.ToDictionary(
                    replacement => (ChangeAnnotationIdentifier)replacement.Id,
                    replacement => new ChangeAnnotation { Label = replacement.Label, NeedsConfirmation = true }),
        };
    }

    // the edit as the client applies it; an action whose edit the client can't apply is not offered - the rename command, which says why, still is.
    private WorkspaceEdit? ForTheClient(WorkspaceEdit edit)
    {
        try
        {
            return edit.For(_client);
        }
        catch (WorkspaceEditNotSupportedException)
        {
            return null;
        }
    }

    // the action that renames a symbol to a name the client asks its user for.
    private static CodeAction RenameCommand(DocumentUri document, string name, Position position, CodeActionKind kind, Diagnostic? diagnostic)
    {
        var title = RefactoringTitles.Rename(name);
        return new CodeAction
        {
            Title = title,
            Kind = kind,
            Diagnostics = diagnostic is null ? null : new Container<Diagnostic>(diagnostic),
            Command = new Command
            {
                Name = RefactoringCommands.Rename,
                Title = title,
                Arguments = new JArray(document.ToString(), JToken.FromObject(new { line = position.Line, character = position.Character })),
            },
        };
    }

    // a workspace whose folder has no project file of its own is offered to import each VB6 project in it.
    private IEnumerable<CodeAction> ImportProjectActions()
    {
        var root = documents.WorkspaceRoot;
        if (string.IsNullOrEmpty(root) || !fileSystem.Directory.Exists(root) || fileSystem.File.Exists(fileSystem.Path.Combine(root, ProjectFile.FileName)))
        {
            return [];
        }

        return fileSystem.Directory.EnumerateFiles(root, "*.vbp").Order(StringComparer.OrdinalIgnoreCase).Select(vbp =>
        {
            var title = VbpImportText.ImportTitle(fileSystem.Path.GetFileName(vbp));
            return new CodeAction
            {
                Title = title,
                Kind = SourceImportProject,
                Command = new Command
                {
                    Name = Workspace.ImportProjectCommand.CommandName,
                    Title = title,
                    Arguments = new JArray(DocumentUri.FromFileSystemPath(vbp).ToString()),
                },
            };
        });
    }

    public override async Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken)
        => request.Edit is null && request.Data is JObject offered
            && offered[ExtractionDataKey]?.ToString() is RefactoringKinds.ExtractMethod or RefactoringKinds.ConvertGoSub
            ? await ResolveRefactoringAsync(request, offered[ExtractionDataKey]!.ToString(), offered, cancellationToken)
            : request;

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(CodeActionCapability capability, ClientCapabilities clientCapabilities)
    {
        _client = clientCapabilities;
        return new()
        {
            CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix, RefactorRename, RefactorExtractMethod, RefactorConvertGoSub, SourceImportProject),
            ResolveProvider = true,
        };
    }
}
