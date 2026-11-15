using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.LanguageServer.Services.Refactoring.MoveMember;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Configuration;

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
/// A diagnostic fixed by edits its analyzer made is offered each fix as a quick fix, in the analyzer's order, with its edits as they are, for the version of
/// the document they were made of: a fix of another version is not offered. Only the first is preferred, and not when it changes what the code does. A
/// diagnostic fixed by a refactoring this server makes is offered the refactoring for its range, as a selection of that range is.
/// </para>
/// <para>
/// A diagnostic fixed by a change of the project (<see cref="ProjectFix"/>) is offered a quick fix that is a command this server runs
/// (<see cref="ServerCommands.ChangeProject"/>, <see cref="Workspace.ChangeProjectCommand"/>), when every change can be made of the project as it is, and it is
/// not what the fix says already. It is preferred unless it changes what the code does.
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
/// The diagnostics of a code that can be disabled are offered, once for the code and after their fixes, a quick fix that disables it in every workspace of the
/// user (<c>Analysis.DisabledDiagnostics</c>): a command (<see cref="Workspace.ListSettingValueCommand"/>) that asks the client to add it to the user's list. Only a
/// client that expects <see cref="SDK.Client.ListSettingValue"/> is offered it.
/// </para>
/// <para>
/// A workspace whose folder has a VB6 project and no project file of its own is offered, in every document, a source action that imports each VB6 project
/// (<c>rdcore.importProject</c>, <see cref="Workspace.ImportProjectCommand"/>).
/// </para>
/// <para>
/// The procedure a range is in is offered as refactorings that move it above the procedure before it and below the one after it (<see cref="IMoveMemberService"/>),
/// with its comments and its <c>Attribute</c> lines; a move that would take it into a branch of an <c>#If</c> block or out of one is offered disabled, with why,
/// to a client that shows an action it cannot take.
/// </para>
/// <para>
/// A document whose procedures are not sorted is offered a source action that sorts them, by kind, then by name (<see cref="RefactoringKinds.SortMembers"/>).
/// </para>
/// <para>
/// An extraction, a conversion, a move and a sort are the actions that are resolved (<c>resolveProvider = true</c>): a client that resolves an action's edit
/// (<strong>LSP 3.17</strong> §Code Action Resolve Request) is answered them without their edit, which is made and verified when it resolves the action, and
/// refused when the document has changed since. Any other client is answered every action complete.
/// </para>
/// </remarks>
internal sealed class CodeActionHandler(
    IRenameService rename, IWorkspaceDocumentService documents, IFileSystem fileSystem, IExtractMethodService extractMethod, IMoveMemberService moveMember,
    IPlatformClientCapabilitiesService clientCapabilities, OmniSharp.Extensions.JsonRpc.ISerializer serializer, IProjectFileService? projectFiles = null,
    ILibrarySource? libraries = null) : CodeActionHandlerBase
{
    /// <summary>
    /// The kind of the action that renames a symbol (<see cref="RefactoringKinds.Rename"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorRename = new(RefactoringKinds.Rename);

    /// <summary>
    /// The kind of the action that extracts the statements of a selection as a procedure (<see cref="RefactoringKinds.ExtractMethod"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorExtractMethod = new(RefactoringKinds.ExtractMethod);

    /// <summary>
    /// The kind of the action that converts a <c>GoSub</c> routine to a procedure (<see cref="RefactoringKinds.ConvertGoSub"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorConvertGoSub = new(RefactoringKinds.ConvertGoSub);

    /// <summary>
    /// The kind of the action that moves a procedure above the one before it (<see cref="RefactoringKinds.MoveMemberUp"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorMoveMemberUp = new(RefactoringKinds.MoveMemberUp);

    /// <summary>
    /// The kind of the action that moves a procedure below the one after it (<see cref="RefactoringKinds.MoveMemberDown"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorMoveMemberDown = new(RefactoringKinds.MoveMemberDown);

    /// <summary>
    /// The kind of the action that sorts the procedures of a document: a source action (<see cref="RefactoringKinds.SortMembers"/>).
    /// </summary>
    public static readonly CodeActionKind SourceSortMembers = new(RefactoringKinds.SortMembers);

    /// <summary>
    /// The kind of the action that imports a VB6 project: a source action of the whole workspace (<see cref="RefactoringKinds.ImportProject"/>).
    /// </summary>
    public static readonly CodeActionKind SourceImportProject = new(RefactoringKinds.ImportProject);

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

        // a client that asks for some kinds of actions only is answered those: a kind is asked for with the kinds under it.
        var only = request.Context.Only?.Select(kind => kind.ToString()).ToList();
        bool IsAsked(CodeActionKind kind) => only is not { Count: > 0 }
            || only.Any(asked => kind.ToString() == asked || kind.ToString().StartsWith(asked + ".", StringComparison.Ordinal));

        int? version = documents.TryGetDocument(documentUri, out var held) ? held.Version : null;
        var actions = new List<CodeAction>();
        var offered = new HashSet<Position>();
        var refactored = new HashSet<(string Kind, SourceRange Range)>();
        foreach (var diagnostic in request.Context.Diagnostics)
        {
            var fixes = DiagnosticFixes.Of(diagnostic);

            // the edits the analyzer made are each an action of their own, as they are: they change only the document the diagnostic is in, and are offered
            // for the version of it they were made of only. The first is the preferred one, unless it changes what the code does.
            for (var index = 0; index < (fixes?.Edits.Count ?? 0); index++)
            {
                var editFix = fixes!.Edits[index];
                if (editFix.Edits.Count == 0 || editFix.Version is not { } madeOf || madeOf != version
                    || ForTheClient(EditOf(request.TextDocument.Uri, madeOf, editFix.Edits)) is not { } applicable)
                {
                    continue;
                }

                actions.Add(new CodeAction
                {
                    Title = editFix.Title,
                    Kind = CodeActionKind.QuickFix,
                    Diagnostics = new Container<Diagnostic>(diagnostic),
                    Edit = applicable,
                    IsPreferred = index == 0 && !editFix.ChangesBehavior,
                });
            }

            // a refactoring that fixes the finding is offered for the range of the diagnostic, as it is for a selection: of the kinds this server makes.
            if (fixes?.Refactoring?.Kind is RefactoringKinds.ExtractMethod or RefactoringKinds.ConvertGoSub
                && IsAsked(new CodeActionKind(fixes.Refactoring.Kind)))
            {
                var diagnosed = new SourceRange(diagnostic.Range.Start.Line, diagnostic.Range.Start.Character, diagnostic.Range.End.Line, diagnostic.Range.End.Character);
                if (refactored.Add((fixes.Refactoring.Kind, diagnosed))
                    && (await RefactoringActionAsync(request.TextDocument.Uri, fixes.Refactoring.Kind, diagnosed, cancellationToken)).Action is { } refactoring)
                {
                    actions.Add(refactoring with { Diagnostics = new Container<Diagnostic>(diagnostic) });
                }
            }

            // a change of the project that fixes the finding is a command this server runs, offered when it changes the project as it is now.
            if (fixes?.Project is { Changes.Count: > 0 } projectFix && projectFiles?.Project is { } project && libraries is not null
                && projectFix.ApplyTo(project, documentUri, libraries, out _) is { } changed && !ReferenceEquals(changed, project))
            {
                actions.Add(new CodeAction
                {
                    Title = projectFix.Title,
                    Kind = CodeActionKind.QuickFix,
                    Diagnostics = new Container<Diagnostic>(diagnostic),
                    Command = new Command
                    {
                        Name = ServerCommands.ChangeProject,
                        Title = projectFix.Title,
                        Arguments = new JArray(request.TextDocument.Uri.ToString(), JObject.FromObject(projectFix)),
                    },
                    IsPreferred = !projectFix.ChangesBehavior,
                });
            }

            if (fixes?.Rename is not { } fix || workspace.Prepare(documentUri, diagnostic.Range.Start).Value is not { } target)
            {
                continue;
            }

            foreach (var suggestion in fix.Suggestions)
            {
                // a rename with edits the user confirms, of strings or of members accessed late-bound, is not offered as an edit to a client that can't ask
                // them: the edit it would be sent leaves them out, and the code that calls the symbol by its old name fails as it runs. The rename command,
                // which the server asks them for, still is.
                if (workspace.Rename(documentUri, diagnostic.Range.Start, suggestion) is { Value: { } edit } answer
                    && (answer.Questions is not { Count: > 0 } || _client.TakesChangeAnnotations())
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

        actions.AddRange(DisableActions(request.Context.Diagnostics, documentUri));
        actions.AddRange(ImportProjectActions());

        var range = new SourceRange(request.Range.Start.Line, request.Range.Start.Character, request.Range.End.Line, request.Range.End.Character);
        // the routine to convert: the one at the position of an empty range, or the first one a selection is refused for, for a GoSub to it or its Return.
        SourceRange? routine = range.IsEmpty ? range : null;
        if (!range.IsEmpty && (IsAsked(RefactorExtractMethod) || IsAsked(RefactorConvertGoSub)))
        {
            var (extraction, refused) = await RefactoringActionAsync(request.TextDocument.Uri, RefactoringKinds.ExtractMethod, range, cancellationToken);
            if (extraction is not null && !refactored.Contains((RefactoringKinds.ExtractMethod, range)))
            {
                actions.Add(extraction);
            }

            string[] ofARoutine = [ExtractMethodService.RefusalCode(ExtractionRefusalKind.GoSubToOutside), ExtractMethodService.RefusalCode(ExtractionRefusalKind.ReturnOfAGoSubOutside)];
            routine = refused.FirstOrDefault(notice => ofARoutine.Contains(notice.Code))?.Location is { } location
                ? new SourceRange(location.Range.Start.Line, location.Range.Start.Character, location.Range.Start.Line, location.Range.Start.Character)
                : null;
        }

        if (routine is { } at && IsAsked(RefactorConvertGoSub) && !refactored.Contains((RefactoringKinds.ConvertGoSub, at))
            && (await RefactoringActionAsync(request.TextDocument.Uri, RefactoringKinds.ConvertGoSub, at, cancellationToken)).Action is { } conversion)
        {
            actions.Add(conversion);
        }

        if (IsAsked(RefactorMoveMemberUp) || IsAsked(RefactorMoveMemberDown))
        {
            actions.AddRange(await MoveMemberActionsAsync(request.TextDocument.Uri, range, cancellationToken));
        }

        if (IsAsked(SourceSortMembers) && await SortMembersActionAsync(request.TextDocument.Uri, range, cancellationToken) is { } sort)
        {
            actions.Add(sort);
        }

        return new CommandOrCodeActionContainer(actions.Where(action => IsAsked(action.Kind)).Select(action => new CommandOrCodeAction(action)));
    }

    // the key of the setting that lists the codes whose diagnostics are not reported.
    private static readonly string DisabledDiagnosticsKey = $"{nameof(SdkAppOptions.Analysis)}.{nameof(SdkAnalysisOptions.DisabledDiagnostics)}";

    // for each code of the diagnostics of the range that can be disabled, once, after their fixes: the action that disables it in every workspace of the user,
    // which asks the client to add it to the user's list (rdcore.listSettingValue). Only a client that takes such a change is offered it. A code of the
    // language's own errors cannot be disabled.
    private IEnumerable<CodeAction> DisableActions(IEnumerable<Diagnostic> diagnostics, Uri documentUri)
    {
        if (!clientCapabilities.Expects(group => group.ListSettingValue))
        {
            yield break;
        }

        var offered = new HashSet<RDCoreDiagnosticCode>();
        foreach (var diagnostic in diagnostics)
        {
            if (!RDCoreDiagnosticCode.TryParse(diagnostic.Code?.String, out var code) || !code.CanBeDisabled || !offered.Add(code))
            {
                continue;
            }

            var change = new ListSettingValueParams { Key = DisabledDiagnosticsKey, Value = code.ToString(), Scope = SettingScope.Platform, Document = documentUri };
            var title = RefactoringTitles.DisableDiagnostic(code, SettingScope.Platform);
            yield return new CodeAction
            {
                Title = title,
                Kind = CodeActionKind.QuickFix,
                Diagnostics = new Container<Diagnostic>(diagnostics.Where(other => other.Code?.String == diagnostic.Code?.String)),
                Command = new Command { Name = Workspace.ListSettingValueCommand.CommandName, Title = title, Arguments = Workspace.ListSettingValueCommand.ArgumentsOf(change) },
            };
        }
    }

    // the action that extracts the statements of a selection, or converts a GoSub routine, offered when nothing refuses it, with the refusals when something
    // does. A client with a dialog of its own runs it as a command, which prepares the refactoring and applies it with its user's answers; any other is sent the
    // edit, with the defaults, when the refactoring changes nothing the user would have to accept. A refused one, and one with a change to accept, is offered
    // disabled, with why, to a client that shows it so, unless the range holds nothing it refactors.
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

        // a change of what the code does is applied only when the user accepts it, which a client with no dialog cannot ask: one that shows an action it
        // cannot take is told why.
        if (prepared.Notices.FirstOrDefault(notice => notice.Kind is RefactorNoticeKind.Change) is { } change)
        {
            return (ShowsDisabled
                ? new CodeAction { Title = title, Kind = actionKind, Disabled = new CodeActionDisabled { Reason = ExtractMethodService.NeedsAccepting(change) } }
                : null, []);
        }

        if (ResolvesEdits)
        {
            return (new CodeAction { Title = title, Kind = actionKind, Data = ResolvedData(kind, lspUri, range, version) }, []);
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
        if (!TryOfferedAt(offered, out var uri, out var range, out var version))
        {
            return action;
        }

        var answer = await extractMethod.ApplyAsync(kind, uri, range, DefaultAnswers(), [], token);
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

    // the actions that move the procedure a range is in above the one before it and below the one after it, each with its edit, made and verified, or without
    // it for a client that resolves it. A refused one is offered disabled, with why, to a client that shows it so.
    private async Task<IReadOnlyList<CodeAction>> MoveMemberActionsAsync(DocumentUri lspUri, SourceRange range, CancellationToken token)
    {
        var documentUri = lspUri.ToUri();
        int? version = documents.TryGetDocument(documentUri, out var document) ? document.Version : null;
        var actions = new List<CodeAction>();
        foreach (var offer in await moveMember.OffersAsync(documentUri, range, token))
        {
            var kind = offer.Direction is MemberMoveDirection.Up ? RefactoringKinds.MoveMemberUp : RefactoringKinds.MoveMemberDown;
            if (offer.Refusal is { } refusal)
            {
                if (ShowsDisabled)
                {
                    actions.Add(new CodeAction { Title = offer.Title, Kind = new CodeActionKind(kind), Disabled = new CodeActionDisabled { Reason = refusal } });
                }

                continue;
            }

            if (ResolvesEdits)
            {
                actions.Add(new CodeAction { Title = offer.Title, Kind = new CodeActionKind(kind), Data = ResolvedData(kind, lspUri, range, version) });
                continue;
            }

            if (await moveMember.MoveAsync(documentUri, range, offer.Direction, token) is { Edits: not null } answer && ForTheClient(LspEditOf(answer)) is { } applicable)
            {
                actions.Add(new CodeAction { Title = offer.Title, Kind = new CodeActionKind(kind), Edit = applicable });
            }
        }

        return actions;
    }

    // the action that sorts the procedures of a document, when they are not sorted: with its edit, made and verified, or without it for a client that
    // resolves it.
    private async Task<CodeAction?> SortMembersActionAsync(DocumentUri lspUri, SourceRange range, CancellationToken token)
    {
        var documentUri = lspUri.ToUri();
        if (await moveMember.SortOfferAsync(documentUri, token) is not { } title)
        {
            return null;
        }

        if (ResolvesEdits)
        {
            int? version = documents.TryGetDocument(documentUri, out var document) ? document.Version : null;
            return new CodeAction { Title = title, Kind = SourceSortMembers, Data = ResolvedData(RefactoringKinds.SortMembers, lspUri, range, version) };
        }

        return await moveMember.SortAsync(documentUri, token) is { Edits: not null } answer && ForTheClient(LspEditOf(answer)) is { } applicable
            ? new CodeAction { Title = title, Kind = SourceSortMembers, Edit = applicable }
            : null;
    }

    // the move or the sort a client resolves: its edit, verified, of the version of the document it was offered for; one that is refused, or whose document
    // has changed since, is answered an error that says why.
    private async Task<CodeAction> ResolveMoveAsync(CodeAction action, string kind, JObject offered, CancellationToken token)
    {
        if (!TryOfferedAt(offered, out var uri, out var range, out var version))
        {
            return action;
        }

        var answer = kind == RefactoringKinds.SortMembers
            ? await moveMember.SortAsync(uri, token)
            : await moveMember.MoveAsync(uri, range, kind == RefactoringKinds.MoveMemberUp ? MemberMoveDirection.Up : MemberMoveDirection.Down, token);
        var refusal = answer.Edits is not null && answer.Version != version ? MoveMemberService.DocumentChanged(kind) : answer.Refusal;
        if (refusal is not null || answer.Edits is null)
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

    // the data of an action a client resolves: a client sends the action back as it holds it, and may drop a kind it does not know, so the data names the
    // refactoring, the range it was offered for, and the version of the document then.
    private JObject ResolvedData(string kind, DocumentUri lspUri, SourceRange range, int? version) => new()
    {
        [ExtractionDataKey] = kind,
        ["uri"] = lspUri.ToString(),
        ["range"] = JToken.Parse(serializer.SerializeObject(new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
            range.Start.Line, range.Start.Character, range.End.Line, range.End.Character))),
        ["version"] = version is { } number ? new JValue(number) : JValue.CreateNull(),
    };

    // the document, the range and the version an action a client resolves was offered for (ResolvedData).
    private bool TryOfferedAt(JObject offered, out Uri uri, out SourceRange range, out int? version)
    {
        (uri, range, version) = (null!, default, null);
        if (offered["uri"]?.ToString() is not { Length: > 0 } offeredUri || offered["range"] is not JObject offeredRange)
        {
            return false;
        }

        var lspRange = serializer.DeserializeObject<OmniSharp.Extensions.LanguageServer.Protocol.Models.Range>(offeredRange.ToString());
        uri = DocumentUri.From(offeredUri).ToUri();
        range = new SourceRange(lspRange.Start.Line, lspRange.Start.Character, lspRange.End.Line, lspRange.End.Character);
        version = offered["version"] is { Type: JTokenType.Integer } number ? number.Value<int>() : null;
        return true;
    }

    // a move's edits as LSP writes them: of its document, of the version they were made from.
    private static WorkspaceEdit LspEditOf(MemberMoveAnswer answer) => new()
    {
        DocumentChanges = new Container<WorkspaceEditDocumentChange>(new WorkspaceEditDocumentChange(new TextDocumentEdit
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.From(answer.Document), Version = answer.Version },
            Edits = new TextEditContainer(answer.Edits!.Select(edit => new TextEdit
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(edit.Range.Start.Line, edit.Range.Start.Character, edit.Range.End.Line, edit.Range.End.Character),
                NewText = edit.NewText,
            })),
        })),
    };

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

    // the edits of a fix an analyzer made, as the edits of the version of the document they are of: a client applies them to that version only.
    private static WorkspaceEdit EditOf(DocumentUri document, int version, IEnumerable<TextEdit> edits) => new()
    {
        DocumentChanges = new Container<WorkspaceEditDocumentChange>(new WorkspaceEditDocumentChange(new TextDocumentEdit
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = document, Version = version },
            Edits = new TextEditContainer(edits),
        })),
    };

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
                    Name = ServerCommands.ImportProject,
                    Title = title,
                    Arguments = new JArray(DocumentUri.FromFileSystemPath(vbp).ToString()),
                },
            };
        });
    }

    public override async Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken)
        => request.Edit is null && request.Data is JObject offered
            ? offered[ExtractionDataKey]?.ToString() switch
            {
                (RefactoringKinds.ExtractMethod or RefactoringKinds.ConvertGoSub) and var kind => await ResolveRefactoringAsync(request, kind, offered, cancellationToken),
                (RefactoringKinds.MoveMemberUp or RefactoringKinds.MoveMemberDown or RefactoringKinds.SortMembers) and var kind => await ResolveMoveAsync(request, kind, offered, cancellationToken),
                _ => request,
            }
            : request;

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(CodeActionCapability capability, ClientCapabilities clientCapabilities)
    {
        _client = clientCapabilities;
        return new()
        {
            CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix, RefactorRename, RefactorExtractMethod, RefactorConvertGoSub, RefactorMoveMemberUp, RefactorMoveMemberDown, SourceSortMembers, SourceImportProject),
            ResolveProvider = true,
        };
    }
}
