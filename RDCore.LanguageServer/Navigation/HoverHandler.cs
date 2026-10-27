using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Refactoring;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Navigation;

/// <summary>
/// Serves <c>textDocument/hover</c> (<strong>LSP 3.17</strong> §Hover Request): the declaration of the symbol named at a position, what declares it, and
/// its doc string (<see cref="SymbolHover"/>), over the name.
/// </summary>
/// <remarks>
/// The name is bound the way a rename binds it (<see cref="SymbolNavigation.SymbolAt"/>): a symbol of the workspace or of a library it references. The hover
/// is Markdown - the declaration in a block of VBA - for a client that reads it, and plain text for one that does not; no name, or a name whose meaning is not
/// known, is answered with nothing.
/// </remarks>
internal sealed class HoverHandler(IRenameService workspace) : HoverHandlerBase
{
    private bool _markdown = true;

    public override async Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        if (await workspace.ComposeAsync(cancellationToken) is not { } composed
            || composed.Hover(request.TextDocument.Uri.ToUri(), request.Position) is not { } found)
        {
            return null;
        }

        var (hover, range) = found;
        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(_markdown
                ? new MarkupContent { Kind = MarkupKind.Markdown, Value = hover.ToMarkdown() }
                : new MarkupContent { Kind = MarkupKind.PlainText, Value = hover.ToPlainText() }),
            Range = range,
        };
    }

    // a client that says which formats it reads, and does not say Markdown, is answered in plain text; one that says nothing of hovers is given no
    // capability at all, and a throw here would leave initialize unanswered.
    protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        _markdown = capability?.ContentFormat is not { } formats || !formats.Any() || formats.Contains(MarkupKind.Markdown);
        return new HoverRegistrationOptions();
    }
}
