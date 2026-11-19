using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Assistant;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Assistant;
using System.Globalization;
using System.Text;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Assistant;

// the tools that fix code: each fix is a code action of textDocument/codeAction, named by an id that says what it is (FixIds), found again when it is
// previewed or applied.

// what the tools do with the code actions of a document: the actions of a range, found again from an id, and their edits as each document's change.
internal sealed class FixToolEdits(AssistantDocuments documents, IDocumentDiagnosticsService diagnostics, IDocumentLifecycleService lifecycle, ICodeActionService actions,
    IWorkspaceDocumentService texts)
{
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

    // the document, synced, and its findings: a fix is of what the findings are now.
    public async Task<(WorkspaceDocument Document, IReadOnlyList<Diagnostic> Findings)> FindingsAsync(string path, CancellationToken token)
    {
        await documents.LoadedAsync(token);
        var document = documents.Resolve(path);
        var uri = document.Id.Uri.ToUri();
        await lifecycle.WhenSyncedAsync(uri, token);
        return (document, (await diagnostics.GetAsync(uri, previousResultId: null, token)).Diagnostics);
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

    // each document the edit changes, by path, with its change: the edits of its text, in order, and the path it is renamed to.
    public IReadOnlyList<ToolFileChange> FilesOf(WorkspaceEdit edit)
    {
        var changes = new Dictionary<Uri, (string Before, string After, Uri? RenamedTo)>();
        void Edit(DocumentUri document, IEnumerable<TextEdit> edits)
        {
            var uri = document.ToUri();
            var (before, after, renamedTo) = changes.TryGetValue(uri, out var change) ? change : (TextOf(uri), TextOf(uri), null);
            var refactorEdits = edits.Select(edit => new RefactorTextEdit { Range = SourceRangeOf(edit.Range), NewText = edit.NewText }).ToList();
            changes[uri] = (before, DeclarationEdits.ApplyTo(after, refactorEdits), renamedTo);
        }

        if (edit.DocumentChanges is { } documentChanges)
        {
            foreach (var change in documentChanges)
            {
                if (change.IsTextDocumentEdit)
                {
                    Edit(change.TextDocumentEdit!.TextDocument.Uri, change.TextDocumentEdit.Edits);
                }
                else if (change.IsRenameFile)
                {
                    var from = change.RenameFile!.OldUri.ToUri();
                    var (before, after, _) = changes.TryGetValue(from, out var text) ? text : (TextOf(from), TextOf(from), null);
                    changes[from] = (before, after, change.RenameFile.NewUri.ToUri());
                }
                else if (change.IsCreateFile)
                {
                    changes[change.CreateFile!.Uri.ToUri()] = (string.Empty, string.Empty, null);
                }
                else if (change.IsDeleteFile)
                {
                    var deleted = change.DeleteFile!.Uri.ToUri();
                    changes[deleted] = (changes.TryGetValue(deleted, out var text) ? text.Before : TextOf(deleted), string.Empty, null);
                }
            }
        }
        else if (edit.Changes is { } byDocument)
        {
            foreach (var (document, edits) in byDocument)
            {
                Edit(document, edits);
            }
        }

        return changes
            .Select(change => new ToolFileChange
            {
                Path = documents.PathOf(change.Key),
                NewPath = change.Value.RenamedTo is { } renamedTo ? documents.PathOf(renamedTo) : null,
                Diff = UnifiedDiff.Of(documents.PathOf(change.Value.RenamedTo ?? change.Key), change.Value.Before, change.Value.After),
            })
            .OrderBy(change => change.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // the questions an edit asks before it is applied: its change annotations that need a confirmation.
    public static IReadOnlyList<ToolEditQuestion> QuestionsOf(WorkspaceEdit edit)
        => edit.ChangeAnnotations is { } annotations
            ? [.. annotations
                .Where(annotation => annotation.Value.NeedsConfirmation)
                .Select(annotation => new ToolEditQuestion { Id = annotation.Key.Identifier, Label = annotation.Value.Label, Description = annotation.Value.Description })]
            : [];

    private string TextOf(Uri document) => texts.TryGetDocument(document, out var held) ? held.Text : string.Empty;

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
        return new PreviewFixToolResult { Title = action.Title, Files = fixes.FilesOf(action.Edit!), Questions = FixToolEdits.QuestionsOf(action.Edit!) };
    }

    protected override string Render(PreviewFixToolResult result, CultureInfo culture) => FixToolText.Of(result.Title, result.Files, result.Questions, culture);
}

// the text of an edit: its title, each document's change, and the questions it asks.
internal static class FixToolText
{
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
