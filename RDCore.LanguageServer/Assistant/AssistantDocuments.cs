using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Assistant;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server.Assistant;
using System.Globalization;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Assistant;

// the documents of the workspace as the tools name them: a path from the workspace root, and 1-based positions.
internal sealed class AssistantDocuments(IWorkspaceDocumentService documents, ISymbolSyncService symbols)
{
    // how long a tool waits for the workspace's first load before it says the workspace is still loading: an assistant asks again, where an answer read from
    // a half-loaded workspace would be wrong.
    public static readonly TimeSpan DefaultLoadBound = TimeSpan.FromSeconds(30);

    public TimeSpan LoadBound { get; init; } = DefaultLoadBound;

    public string Root => documents.WorkspaceRoot;

    // waits for the workspace as the server brings it up (every document parsed and its modules defined in the host, however the bring-up ends); past
    // LoadBound, the tool is refused with WorkspaceLoading.
    public async Task LoadedAsync(CancellationToken token)
    {
        try
        {
            await symbols.WorkspaceDefinedAsync(token).WaitAsync(LoadBound, token);
        }
        catch (TimeoutException)
        {
            throw new AssistantToolRefusedException(AssistantToolTexts.WorkspaceLoading);
        }
    }

    public IEnumerable<WorkspaceDocument> All => documents.GetAllDocuments().OrderBy(document => PathOf(document.Id.Uri), StringComparer.OrdinalIgnoreCase);

    public WorkspaceDocument Resolve(string path)
    {
        Uri uri;
        try
        {
            uri = TextLocation.UriOf(Root, path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw Refused(AssistantToolTexts.NoSuchDocument, path);
        }

        return documents.TryGetDocument(uri, out var document) ? document : throw Refused(AssistantToolTexts.NoSuchDocument, path);
    }

    public string PathOf(DocumentUri uri) => TextLocation.PathOf(Root, uri.ToUri());

    public TextLocation Locate(DocumentUri uri, LspRange range)
        => TextLocation.Of(Root, uri.ToUri(), new SourceRange(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character));

    // a finding as the tools say it: where it is, its code and severity, and whether something fixes it.
    public ToolDiagnostic Project(DocumentUri uri, Diagnostic diagnostic) => new()
    {
        Location = Locate(uri, diagnostic.Range),
        Code = diagnostic.Code?.String ?? diagnostic.Code?.Long.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        Severity = diagnostic.Severity switch
        {
            DiagnosticSeverity.Warning => ToolDiagnosticSeverity.Warning,
            DiagnosticSeverity.Information => ToolDiagnosticSeverity.Information,
            DiagnosticSeverity.Hint => ToolDiagnosticSeverity.Hint,
            _ => ToolDiagnosticSeverity.Error,
        },
        Message = diagnostic.Message,
        HelpUrl = diagnostic.CodeDescription?.Href?.ToString(),
        HasFix = DiagnosticFixes.Of(diagnostic) is { } fixes && (fixes.Edits.Count > 0 || fixes.Rename is not null || fixes.Refactoring is not null || fixes.Project is not null),
    };

    public static Position PositionOf(PositionToolArguments arguments)
    {
        if (arguments.Line < 1 || arguments.Column < 1)
        {
            throw new AssistantToolRefusedException(AssistantToolTexts.PositionNotValid);
        }

        var position = arguments.Position.ToSourcePosition();
        return new Position(position.Line, position.Character);
    }

    public static AssistantToolRefusedException Refused(string format, params object[] arguments)
        => new(string.Format(CultureInfo.CurrentUICulture, format, arguments));
}
