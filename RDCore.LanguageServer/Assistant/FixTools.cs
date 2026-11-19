using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Assistant;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server.Assistant;
using RDCore.SDK.Server.ProtocolExtensions;
using System.Globalization;
using System.IO.Abstractions;
using System.Text;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Assistant;

// the tools that fix code: each fix is a code action of textDocument/codeAction, named by an id that says what it is (FixIds), found again when it is
// previewed or applied.

// what the tools do with the code actions of a document: the actions of a range, found again from an id, and their edits as each document's change.
internal sealed class FixToolEdits(AssistantDocuments documents, IDocumentDiagnosticsService diagnostics, IDocumentLifecycleService lifecycle, ICodeActionService actions,
    IWorkspaceDocumentService texts, IFileSystem fileSystem, IClientEdits client)
{
    // what the client the edits are applied by said it can do.
    public ClientCapabilities? ClientCapabilities => client.Capabilities;

    // what the tools say they can do, whatever the client said: edits in documentChanges, with their change annotations (the questions of an edit) and file
    // renames; a refactoring is listed without its edit, which is made when it is previewed or applied.
    public static ClientCapabilities Client { get; } = new()
    {
        TextDocument = new TextDocumentClientCapabilities
        {
            CodeAction = new CodeActionCapability
            {
                DataSupport = true,
                ResolveSupport = new CodeActionCapabilityResolveSupportOptions { Properties = new Container<string>("edit") },
                IsPreferredSupport = true,
            },
        },
        Workspace = new WorkspaceClientCapabilities
        {
            WorkspaceEdit = new WorkspaceEditCapability
            {
                DocumentChanges = true,
                ChangeAnnotationSupport = new WorkspaceEditSupportCapabilitiesChangeAnnotationSupport { GroupsOnLabel = false },
                ResourceOperations = new Container<ResourceOperationKind>(ResourceOperationKind.Rename),
            },
        },
    };

    // the document, once the workspace is loaded and the host has its edits.
    public async Task<WorkspaceDocument> SyncedAsync(string path, CancellationToken token)
    {
        await documents.LoadedAsync(token);
        var document = documents.Resolve(path);
        await lifecycle.WhenSyncedAsync(document.Id.Uri.ToUri(), token);
        return document;
    }

    // the document, synced, and its findings: a fix is of what the findings are now.
    public async Task<(WorkspaceDocument Document, IReadOnlyList<Diagnostic> Findings)> FindingsAsync(string path, CancellationToken token)
    {
        var document = await SyncedAsync(path, token);
        return (document, (await diagnostics.GetAsync(document.Id.Uri.ToUri(), previousResultId: null, token)).Diagnostics);
    }

    // the edit applied by the client, as its questions are answered: an edit that asks one, called with no answers, is not applied, and says its questions.
    public async Task<EditToolResult> ApplyAsync(string label, WorkspaceEdit edit, IReadOnlyList<string>? accept, CancellationToken token)
    {
        var questions = ToolEdits.QuestionsOf(edit);
        if (questions.Count > 0 && accept is null)
        {
            return new EditToolResult { Files = FilesOf(edit), Questions = questions };
        }

        var answered = edit.Answered(accept ?? []);
        var files = FilesOf(answered);
        if (files.Count == 0)
        {
            return new EditToolResult();
        }

        if (!client.Capabilities.AppliesEdits())
        {
            throw new AssistantToolRefusedException(AssistantToolTexts.ClientAppliesNoEdits);
        }

        WorkspaceEdit sent;
        try
        {
            sent = answered.For(client.Capabilities);
        }
        catch (WorkspaceEditNotSupportedException refused)
        {
            throw new AssistantToolRefusedException(refused.Message);
        }

        var applied = await client.ApplyAsync(label, sent, token);
        return applied.Applied
            ? new EditToolResult { Applied = true, Files = files }
            : throw AssistantDocuments.Refused(AssistantToolTexts.EditNotApplied, applied.FailureReason ?? string.Empty);
    }

    // the actions of a range, asked with the findings it holds, as an editor asks for them.
    public async Task<IReadOnlyList<CodeAction>> ActionsAsync(WorkspaceDocument document, IReadOnlyList<Diagnostic> findings, LspRange range, CancellationToken token)
    {
        var request = new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(document.Id.Uri),
            Range = range,
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>(findings.Where(finding => Overlaps(finding.Range, range))) },
        };
        return await actions.ActionsAsync(request, Client, token);
    }

    // the fix an id names, with its edit; refused when the id is not one, the document changed since it was listed, or the fix is no edit.
    public async Task<(CodeAction Action, WorkspaceDocument Document)> FindAsync(string fixId, CancellationToken token)
    {
        if (!FixIds.TryRead(fixId, out var fix))
        {
            throw AssistantDocuments.Refused(AssistantToolTexts.FixNotFound, fixId);
        }

        var (document, findings) = await FindingsAsync(fix.Path, token);
        if (document.Version != fix.Version)
        {
            throw new AssistantToolRefusedException(AssistantToolTexts.FixStale);
        }

        var range = LspRangeOf(fix.Range.ToSourceRange());
        var action = (await ActionsAsync(document, findings, range, token)).FirstOrDefault(action => KindOf(action) == fix.Kind && action.Title == fix.Title)
            ?? throw new AssistantToolRefusedException(AssistantToolTexts.FixStale);
        if (action.Edit is null && action.Data is not null)
        {
            action = await actions.ResolveAsync(action, Client, token);
        }

        return action.Edit is null ? throw AssistantDocuments.Refused(AssistantToolTexts.FixHasNoEdit, action.Title) : (action, document);
    }

    public ToolFix Project(CodeAction action, WorkspaceDocument document, LspRange range) => new()
    {
        FixId = FixIds.Of(new FixIdParts(documents.PathOf(document.Id.Uri), document.Version, TextRange.Of(SourceRangeOf(range)), KindOf(action), action.Title)),
        Title = action.Title,
        Kind = KindOf(action),
        IsPreferred = action.IsPreferred,
        HasEdit = action.Edit is not null || action.Data is not null,
        Diagnostic = action.Diagnostics?.FirstOrDefault() is { } diagnostic ? documents.Project(document.Id.Uri, diagnostic) : null,
    };

    // each document the edit changes, by path, with its change.
    public IReadOnlyList<ToolFileChange> FilesOf(WorkspaceEdit edit) => ToolEdits.FilesOf(edit, TextOf, documents.PathOf);

    // the text of a document as the server holds it; one it does not hold (the project file) as its file has it.
    private string? TextOf(DocumentUri document)
    {
        if (texts.TryGetDocument(document.ToUri(), out var held))
        {
            return held.Text;
        }

        var path = document.GetFileSystemPath();
        return fileSystem.File.Exists(path) ? fileSystem.File.ReadAllText(path) : null;
    }

    private static string KindOf(CodeAction action) => action.Kind?.Name ?? string.Empty;

    public static LspRange LspRangeOf(SourceRange range) => new(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);

    private static SourceRange SourceRangeOf(LspRange range) => new(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);

    // two ranges overlap when neither ends before the other starts; an empty range at a finding's edge touches it.
    private static bool Overlaps(LspRange left, LspRange right) => !(left.End < right.Start || right.End < left.Start);
}

