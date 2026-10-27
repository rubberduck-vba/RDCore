using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Serves <c>textDocument/inlayHint</c> (<strong>LSP 3.17</strong> §Inlay Hint Request): before each positional argument of the calls in the range a client
/// shows, the name of the parameter it is given to (<see cref="ParameterHints"/>).
/// </summary>
/// <remarks>
/// Each hint is of kind <see cref="InlayHintKind.Parameter"/>, its label the name and a colon, padded on its right. Every hint is complete as it is answered:
/// there is nothing to resolve.
/// </remarks>
internal sealed class InlayHintHandler(IRenameService workspace) : InlayHintsHandlerBase
{
    public override async Task<InlayHintContainer?> Handle(InlayHintParams request, CancellationToken cancellationToken)
        => await workspace.ComposeAsync(cancellationToken) is { } composed
            ? new InlayHintContainer(composed.ParameterHints(request.TextDocument.Uri.ToUri(), request.Range).Select(hint => new InlayHint
            {
                Position = hint.Position,
                Label = new StringOrInlayHintLabelParts($"{hint.Name}:"),
                Kind = InlayHintKind.Parameter,
                PaddingRight = true,
            }))
            : new InlayHintContainer();

    // a hint is answered whole: there is no inlayHint/resolve.
    public override Task<InlayHint> Handle(InlayHint request, CancellationToken cancellationToken) => Task.FromResult(request);

    protected override InlayHintRegistrationOptions CreateRegistrationOptions(InlayHintClientCapabilities capability, ClientCapabilities clientCapabilities)
        => new() { ResolveProvider = false };
}
