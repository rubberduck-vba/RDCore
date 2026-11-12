using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Services.Refactoring;
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
/// A workspace whose folder has a VB6 project and no project file of its own is offered, in every document, a source action that imports each VB6 project
/// (<c>rdcore.importProject</c>, <see cref="Workspace.ImportProjectCommand"/>).
/// </para>
/// <para>
/// An extraction is the one action that is resolved (<c>resolveProvider = true</c>): a client that resolves an action's edit (<strong>LSP 3.17</strong> §Code
/// Action Resolve Request) is answered the extraction without its edit, which is made and verified when it resolves the action, and refused when the document
/// has changed since. Any other client is answered every action complete.
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
        if (IsAsked(RefactorExtractMethod) && await ExtractMethodActionAsync(request, cancellationToken) is { } extraction)
        {
            actions.Add(extraction);
        }

        return new CommandOrCodeActionContainer(actions.Where(action => IsAsked(action.Kind)).Select(action => new CommandOrCodeAction(action)));
    }

    // the action that extracts the statements of a selection, offered when nothing refuses it. A client with a dialog of its own runs it as a command, which
    // prepares the refactoring and applies it with its user's answers; any other is sent the edit, with the defaults, when the extraction changes nothing the
    // user would have to accept.
    private async Task<CodeAction?> ExtractMethodActionAsync(CodeActionParams request, CancellationToken token)
    {
        var range = new SourceRange(request.Range.Start.Line, request.Range.Start.Character, request.Range.End.Line, request.Range.End.Character);
        if (range.IsEmpty)
        {
            return null;
        }

        var documentUri = request.TextDocument.Uri.ToUri();

        // the version of the document the range belongs to, read before the extraction is prepared: once the document changes, resolving the extraction is refused.
        int? version = documents.TryGetDocument(documentUri, out var document) ? document.Version : null;
        var prepared = await extractMethod.PrepareAsync(documentUri, range, token);
        if (prepared.Notices.Any(notice => notice.Kind is RefactorNoticeKind.Refusal))
        {
            return null;
        }

        var title = ExtractMethodService.Title();
        if (clientCapabilities.Expects(capabilities => capabilities.Refactoring))
        {
            var prepare = new RefactorPrepareParams { Kind = RefactoringKinds.ExtractMethod, Uri = documentUri, Range = range };
            return new CodeAction
            {
                Title = title,
                Kind = RefactorExtractMethod,
                Command = new Command { Name = RefactoringCommands.Refactor, Title = title, Arguments = new JArray(JToken.Parse(serializer.SerializeObject(prepare))) },
            };
        }

        if (prepared.Notices.Any(notice => notice.Kind is RefactorNoticeKind.Change))
        {
            return null;
        }

        if (ResolvesEdits)
        {
            return new CodeAction
            {
                Title = title,
                Kind = RefactorExtractMethod,
                // a resolved action is recognized by its data: a client sends the action back as it holds it, and may drop a kind it does not know.
                Data = new JObject
                {
                    [ExtractionDataKey] = RefactoringKinds.ExtractMethod,
                    ["uri"] = request.TextDocument.Uri.ToString(),
                    ["range"] = JToken.Parse(serializer.SerializeObject(request.Range)),
                    ["version"] = version is { } number ? new JValue(number) : JValue.CreateNull(),
                },
            };
        }

        if ((await extractMethod.ApplyAsync(documentUri, range, [], [], token)).Edit is not { } edit || ForTheClient(LspEditOf(edit)) is not { } applicable)
        {
            return null;
        }

        return new CodeAction { Title = title, Kind = RefactorExtractMethod, Edit = applicable };
    }

    // the extraction a client resolves: its edit, made with the defaults and verified, of the version of the document it was offered for. One that is refused,
    // or whose document has changed since, is answered an error that says why.
    private async Task<CodeAction> ResolveExtractionAsync(CodeAction action, JObject offered, CancellationToken token)
    {
        if (offered["uri"]?.ToString() is not { Length: > 0 } uri || offered["range"] is not JObject offeredRange)
        {
            return action;
        }

        var lspRange = serializer.DeserializeObject<OmniSharp.Extensions.LanguageServer.Protocol.Models.Range>(offeredRange.ToString());
        var range = new SourceRange(lspRange.Start.Line, lspRange.Start.Character, lspRange.End.Line, lspRange.End.Character);
        int? version = offered["version"] is { Type: JTokenType.Integer } number ? number.Value<int>() : null;

        var answer = await extractMethod.ApplyAsync(DocumentUri.From(uri).ToUri(), range, [], [], token);
        var refusal = answer.Edit is { } edit && edit.Documents.Any(document => document.Version != version) ? ExtractMethodService.DocumentChanged() : answer.Refusal;
        if (refusal is not null || answer.Edit is null)
        {
            throw new RpcErrorException(RequestFailedCode, error: null!, refusal ?? string.Empty);
        }

        try
        {
            return new CodeAction { Title = action.Title, Kind = RefactorExtractMethod, Data = action.Data, Edit = LspEditOf(answer.Edit).For(_client) };
        }
        catch (WorkspaceEditNotSupportedException unsupported)
        {
            throw new RpcErrorException(RequestFailedCode, error: null!, unsupported.Message);
        }
    }

    // a refactoring's edit as LSP writes it: the edits of each document, of the version the client said it last.
    private static WorkspaceEdit LspEditOf(RefactorWorkspaceEdit edit) => new()
    {
        DocumentChanges = new Container<WorkspaceEditDocumentChange>(edit.Documents.Select(document => new WorkspaceEditDocumentChange(new TextDocumentEdit
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.From(document.Uri!), Version = document.Version },
            Edits = new TextEditContainer(document.Edits.Select(each => new TextEdit
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(each.Range.Start.Line, each.Range.Start.Character, each.Range.End.Line, each.Range.End.Character),
                NewText = each.NewText,
            })),
        }))),
    };

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
        => request.Edit is null && request.Data is JObject offered && offered[ExtractionDataKey]?.ToString() == RefactoringKinds.ExtractMethod
            ? await ResolveExtractionAsync(request, offered, cancellationToken)
            : request;

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(CodeActionCapability capability, ClientCapabilities clientCapabilities)
    {
        _client = clientCapabilities;
        return new()
        {
            CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix, RefactorRename, RefactorExtractMethod, SourceImportProject),
            ResolveProvider = true,
        };
    }
}