internal sealed class ListFixesTool(AssistantDocuments documents, FixToolEdits fixes) : AssistantTool<ListFixesToolArguments, ListFixesToolResult>
{
    public override string Name => CoreAssistantTools.ListFixes;

    protected override ToolAnnotations Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string? TitleOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.ListFixes_Title), culture);

    protected override string DescriptionOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.ListFixes_Description), culture)!;

    protected override string? ArgumentDescriptionOf(string argument, CultureInfo culture) => argument switch
    {
        "endLine" => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.ListFixes_EndLine), culture),
        "endColumn" => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.ListFixes_EndColumn), culture),
        "line" => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.ListFixes_Line), culture),
        "column" => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.ListFixes_Column), culture),
        _ => ReadToolText.PositionArgument(argument, culture),
    };

    protected override async Task<ListFixesToolResult> RunAsync(ListFixesToolArguments arguments, CancellationToken token)
    {
        if (arguments is { Line: < 1 } or { Column: < 1 } or { EndLine: < 1 } or { EndColumn: < 1 })
        {
            throw new AssistantToolRefusedException(AssistantToolTexts.PositionNotValid);
        }

        var (document, findings) = await fixes.FindingsAsync(arguments.Path, token);

        // with no range, the fixes of each finding: what is asked at its range that fixes something.
        var ranges = arguments.Range is { } range
            ? [FixToolEdits.LspRangeOf(range.ToSourceRange())]
            : findings.Select(finding => finding.Range).Distinct().ToList();
        var found = new List<ToolFix>();
        foreach (var asked in ranges)
        {
            found.AddRange((await fixes.ActionsAsync(document, findings, asked, token))
                .Where(action => arguments.Range is not null || action.Diagnostics?.Any() == true)
                .Select(action => fixes.Project(action, document, asked)));
        }

        var capped = ToolResultLimits.Cap(found.DistinctBy(fix => fix.FixId), ToolResultLimits.Fixes);
        return new ListFixesToolResult { Fixes = capped.Items, Total = capped.Total, Truncated = capped.Truncated };
    }

    protected override string Render(ListFixesToolResult result, CultureInfo culture)
    {
        if (result.Total == 0)
        {
            return AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.NoFixes), culture)!;
        }

        var preferred = AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.FixPreferred), culture);
        var noEdit = AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.FixRunByCommand), culture);
        var fixesFinding = AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.FixFixes), culture)!;
        var text = new StringBuilder();
        foreach (var fix in result.Fixes)
        {
            text.Append(CultureInfo.InvariantCulture, $"{fix.Kind}: {fix.Title}");
            text.Append(fix.IsPreferred ? $" ({preferred})" : string.Empty).Append(fix.HasEdit ? string.Empty : $" ({noEdit})");
            text.Append(fix.Diagnostic is { } diagnostic ? " " + string.Format(culture, fixesFinding, diagnostic.Code, diagnostic.Location) : string.Empty);
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"  fixId: {fix.FixId}");
        }

        return ReadToolText.End(text, new CappedList<ToolFix>(result.Fixes, result.Total), culture);
    }
}

