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
/// What can be done at a range of a document, as <c>textDocument/codeAction</c> answers it (<see cref="CodeActionService"/>).
/// </summary>
internal interface ICodeActionService
{
    /// <summary>
    /// The actions of a range, as they are offered to a client.
    /// </summary>
    /// <param name="request">The range, its diagnostics, and the kinds of actions asked for.</param>
    /// <param name="client">What the client said it can do as it initialized: the form of the edits it is sent, and whether it resolves them.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    Task<IReadOnlyList<CodeAction>> ActionsAsync(CodeActionParams request, ClientCapabilities? client, CancellationToken cancellationToken);

    /// <summary>
    /// The action, with the edit of a refactoring that was offered without it (<strong>LSP 3.17</strong> §Code Action Resolve Request); any other as it is.
    /// </summary>
    /// <param name="request">The action, as the client holds it.</param>
    /// <param name="client">What the client said it can do as it initialized.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <exception cref="RpcErrorException">The refactoring is refused, or the document has changed since it was offered: <c>RequestFailed</c>, with why.</exception>
    Task<CodeAction> ResolveAsync(CodeAction request, ClientCapabilities? client, CancellationToken cancellationToken);
}

/// <summary>
/// The <see cref="ICodeActionService"/> of the language server: each call is answered by the <see cref="CodeActions"/> of the client it is given.
/// </summary>
internal sealed class CodeActionService(
    IRenameService rename, IWorkspaceDocumentService documents, IFileSystem fileSystem, IExtractMethodService extractMethod, IMoveMemberService moveMember,
    IPlatformClientCapabilitiesService clientCapabilities, OmniSharp.Extensions.JsonRpc.ISerializer serializer, IProjectFileService? projectFiles = null,
    ILibrarySource? libraries = null, IEncapsulateFieldService? encapsulateField = null, IImplementInterfaceService? implementInterface = null,
    IMoveCloserToUsageService? closerToUsage = null, IParameterReorderService? parameterReorder = null, IInlineMemberService? inlineMember = null)
    : ICodeActionService
{
    public Task<IReadOnlyList<CodeAction>> ActionsAsync(CodeActionParams request, ClientCapabilities? client, CancellationToken cancellationToken)
        => For(client).ActionsAsync(request, cancellationToken);

    public Task<CodeAction> ResolveAsync(CodeAction request, ClientCapabilities? client, CancellationToken cancellationToken)
        => For(client).ResolveAsync(request, cancellationToken);

    private CodeActions For(ClientCapabilities? client) => new(client, rename, documents, fileSystem, extractMethod, moveMember, clientCapabilities, serializer, projectFiles, libraries, encapsulateField, implementInterface,
        closerToUsage, parameterReorder, inlineMember);
}

