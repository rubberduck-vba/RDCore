using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Assistant;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server.Assistant;
using System.Globalization;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Assistant;

// the documents of the workspace as the tools name them: a path from the workspace root, and 1-based positions.
internal sealed class AssistantDocuments(IWorkspaceDocumentService documents)
{
    public string Root => documents.WorkspaceRoot;

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