internal sealed class PreviewFixTool(FixToolEdits fixes) : AssistantTool<FixToolArguments, PreviewFixToolResult>
{
    public override string Name => CoreAssistantTools.PreviewFix;

    protected override ToolAnnotations Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string? TitleOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.PreviewFix_Title), culture);

    protected override string DescriptionOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.PreviewFix_Description), culture)!;

    protected override string? ArgumentDescriptionOf(string argument, CultureInfo culture)
        => argument == "fixId" ? AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Fix_FixId), culture) : null;

    protected override async Task<PreviewFixToolResult> RunAsync(FixToolArguments arguments, CancellationToken token)
    {
        var (action, _) = await fixes.FindAsync(arguments.FixId, token);
        return new PreviewFixToolResult { Title = action.Title, Files = fixes.FilesOf(action.Edit!), Questions = ToolEdits.QuestionsOf(action.Edit!) };
    }

    protected override string Render(PreviewFixToolResult result, CultureInfo culture) => FixToolText.Of(result.Title, result.Files, result.Questions, culture);
}

internal sealed class ApplyFixTool(FixToolEdits fixes) : AssistantTool<ApplyFixToolArguments, EditToolResult>
{
    public override string Name => CoreAssistantTools.ApplyFix;

    protected override ToolAnnotations Annotations => new() { DestructiveHint = true };

    protected override string? TitleOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.ApplyFix_Title), culture);

    protected override string DescriptionOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.ApplyFix_Description), culture)!;

    protected override string? ArgumentDescriptionOf(string argument, CultureInfo culture) => argument switch
    {
        "fixId" => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Fix_FixId), culture),
        "accept" => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Edit_Accept), culture),
        _ => null,
    };

    protected override async Task<EditToolResult> RunAsync(ApplyFixToolArguments arguments, CancellationToken token)
    {
        var (action, _) = await fixes.FindAsync(arguments.FixId, token);
        return await fixes.ApplyAsync(action.Title, action.Edit!, arguments.Accept, token);
    }

    protected override string Render(EditToolResult result, CultureInfo culture) => FixToolText.Of(result, culture);
}