/// <summary>
/// What <c>textDocument/codeAction</c> answers (<strong>LSP 3.17</strong> §Code Action Request) to one client, as its capabilities say: what can be done about the
/// diagnostics of a range of a document, and what can be renamed or extracted in it.
/// </summary>
/// <remarks>
/// <para>
/// A diagnostic whose fix is a rename (<see cref="DiagnosticFixes"/>) is offered as quick fixes: a rename to each name its analyzer suggests that can be made
/// (its edits are the action's), and a rename to a name the user chooses, which is a command the client runs (<see cref="RefactoringCommands.Rename"/>). A name
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
/// applies it; any other is sent its edit, made with the defaults. An edit that changes what the code does is sent only to a client that takes change
/// annotations, annotated as an edit the client asks its user to confirm before it applies it.
/// </para>
/// <para>
/// A <c>GoSub</c> routine is offered, the same way, as a refactoring that converts it to a procedure: at a position on its label, on a <c>GoSub</c> that names
/// it or on one of its <c>Return</c>s, and for a selection whose extraction is refused for a <c>GoSub</c> to it or one of its <c>Return</c>s.
/// </para>
/// <para>
/// A parameter declared at the start of the range is offered its removal (<see cref="ParameterRemoval"/>), as an edit, and so is the parameter of a diagnostic
/// fixed by removing it, as a preferred quick fix. The procedure named at the start of the range, or whose parameter is declared there, is offered the
/// reordering of its parameters (<see cref="IParameterReorderService"/>) when it has two or more: a command (<see cref="RefactoringCommands.Refactor"/>) that
/// a client expecting <see cref="SDK.Client.Refactoring"/> runs with its user's order. A refused one is offered disabled, with why, to a client that shows it
/// so.
/// </para>
/// <para>
/// The diagnostics of a code that can be disabled are offered, once for the code and after their fixes, a quick fix that disables it in every workspace of the
/// user (<c>Analysis.DisabledDiagnostics</c>), then one that disables it in the document's workspace only: each a command
/// (<see cref="Workspace.ListSettingValueCommand"/>) that asks the client to add it to the user's list, or to the workspace's. Only a client that expects
/// <see cref="SDK.Client.ListSettingValue"/> is offered them. A diagnostic whose fix adds an item to the list of a setting (<see cref="SettingFix"/>), as a
/// name a naming rule is to allow, is offered the quick fix that asks the client to add it to the user's list, by the same command.
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
/// The field named at the start of a range is offered as a refactoring that encapsulates it behind properties (<see cref="IEncapsulateFieldService"/>), as an
/// extraction is: a command for a client that expects <see cref="SDK.Client.Refactoring"/>, and its edit, made with the defaults, for any other, which the
/// client asks its user to confirm when it changes what the code does. A refused one is offered disabled, with why, to a client that shows it so, unless no
/// field is named there.
/// </para>
/// <para>
/// An <c>Implements</c> directive of a class module, or a <c>WithEvents</c> variable of one, on a line a range starts on, is offered as a refactoring that writes
/// the procedures the class does not have for it (<see cref="IImplementInterfaceService"/>): the members of the interface, or the handlers of the variable's
/// events. The action is preferred, and fixes the diagnostics of the range that say the directive's interface is not implemented. A refused one is offered
/// disabled, with why, to a client that shows it so.
/// </para>
/// <para>
/// A variable named at a position, or by a selection on one line, is offered a refactoring that moves its declaration closer to its usage
/// (<see cref="IMoveCloserToUsageService"/>): as a command to a client that expects <see cref="SDK.Client.Refactoring"/>, and to any other as its edit, with
/// <c>Static</c> for a variable of a module, when that changes nothing the user would have to accept.
/// </para>
/// <para>
/// An extraction, a conversion, a move, a sort, an encapsulation and an implementation are the actions that are resolved (<c>resolveProvider = true</c>): a
/// client that resolves an action's edit (<strong>LSP 3.17</strong> §Code Action Resolve Request) is answered them without their edit, which is made and verified
/// when it resolves the action, and refused when the document has changed since. Any other client is answered every action complete.
/// </para>
/// </remarks>
internal sealed class CodeActions(
    ClientCapabilities? client,
    IRenameService rename, IWorkspaceDocumentService documents, IFileSystem fileSystem, IExtractMethodService extractMethod, IMoveMemberService moveMember,
    IPlatformClientCapabilitiesService clientCapabilities, OmniSharp.Extensions.JsonRpc.ISerializer serializer, IProjectFileService? projectFiles = null,
    ILibrarySource? libraries = null, IEncapsulateFieldService? encapsulateField = null, IImplementInterfaceService? implementInterface = null,
    IMoveCloserToUsageService? closerToUsage = null, IParameterReorderService? parameterReorder = null, IInlineMemberService? inlineMember = null)
{
    // LSP "RequestFailed": the request is valid, and is refused.
    private const int RequestFailedCode = -32803;

    // the member of an action's data that names the refactoring it resolves to.
    private const string ExtractionDataKey = "refactoring";

    // a client that sends an action's data back and resolves its edit is sent an extraction without its edit.
    private bool ResolvesEdits => client?.TextDocument?.CodeAction.Value is { DataSupport: true, ResolveSupport.Properties: { } properties }
        && properties.Contains("edit");

    // a client that shows an action it cannot take, with why, is offered a refactoring that is refused.
    private bool ShowsDisabled => client?.TextDocument?.CodeAction.Value is { DisabledSupport: true };

    public async Task<IReadOnlyList<CodeAction>> ActionsAsync(CodeActionParams request, CancellationToken cancellationToken)
    {
        var documentUri = request.TextDocument.Uri.ToUri();
        if (await rename.ComposeAsync(cancellationToken) is not { } workspace)
        {
            return [];
        }

        // a client that asks for some kinds of actions only is answered those: a kind is asked for with the kinds under it.
        var only = request.Context.Only?.Select(kind => kind.ToString()).ToList();
        bool IsAsked(CodeActionKind kind) => only is not { Count: > 0 }
            || only.Any(asked => kind.ToString() == asked || kind.ToString().StartsWith(asked + ".", StringComparison.Ordinal));

        int? version = documents.TryGetDocument(documentUri, out var held) ? held.Version : null;
        var actions = new List<CodeAction>();
        var offered = new HashSet<Position>();
        var parameters = new HashSet<Position>();
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

            // a parameter that the finding is fixed by removing is offered its removal, as an edit, when it can be made.
            if (fixes?.Refactoring?.Kind is RefactoringKinds.RemoveParameter
                && workspace.RemoveParameter(documentUri, diagnostic.Range.Start) is { Answer.Value: { } removal } removable
                && ForTheClient(removal) is { } removed)
            {
                actions.Add(new CodeAction
                {
                    Title = ParameterRemovalRefusal.Title(removable.Name),
                    Kind = CodeActionKind.QuickFix,
                    Diagnostics = new Container<Diagnostic>(diagnostic),
                    Edit = removed,
                    IsPreferred = true,
                });
                parameters.Add(diagnostic.Range.Start);
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

            // an item the fix adds to the list of a setting is added to the user's list: a command that asks the client to, for a client that takes it.
            if (fixes?.Setting is { Key.Length: > 0, Value.Length: > 0 } settingFix && clientCapabilities.Expects(group => group.ListSettingValue))
            {
                var change = new ListSettingValueParams
                {
                    Key = settingFix.Key, Value = settingFix.Value, Scope = SettingScope.Platform, Document = documentUri,
                };
                actions.Add(ListSettingAction(settingFix.Title, change, [diagnostic]));
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
                    && (answer.Questions is not { Count: > 0 } || client.TakesChangeAnnotations())
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
            actions.Add(RenameCommand(documentUri, name, position, CodeActionHandler.RefactorRename, diagnostic: null));
        }

        // the parameter declared at the start of the range is offered its removal: as an edit when it can be made, or disabled, with why, to a client
        // that shows it.
        if (IsAsked(CodeActionHandler.RefactorRemoveParameter) && !parameters.Contains(request.Range.Start)
            && workspace.RemoveParameter(documentUri, request.Range.Start) is { } parameter)
        {
            var title = ParameterRemovalRefusal.Title(parameter.Name);
            if (parameter.Answer.Value is { } removal && ForTheClient(removal) is { } removed)
            {
                actions.Add(new CodeAction { Title = title, Kind = CodeActionHandler.RefactorRemoveParameter, Edit = removed });
            }
            else if (ShowsDisabled && parameter.Answer.Refusal is { } refusal)
            {
                actions.Add(new CodeAction { Title = title, Kind = CodeActionHandler.RefactorRemoveParameter, Disabled = new CodeActionDisabled { Reason = refusal } });
            }
        }

        // the procedure named at the start of the range, or one of its parameters declared there, is offered the reordering of its parameters: a
        // command that a client with a dialog runs, which asks its user the order. It is not offered to another client: the order it would take by
        // default is the one the parameters have.
        if (IsAsked(CodeActionHandler.RefactorReorderParameters) && parameterReorder is not null && clientCapabilities.Expects(capabilities => capabilities.Refactoring)
            && await parameterReorder.OfferAsync(documentUri, new SourcePosition(request.Range.Start.Line, request.Range.Start.Character), cancellationToken) is { } reorder)
        {
            if (reorder.Refusal is null)
            {
                var start = new SourcePosition(request.Range.Start.Line, request.Range.Start.Character);
                var prepare = new RefactorPrepareParams { Kind = RefactoringKinds.ReorderParameters, Uri = documentUri, Range = new SourceRange(start, start) };
                actions.Add(new CodeAction
                {
                    Title = reorder.Title,
                    Kind = CodeActionHandler.RefactorReorderParameters,
                    Command = new Command { Name = RefactoringCommands.Refactor, Title = reorder.Title, Arguments = new JArray(JToken.Parse(serializer.SerializeObject(prepare))) },
                });
            }
            else if (ShowsDisabled)
            {
                actions.Add(new CodeAction { Title = reorder.Title, Kind = CodeActionHandler.RefactorReorderParameters, Disabled = new CodeActionDisabled { Reason = reorder.Refusal } });
            }
        }

        // a call named at the start of the range, or a member declared there, is offered Inline Member: a command that a client with a dialog runs, which
        // shows its user the calls and the removal of the member.
        var inlineAt = new SourcePosition(request.Range.Start.Line, request.Range.Start.Character);
        if (IsAsked(CodeActionHandler.RefactorInlineMember) && inlineMember is not null && clientCapabilities.Expects(capabilities => capabilities.Refactoring)
            && await inlineMember.OfferAsync(documentUri, inlineAt, cancellationToken) is { } inline)
        {
            if (inline.Refusal is null)
            {
                var prepare = new RefactorPrepareParams
                {
                    Kind = RefactoringKinds.InlineMember, Uri = documentUri, Range = new SourceRange(inlineAt, inlineAt),
                };
                actions.Add(new CodeAction
                {
                    Title = inline.Title,
                    Kind = CodeActionHandler.RefactorInlineMember,
                    Command = new Command
                    {
                        Name = RefactoringCommands.Refactor, Title = inline.Title, Arguments = new JArray(JToken.Parse(serializer.SerializeObject(prepare))),
                    },
                });
            }
            else if (ShowsDisabled)
            {
                actions.Add(new CodeAction
                {
                    Title = inline.Title, Kind = CodeActionHandler.RefactorInlineMember, Disabled = new CodeActionDisabled { Reason = inline.Refusal },
                });
            }
        }

        actions.AddRange(DisableActions(request.Context.Diagnostics, documentUri));
        actions.AddRange(ImportProjectActions());

        var range = new SourceRange(request.Range.Start.Line, request.Range.Start.Character, request.Range.End.Line, request.Range.End.Character);
        // the routine to convert: the one at the position of an empty range, or the first one a selection is refused for, for a GoSub to it or its Return.
        SourceRange? routine = range.IsEmpty ? range : null;
        if (!range.IsEmpty && (IsAsked(CodeActionHandler.RefactorExtractMethod) || IsAsked(CodeActionHandler.RefactorConvertGoSub)))
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

        if (routine is { } at && IsAsked(CodeActionHandler.RefactorConvertGoSub) && !refactored.Contains((RefactoringKinds.ConvertGoSub, at))
            && (await RefactoringActionAsync(request.TextDocument.Uri, RefactoringKinds.ConvertGoSub, at, cancellationToken)).Action is { } conversion)
        {
            actions.Add(conversion);
        }

        if (IsAsked(CodeActionHandler.RefactorMoveMemberUp) || IsAsked(CodeActionHandler.RefactorMoveMemberDown))
        {
            actions.AddRange(await MoveMemberActionsAsync(request.TextDocument.Uri, range, cancellationToken));
        }

        if (IsAsked(CodeActionHandler.SourceSortMembers) && await SortMembersActionAsync(request.TextDocument.Uri, range, cancellationToken) is { } sort)
        {
            actions.Add(sort);
        }

        if (IsAsked(CodeActionHandler.RefactorEncapsulateField))
        {
            actions.AddRange(await EncapsulateFieldActionsAsync(request.TextDocument.Uri, range, cancellationToken));
        }

        if (IsAsked(CodeActionHandler.RefactorImplementInterface)
            && await ImplementInterfaceActionAsync(request.TextDocument.Uri, range, request.Context.Diagnostics, cancellationToken) is { } implementation)
        {
            actions.Add(implementation);
        }

        // the variable named at a position, or by a selection on one line.
        if (range.Start.Line == range.End.Line && IsAsked(CodeActionHandler.RefactorMoveCloserToUsage)
            && await CloserToUsageActionAsync(request.TextDocument.Uri, range.Start, cancellationToken) is { } moved)
        {
            actions.Add(moved);
        }

        return [.. actions.Where(action => IsAsked(action.Kind))];
    }

    // the key of the setting that lists the codes whose diagnostics are not reported.
    private static readonly string DisabledDiagnosticsKey = $"{nameof(SdkAppOptions.Analysis)}.{nameof(SdkAnalysisOptions.DisabledDiagnostics)}";

    // where a code is disabled: in every workspace of the user, then in the document's workspace only.
    private static readonly SettingScope[] DisableScopes = [SettingScope.Platform, SettingScope.Workspace];

    // for each code of the diagnostics of the range that can be disabled, once, after their fixes: the action that disables it in every workspace of the user,
    // then the one that disables it in the document's workspace only. Each asks the client to add the code to a list (rdcore.listSettingValue): the user's,
    // or the workspace's. Only a client that takes such a change is offered them. A code of the language's own errors cannot be disabled.
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

            foreach (var scope in DisableScopes)
            {
                var change = new ListSettingValueParams { Key = DisabledDiagnosticsKey, Value = code.ToString(), Scope = scope, Document = documentUri };
                yield return ListSettingAction(
                    RefactoringTitles.DisableDiagnostic(code, scope), change, diagnostics.Where(other => other.Code?.String == diagnostic.Code?.String));
            }
        }
    }

    // the quick fix that asks the client to change the list of a setting (rdcore.listSettingValue), for the diagnostics it fixes.
    private static CodeAction ListSettingAction(string title, ListSettingValueParams change, IEnumerable<Diagnostic> diagnostics) => new()
    {
        Title = title,
        Kind = CodeActionKind.QuickFix,
        Diagnostics = new Container<Diagnostic>(diagnostics),
        Command = new Command
        {
            Name = Workspace.ListSettingValueCommand.CommandName, Title = title, Arguments = Workspace.ListSettingValueCommand.ArgumentsOf(change),
        },
    };

    // the action that extracts the statements of a selection, or converts a GoSub routine, offered when nothing refuses it, with the refusals when something
    // does. A client with a dialog of its own runs it as a command, which prepares the refactoring and applies it with its user's answers; any other is sent the
    // edit, with the defaults: when the refactoring changes what the code does, as one its user confirms, and only where it takes change annotations. A
    // refused one, and one with a change to accept for a client that cannot ask its user, is offered disabled, with why, to a client that shows it so, unless
    // the range holds nothing it refactors.
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

        // a change of what the code does is applied only when the user accepts it: a client that takes change annotations is sent the edit to confirm with its
        // user first, and one that cannot ask and shows an action it cannot take is told why.
        var changes = ChangesOf(prepared);
        if (changes.Count > 0 && !client.TakesChangeAnnotations())
        {
            var reason = ExtractMethodService.NeedsAccepting(changes[0]);
            return (ShowsDisabled ? new CodeAction { Title = title, Kind = actionKind, Disabled = new CodeActionDisabled { Reason = reason } } : null, []);
        }

        if (ResolvesEdits)
        {
            return (new CodeAction { Title = title, Kind = actionKind, Data = WithChanges(ResolvedData(kind, lspUri, range, version), changes) }, []);
        }

        var answer = await extractMethod.ApplyAsync(kind, documentUri, range, DefaultAnswers(), CodesOf(changes), token);
        if (answer.Edit is null || ForTheClient(Confirmed(LspEditOf(answer), title, changes)) is not { } applicable)
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

        var changes = client.TakesChangeAnnotations() ? ChangesOf(offered) : [];
        var answer = await extractMethod.ApplyAsync(kind, uri, range, DefaultAnswers(), CodesOf(changes), token);
        var refusal = answer.Edit is { } edit && edit.Documents.Any(document => document.Version != version) ? ExtractMethodService.DocumentChanged(kind) : answer.Refusal;
        if (refusal is not null || answer.Edit is null)
        {
            throw new RpcErrorException(RequestFailedCode, error: null!, refusal ?? string.Empty);
        }

        try
        {
            return new CodeAction
            {
                Title = action.Title, Kind = new CodeActionKind(kind), Data = action.Data,
                Edit = Confirmed(LspEditOf(answer), ExtractMethodService.Title(kind), changes).For(client),
            };
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
            return new CodeAction { Title = title, Kind = CodeActionHandler.SourceSortMembers, Data = ResolvedData(RefactoringKinds.SortMembers, lspUri, range, version) };
        }

        return await moveMember.SortAsync(documentUri, token) is { Edits: not null } answer && ForTheClient(LspEditOf(answer)) is { } applicable
            ? new CodeAction { Title = title, Kind = CodeActionHandler.SourceSortMembers, Edit = applicable }
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
            return new CodeAction { Title = action.Title, Kind = new CodeActionKind(kind), Data = action.Data, Edit = LspEditOf(answer).For(client) };
        }
        catch (WorkspaceEditNotSupportedException unsupported)
        {
            throw new RpcErrorException(RequestFailedCode, error: null!, unsupported.Message);
        }
    }

    // the key of the data of an encapsulation a client resolves that encapsulates every Public field of the module.
    private const string AllFieldsDataKey = "allFields";

    // the actions that encapsulate the field named at the start of a range. The field alone, as an extraction is offered: a command for a client with a dialog
    // of its own, the edit with the defaults for any other (EncapsulationActionAsync). A refused one is offered disabled, with why, to a client that shows it
    // so, unless no field is named there. And, when the module has other Public fields, the field with all of them, with the defaults, offered as an edit to
    // any client: disabled, with why, when one of them is refused.
    private async Task<IReadOnlyList<CodeAction>> EncapsulateFieldActionsAsync(DocumentUri lspUri, SourceRange range, CancellationToken token)
    {
        if (encapsulateField is null)
        {
            return [];
        }

        var documentUri = lspUri.ToUri();
        int? version = documents.TryGetDocument(documentUri, out var document) ? document.Version : null;
        var prepared = await encapsulateField.PrepareAsync(documentUri, range.Start, token);
        if (prepared.Notices.FirstOrDefault(notice => notice.Kind is RefactorNoticeKind.Refusal && notice.Item is null) is { } refused)
        {
            return ShowsDisabled && refused.Code != EncapsulateFieldService.NotAFieldCode
                ? [new CodeAction { Title = EncapsulateFieldService.Title(), Kind = CodeActionHandler.RefactorEncapsulateField, Disabled = new CodeActionDisabled { Reason = refused.Message } }]
                : [];
        }

        var own = prepared with { Notices = [.. prepared.Notices.Where(notice => notice.Item is null)] };
        var all = EncapsulateFieldService.AllFields(prepared.Choices);
        CodeAction?[] actions =
        [
            await EncapsulationActionAsync(lspUri, range, version, own, [], EncapsulateFieldService.Title(), dialog: true, token),
            all.Count > 0 ? await EncapsulationActionAsync(lspUri, range, version, prepared, all, EncapsulateFieldService.AllTitle(), dialog: false, token) : null,
        ];
        return [.. actions.OfType<CodeAction>()];
    }

    // the action of an encapsulation with answers, as its notices prepared it: a command for a client with a dialog of its own when the dialog asks it, the edit
    // for any other, which its user confirms when it changes what the code does. One that is refused, or that changes what the code does for a client that
    // takes no change annotations, is offered disabled, with why, to a client that shows it so.
    private async Task<CodeAction?> EncapsulationActionAsync(
        DocumentUri lspUri, SourceRange range, int? version, RefactorPrepareResult prepared, IReadOnlyList<RefactorAnswer> answers, string title, bool dialog,
        CancellationToken token)
    {
        var documentUri = lspUri.ToUri();
        CodeAction? Disabled(string reason) => ShowsDisabled ? new CodeAction { Title = title, Kind = CodeActionHandler.RefactorEncapsulateField, Disabled = new CodeActionDisabled { Reason = reason } } : null;
        if (prepared.Notices.FirstOrDefault(notice => notice.Kind is RefactorNoticeKind.Refusal) is { } refused)
        {
            return Disabled(refused.Message);
        }

        if (dialog && clientCapabilities.Expects(capabilities => capabilities.Refactoring))
        {
            var prepare = new RefactorPrepareParams { Kind = RefactoringKinds.EncapsulateField, Uri = documentUri, Range = range };
            return new CodeAction
            {
                Title = title,
                Kind = CodeActionHandler.RefactorEncapsulateField,
                Command = new Command { Name = RefactoringCommands.Refactor, Title = title, Arguments = new JArray(JToken.Parse(serializer.SerializeObject(prepare))) },
            };
        }

        var changes = ChangesOf(prepared);
        if (changes.Count > 0 && !client.TakesChangeAnnotations())
        {
            return Disabled(ExtractMethodService.NeedsAccepting(changes[0]));
        }

        if (ResolvesEdits)
        {
            var data = WithChanges(ResolvedData(RefactoringKinds.EncapsulateField, lspUri, range, version), changes);
            if (answers.Count > 0)
            {
                data[AllFieldsDataKey] = true;
            }

            return new CodeAction { Title = title, Kind = CodeActionHandler.RefactorEncapsulateField, Data = data };
        }

        var answer = await encapsulateField!.ApplyAsync(documentUri, range.Start, answers, CodesOf(changes), token);
        if (answer.Edit is null)
        {
            return answer.Refusal is { } refusal ? Disabled(refusal) : null;
        }

        return ForTheClient(Confirmed(LspEditOf(answer.Edit), title, changes)) is { } applicable
            ? new CodeAction { Title = title, Kind = CodeActionHandler.RefactorEncapsulateField, Edit = applicable }
            : null;
    }

    // the encapsulation a client resolves: its edit, made with the defaults and verified, of the version of the document it was offered for; of every Public
    // field of the module when it was offered so. One that is refused, or whose document has changed since, is answered an error that says why.
    private async Task<CodeAction> ResolveEncapsulationAsync(CodeAction action, JObject offered, CancellationToken token)
    {
        if (encapsulateField is null || !TryOfferedAt(offered, out var uri, out var range, out var version))
        {
            return action;
        }

        var allFields = offered[AllFieldsDataKey] is { Type: JTokenType.Boolean } all && all.Value<bool>();
        var answers = allFields ? EncapsulateFieldService.AllFields((await encapsulateField.PrepareAsync(uri, range.Start, token)).Choices) : [];
        var changes = client.TakesChangeAnnotations() ? ChangesOf(offered) : [];
        var answer = await encapsulateField.ApplyAsync(uri, range.Start, answers, CodesOf(changes), token);
        var refusal = answer.Edit is { } edit && edit.Documents.Any(document => document.Version != version) ? EncapsulateFieldService.DocumentChanged() : answer.Refusal;
        if (refusal is not null || answer.Edit is null)
        {
            throw new RpcErrorException(RequestFailedCode, error: null!, refusal ?? string.Empty);
        }

        try
        {
            var label = allFields ? EncapsulateFieldService.AllTitle() : EncapsulateFieldService.Title();
            return new CodeAction
            {
                Title = action.Title, Kind = CodeActionHandler.RefactorEncapsulateField, Data = action.Data,
                Edit = Confirmed(LspEditOf(answer.Edit), label, changes).For(client),
            };
        }
        catch (WorkspaceEditNotSupportedException unsupported)
        {
            throw new RpcErrorException(RequestFailedCode, error: null!, unsupported.Message);
        }
    }

    // a refactoring's edits as LSP writes them: the edits of each document, of the version they were made from.
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

    // the action that writes the procedures the directive or the WithEvents variable on the line a range starts on calls for, preferred, with the diagnostics
    // of the range that say the directive's interface is not implemented; with its edit, made and verified, or without it for a client that resolves it. A
    // refused one is offered disabled, with why, to a client that shows it so.
    private async Task<CodeAction?> ImplementInterfaceActionAsync(DocumentUri lspUri, SourceRange range, IEnumerable<Diagnostic> diagnostics, CancellationToken token)
    {
        var documentUri = lspUri.ToUri();
        if (implementInterface is null || await implementInterface.OfferAsync(documentUri, range, token) is not { } offer)
        {
            return null;
        }

        if (offer.Refusal is { } refusal)
        {
            return ShowsDisabled
                ? new CodeAction { Title = offer.Title, Kind = CodeActionHandler.RefactorImplementInterface, Disabled = new CodeActionDisabled { Reason = refusal } }
                : null;
        }

        var notImplemented = RDCoreDiagnosticCode.CompilePrefix + ((int)global::RDCore.Runtime.LanguageCore.Errors.VBCompileErrorId.InterfaceMemberNotImplemented).ToString("00000", System.Globalization.CultureInfo.InvariantCulture);
        var fixes = diagnostics.Where(diagnostic => diagnostic.Code?.String == notImplemented
            && diagnostic.Range.Start.Line >= offer.Target.Start.Line && diagnostic.Range.Start.Line <= offer.Target.End.Line).ToArray();
        var action = new CodeAction
        {
            Title = offer.Title,
            Kind = CodeActionHandler.RefactorImplementInterface,
            Diagnostics = fixes.Length == 0 ? null : new Container<Diagnostic>(fixes),
            IsPreferred = true,
        };

        if (ResolvesEdits)
        {
            int? version = documents.TryGetDocument(documentUri, out var document) ? document.Version : null;
            return action with { Data = ResolvedData(RefactoringKinds.ImplementInterface, lspUri, range, version) };
        }

        return await implementInterface.ImplementAsync(documentUri, range, token) is { Edits: { } edits } answer
            && ForTheClient(LspEditOf(answer.Document, answer.Version, edits)) is { } applicable
            ? action with { Edit = applicable }
            : null;
    }

    // the implementation a client resolves: its edit, verified, of the version of the document it was offered for; one that is refused, or whose document has
    // changed since, is answered an error that says why.
    private async Task<CodeAction> ResolveImplementationAsync(CodeAction action, JObject offered, CancellationToken token)
    {
        if (implementInterface is null || !TryOfferedAt(offered, out var uri, out var range, out var version))
        {
            return action;
        }

        var answer = await implementInterface.ImplementAsync(uri, range, token);
        var refusal = answer.Edits is not null && answer.Version != version ? ImplementInterfaceService.DocumentChanged() : answer.Refusal;
        if (refusal is not null || answer.Edits is null)
        {
            throw new RpcErrorException(RequestFailedCode, error: null!, refusal ?? string.Empty);
        }

        try
        {
            return action with { Kind = CodeActionHandler.RefactorImplementInterface, Edit = LspEditOf(answer.Document, answer.Version, answer.Edits).For(client) };
        }
        catch (WorkspaceEditNotSupportedException unsupported)
        {
            throw new RpcErrorException(RequestFailedCode, error: null!, unsupported.Message);
        }
    }

    // the action that moves the declaration of the variable named at a position closer to its usage. A client with a dialog of its own runs it as a
    // command, which asks the keyword of a variable of a module and the changes to accept; any other is sent the edit, with Static, when that changes
    // nothing. A refused one is offered disabled, with why, to a client that shows it so.
    private async Task<CodeAction?> CloserToUsageActionAsync(DocumentUri lspUri, SourcePosition position, CancellationToken token)
    {
        var documentUri = lspUri.ToUri();
        if (closerToUsage is null || await closerToUsage.OfferAsync(documentUri, position, token) is not { } offer)
        {
            return null;
        }

        var (title, refusal, changes) = offer;
        if (refusal is not null)
        {
            return ShowsDisabled ? new CodeAction { Title = title, Kind = CodeActionHandler.RefactorMoveCloserToUsage, Disabled = new CodeActionDisabled { Reason = refusal } } : null;
        }

        if (clientCapabilities.Expects(capabilities => capabilities.Refactoring))
        {
            var prepare = new RefactorPrepareParams { Kind = RefactoringKinds.MoveCloserToUsage, Uri = documentUri, Range = new SourceRange(position, position) };
            return new CodeAction
            {
                Title = title,
                Kind = CodeActionHandler.RefactorMoveCloserToUsage,
                Command = new Command { Name = RefactoringCommands.Refactor, Title = title, Arguments = new JArray(JToken.Parse(serializer.SerializeObject(prepare))) },
            };
        }

        if (changes)
        {
            return null;
        }

        var answer = await closerToUsage.ApplyAsync(documentUri, position, [], [], token);
        return answer.Edit is not null && ForTheClient(LspEditOf(answer.Edit)) is { } applicable
            ? new CodeAction { Title = title, Kind = CodeActionHandler.RefactorMoveCloserToUsage, Edit = applicable }
            : null;
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
    private static WorkspaceEdit LspEditOf(MemberMoveAnswer answer) => LspEditOf(answer.Document, answer.Version, answer.Edits!);

    // the edits of one document as LSP writes them, of the version they were made from.
    private static WorkspaceEdit LspEditOf(Uri document, int? version, IReadOnlyList<RefactorTextEdit> edits) => new()
    {
        DocumentChanges = new Container<WorkspaceEditDocumentChange>(new WorkspaceEditDocumentChange(new TextDocumentEdit
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.From(document), Version = version },
            Edits = new TextEditContainer(edits.Select(edit => new TextEdit
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(edit.Range.Start.Line, edit.Range.Start.Character, edit.Range.End.Line, edit.Range.End.Character),
                NewText = edit.NewText,
            })),
        })),
    };

    // what a client with no dialog extracts with: the defaults, but for the duplicates, which the user confirms each of where the client asks for change
    // annotations, and which are not replaced where it cannot.
    private IReadOnlyList<RefactorAnswer> DefaultAnswers()
        => client.TakesChangeAnnotations() ? [] : [new RefactorAnswer { Id = ExtractMethodService.ReplaceDuplicatesChoice, Value = "false" }];

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

    // the member of the data of an action a client resolves that holds the changes of what the code does its edit makes, each with its code and its message.
    private const string ChangesDataKey = "changes";

    // the annotation of the edits of a refactoring that changes what the code does; a duplicate's replacement is annotated by its own id, which is a position.
    private const string ChangeAnnotationId = "rdcore.change";

    // the changes of what the code does a prepared refactoring makes, which the user accepts or declines as a whole.
    private static IReadOnlyList<RefactorNotice> ChangesOf(RefactorPrepareResult prepared)
        => [.. prepared.Notices.Where(notice => notice.Kind is RefactorNoticeKind.Change)];

    // the changes an action was offered with (WithChanges): the document they were found in is the one it resolves, as it is of the version it was offered for.
    private static IReadOnlyList<RefactorNotice> ChangesOf(JObject offered)
        => offered[ChangesDataKey] is JArray changes
            ? [.. changes.OfType<JObject>().Select(change => new RefactorNotice
            {
                Kind = RefactorNoticeKind.Change, Code = (string?)change["code"] ?? string.Empty, Message = (string?)change["message"] ?? string.Empty,
            })]
            : [];

    private static JObject WithChanges(JObject data, IReadOnlyList<RefactorNotice> changes)
    {
        if (changes.Count > 0)
        {
            data[ChangesDataKey] = new JArray(changes.Select(change => new JObject { ["code"] = change.Code, ["message"] = change.Message }));
        }

        return data;
    }

    private static IReadOnlyList<string> CodesOf(IReadOnlyList<RefactorNotice> changes) => [.. changes.Select(change => change.Code)];

    // an edit that changes what the code does, as an edit the client asks its user to confirm before it applies it (LSP ChangeAnnotation.needsConfirmation):
    // each text edit carries one annotation, labeled with the refactoring and described with the changes, unless it is annotated already - a duplicate's
    // replacement keeps its own, which the user confirms by itself.
    private static WorkspaceEdit Confirmed(WorkspaceEdit edit, string label, IReadOnlyList<RefactorNotice> changes)
    {
        if (changes.Count == 0 || edit.DocumentChanges is not { } documents)
        {
            return edit;
        }

        var annotation = (ChangeAnnotationIdentifier)ChangeAnnotationId;
        return new WorkspaceEdit
        {
            DocumentChanges = new Container<WorkspaceEditDocumentChange>(documents.Select(document => document.TextDocumentEdit is { } text
                ? new WorkspaceEditDocumentChange(new TextDocumentEdit
                {
                    TextDocument = text.TextDocument,
                    Edits = new TextEditContainer(text.Edits.Select(each => each is AnnotatedTextEdit
                        ? each
                        : new AnnotatedTextEdit { Range = each.Range, NewText = each.NewText, AnnotationId = annotation })),
                })
                : document)),
            ChangeAnnotations = new Dictionary<ChangeAnnotationIdentifier, ChangeAnnotation>(
                edit.ChangeAnnotations ?? new Dictionary<ChangeAnnotationIdentifier, ChangeAnnotation>())
            {
                [annotation] = new ChangeAnnotation
                {
                    Label = label, Description = string.Join("\n", changes.Select(change => change.Message)), NeedsConfirmation = true,
                },
            },
        };
    }

    // the edit as the client applies it; an action whose edit the client can't apply is not offered - the rename command, which says why, still is.
    private WorkspaceEdit? ForTheClient(WorkspaceEdit edit)
    {
        try
        {
            return edit.For(client);
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
            Command = new RenameCommandArguments(document.ToString(), position.ToSourcePosition()).ToCommand(title),
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
                Kind = CodeActionHandler.SourceImportProject,
                Command = new Command
                {
                    Name = ServerCommands.ImportProject,
                    Title = title,
                    Arguments = new JArray(DocumentUri.FromFileSystemPath(vbp).ToString()),
                },
            };
        });
    }

    public async Task<CodeAction> ResolveAsync(CodeAction request, CancellationToken cancellationToken)
        => request.Edit is null && request.Data is JObject offered
            ? offered[ExtractionDataKey]?.ToString() switch
            {
                (RefactoringKinds.ExtractMethod or RefactoringKinds.ConvertGoSub) and var kind => await ResolveRefactoringAsync(request, kind, offered, cancellationToken),
                (RefactoringKinds.MoveMemberUp or RefactoringKinds.MoveMemberDown or RefactoringKinds.SortMembers) and var kind => await ResolveMoveAsync(request, kind, offered, cancellationToken),
                RefactoringKinds.EncapsulateField => await ResolveEncapsulationAsync(request, offered, cancellationToken),
                RefactoringKinds.ImplementInterface => await ResolveImplementationAsync(request, offered, cancellationToken),
                _ => request,
            }
            : request;
}
