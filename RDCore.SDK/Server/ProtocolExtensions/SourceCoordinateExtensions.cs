using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Source;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Server.ProtocolExtensions;

/// <summary>
/// Converts between the model's source-coordinate types and their LSP protocol equivalents at the protocol boundary.
/// </summary>
public static class SourceCoordinateExtensions
{
    public static Position ToLsp(this SourcePosition position) => new(position.Line, position.Character);
    public static LspRange ToLsp(this SourceRange range) => new(range.Start.ToLsp(), range.End.ToLsp());
    public static Location ToLsp(this SourceLocation location) => new() { Uri = location.Uri, Range = location.Range.ToLsp() };

    public static SourcePosition ToSourcePosition(this Position position) => new(position.Line, position.Character);
    public static SourceRange ToSourceRange(this LspRange range) => new(range.Start.ToSourcePosition(), range.End.ToSourcePosition());
    public static SourceLocation ToSourceLocation(this Location location) => new(location.Uri.ToUri(), location.Range.ToSourceRange());
}
