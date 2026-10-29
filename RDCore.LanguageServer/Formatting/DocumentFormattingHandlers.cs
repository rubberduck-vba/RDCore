using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Server;

namespace RDCore.LanguageServer.Formatting;

/// <summary>
/// <c>textDocument/formatting</c> (<strong>LSP 3.17</strong> §Document Formatting Request): the edits that indent every line of a document.
/// </summary>
/// <remarks>
/// The settings are the server's (<c>Formatting</c>), and not the request's <c>FormattingOptions</c>: the indentation follows the editor's tab size when no
/// indentation is set, through the configuration the client owns. A change to the document that arrives meanwhile ends the request, answered
/// <c>ContentModified</c>: edits of a text that is not there any more would put the wrong lines in the wrong places.
/// </remarks>
internal sealed class DocumentFormattingHandler(ICodeFormattingService formatting, TextDocumentSelector selector, IncomingDocumentChanges incoming)
    : DocumentFormattingHandlerBase
{
    public override async Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken)
    {
        var document = request.TextDocument.Uri.ToUri();
        var edits = await incoming.WhileCurrentAsync(document, cancellationToken, token => formatting.FormatAsync(document, lines: null, token));
        return new TextEditContainer(edits);
    }

    protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(DocumentFormattingCapability capability, ClientCapabilities clientCapabilities)
        => new() { DocumentSelector = selector };
}

/// <summary>
/// <c>textDocument/rangeFormatting</c> (<strong>LSP 3.17</strong> §Document Range Formatting Request): the edits that indent the lines a range is on.
/// </summary>
/// <remarks>
/// A line is indented by what encloses it in the whole module, whatever the range: the range says which lines are given edits. A range that ends at the start
/// of a line - a selection of whole lines - does not take in that line.
/// </remarks>
internal sealed class DocumentRangeFormattingHandler(ICodeFormattingService formatting, TextDocumentSelector selector, IncomingDocumentChanges incoming)
    : DocumentRangeFormattingHandlerBase
{
    public override async Task<TextEditContainer> Handle(DocumentRangeFormattingParams request, CancellationToken cancellationToken)
    {
        var document = request.TextDocument.Uri.ToUri();
        var (start, end) = (request.Range.Start, request.Range.End);
        var last = end.Character == 0 && end.Line > start.Line ? end.Line - 1 : end.Line;
        var edits = await incoming.WhileCurrentAsync(document, cancellationToken, token => formatting.FormatAsync(document, (start.Line, last), token));
        return new TextEditContainer(edits);
    }

    protected override DocumentRangeFormattingRegistrationOptions CreateRegistrationOptions(DocumentRangeFormattingCapability capability, ClientCapabilities clientCapabilities)
        => new() { DocumentSelector = selector };
}
