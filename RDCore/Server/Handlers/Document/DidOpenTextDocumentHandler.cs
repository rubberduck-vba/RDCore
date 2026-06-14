using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace.States;

namespace RDCore.LanguageServer.Server.Handlers.Document;

internal class DidOpenTextDocumentHandler(IDocumentStateProvider state, TextDocumentSelector selector) : DidOpenTextDocumentHandlerBase
{
    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        state.OnDocumentOpened(new TextDocumentIdentifier(request.TextDocument.Uri));
        return Task.FromResult(Unit.Value);
    }

    protected override TextDocumentOpenRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = selector };
}