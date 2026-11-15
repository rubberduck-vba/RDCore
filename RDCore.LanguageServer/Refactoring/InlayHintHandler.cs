using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Serves <c>textDocument/inlayHint</c> (<strong>LSP 3.17</strong> §Inlay Hint Request): the hints of the range a client shows, each in square brackets at the
/// place it describes (<see cref="SourceInlayHints"/>), of the places the settings show (<see cref="SdkInlayHintOptions"/>).
/// </summary>
/// <remarks>
/// The settings are read for each request: a change is the next request's. Every hint is complete as it is answered: there is nothing to resolve.
/// </remarks>
internal sealed class InlayHintHandler(IRenameService workspace, IOptions<SdkAppOptions> options) : InlayHintsHandlerBase
{
    public override async Task<InlayHintContainer?> Handle(InlayHintParams request, CancellationToken cancellationToken)
    {
        if (await workspace.ComposeAsync(cancellationToken) is not { } composed)
        {
            return new InlayHintContainer();
        }

        return new InlayHintContainer(composed.InlayHints(request.TextDocument.Uri.ToUri(), request.Range, options.Value.InlayHints)
            .OrderBy(hint => hint.Position.Line).ThenBy(hint => hint.Position.Character));
    }

    // a hint is answered whole: there is no inlayHint/resolve.
    public override Task<InlayHint> Handle(InlayHint request, CancellationToken cancellationToken) => Task.FromResult(request);

    protected override InlayHintRegistrationOptions CreateRegistrationOptions(InlayHintClientCapabilities capability, ClientCapabilities clientCapabilities)
        => new() { ResolveProvider = false };
}
