using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace.Services;

namespace RDCore.LanguageServer.Server.Handlers.Document;

internal class DidChangeTextDocumentHandler(IWorkspaceDocumentService service, TextDocumentSelector selector) : DidChangeTextDocumentHandlerBase
{
    public async override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        var id = new TextDocumentIdentifier(request.TextDocument.Uri);
        foreach (var change in request.ContentChanges)
        {
            if (change.Range is not null)
            {
                service.Edit(id, change.Range, change.RangeLength, change.Text);
            }
            else
            {
                service.Edit(id, change.Text);
                break;
            }
        }

        // TODO parse modified file from here?

        return Unit.Value;
    }

    protected override TextDocumentChangeRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = selector };
}