internal sealed class RenameTool(AssistantDocuments documents, IRenameService rename, FixToolEdits fixes) : AssistantTool<RenameToolArguments, EditToolResult>
{
    public override string Name => CoreAssistantTools.Rename;

    protected override ToolAnnotations Annotations => new() { DestructiveHint = true };

    protected override string? TitleOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Rename_Title), culture);

    protected override string DescriptionOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Rename_Description), culture)!;

    protected override string? ArgumentDescriptionOf(string argument, CultureInfo culture) => argument switch
    {
        "newName" => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Rename_NewName), culture),
        "accept" => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Edit_Accept), culture),
        _ => ReadToolText.PositionArgument(argument, culture),
    };

    protected override async Task<EditToolResult> RunAsync(RenameToolArguments arguments, CancellationToken token)
    {
        var position = AssistantDocuments.PositionOf(arguments);
        var document = await fixes.SyncedAsync(arguments.Path, token);
        var answer = await rename.RenameAsync(document.Id.Uri.ToUri(), position, arguments.NewName, token);
        var edit = answer.Value ?? throw new AssistantToolRefusedException(answer.Refusal ?? string.Empty);

        // a module is named by its VB_Name: a client that does not rename files keeps the file's name, and is sent the rest of the edit, as a rename asked of it is.
        if (!fixes.ClientCapabilities.TakesResourceOperation(ResourceOperationKind.Rename))
        {
            edit = edit.WithoutFileRenames();
        }

        var label = string.Format(CultureInfo.CurrentUICulture, AssistantToolTexts.RenameLabel, arguments.NewName);
        return await fixes.ApplyAsync(label, edit, arguments.Accept, token);
    }

    protected override string Render(EditToolResult result, CultureInfo culture) => FixToolText.Of(result, culture);
}

// the client the server serves, which applies the edits of the tools that change code (workspace/applyEdit): the server never writes the user's files itself.
internal interface IClientEdits
{
    // what the client said it can do, as it initialized; none before it did.
    ClientCapabilities? Capabilities { get; }

    Task<ApplyWorkspaceEditResponse> ApplyAsync(string label, WorkspaceEdit edit, CancellationToken token);
}

internal sealed class ClientEdits(Func<ILanguageServerFacade?> server) : IClientEdits
{
    public ClientCapabilities? Capabilities => server()?.ClientSettings?.Capabilities;

    public Task<ApplyWorkspaceEditResponse> ApplyAsync(string label, WorkspaceEdit edit, CancellationToken token)
        => (server() ?? throw new InvalidOperationException()).Workspace.ApplyWorkspaceEdit(new ApplyWorkspaceEditParams { Label = label, Edit = edit }, token);
}

// the text of an edit: its title, each document's change, and the questions it asks.
internal static class FixToolText
{
    public static string Of(EditToolResult result, CultureInfo culture)
    {
        var said = result switch
        {
            { Applied: true } => nameof(AssistantToolTexts.EditApplied),
            { Questions.Count: > 0 } => nameof(AssistantToolTexts.EditNotAppliedAsks),
            _ => nameof(AssistantToolTexts.NothingToApply),
        };
        return Of(AssistantToolTexts.ResourceManager.GetString(said, culture)!, result.Files, result.Questions, culture);
    }

    public static string Of(string title, IReadOnlyList<ToolFileChange> files, IReadOnlyList<ToolEditQuestion> questions, CultureInfo culture)
    {
        var text = new StringBuilder().AppendLine(title);
        foreach (var file in files)
        {
            if (file.NewPath is { } newPath)
            {
                text.AppendLine(string.Format(culture, AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.FileRenamed), culture)!, file.Path, newPath));
            }

            text.Append(file.Diff);
        }

        if (questions.Count > 0)
        {
            text.AppendLine(AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.EditAsks), culture));
            foreach (var question in questions)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  {question.Id}: {question.Label}{(question.Description is { } description ? $" ({description})" : string.Empty)}");
            }
        }

        return ToolResultLimits.CapText(text.ToString().TrimEnd(), culture);
    }
}
