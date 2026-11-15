using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Assistant;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server.Assistant;
using System.Globalization;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Assistant;

// the documents of the workspace as the tools name them: a path from the workspace root, and 1-based positions.
internal sealed class AssistantDocuments(IWorkspaceDocumentService documents, ISymbolSyncService symbols)
{
    // how long a tool waits for the workspace's first load before it says the workspace is still loading: an assistant asks again, where an answer read from
    // a workspace half loaded would be wrong.
    public static readonly TimeSpan DefaultLoadBound = TimeSpan.FromSeconds(30);

    public TimeSpan LoadBound { get; init; } = DefaultLoadBound;

    public string Root => documents.WorkspaceRoot;

    // the workspace as the server brought it up: every document parsed and its modules defined in the host, however the bring-up ended.
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
