using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace.States;

namespace RDCore.LanguageServer.Server.Handlers.Document;

internal class DidCloseTextDocumentHandler(IDocumentStateProvider state, TextDocumentSelector selector) : DidCloseTextDocumentHandlerBase
{
    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        state.OnDocumentClosed(new TextDocumentIdentifier(request.TextDocument.Uri));
        return Task.FromResult(Unit.Value);
    }

    protected override TextDocumentCloseRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = selector };
}