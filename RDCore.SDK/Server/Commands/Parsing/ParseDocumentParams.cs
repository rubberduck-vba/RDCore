namespace RDCore.SDK.Server.Commands.Parsing;

/// <summary>
/// The <em>parameter</em> object for a <see cref="ParseDocumentCommand"/>.
/// </summary>
public record class ParseDocumentParams
{
    /// <summary>
    /// The <c>Uri</c> of the document to parse.
    /// </summary>
    public Uri DocumentUri { get; init; } = default!;
}
