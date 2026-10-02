using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using RDCore.LanguageServer.Workspace.Services;

namespace RDCore.LanguageServer.Server.Handlers.Document;

// The handlers of the document lifecycle (LSP 3.17 §Text Document Synchronization). Each only says what the client said: what the server does about it is the
// lifecycle service's, which keeps the protocol out of what it decides.

/// <summary>
/// <c>textDocument/didOpen</c>: the client opened a document, and from now on its text is the client's.
/// </summary>
internal sealed class DidOpenTextDocumentHandler(IDocumentLifecycleService lifecycle, TextDocumentSelector selector) : DidOpenTextDocumentHandlerBase
{
    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        lifecycle.Opened(request.TextDocument.Uri.ToUri(), request.TextDocument.Text, request.TextDocument.Version ?? 0);
        return Unit.Task;
    }

    protected override TextDocumentOpenRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        => new() { DocumentSelector = selector };
}

/// <summary>
/// <c>textDocument/didChange</c>: the client edited a document it has open.
/// </summary>
internal sealed class DidChangeTextDocumentHandler(IDocumentLifecycleService lifecycle, TextDocumentSelector selector) : DidChangeTextDocumentHandlerBase
{
    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        lifecycle.Changed(request.TextDocument.Uri.ToUri(), request.TextDocument.Version ?? 0, request.ContentChanges);
        return Unit.Task;
    }

    // incremental, which is what an editor sends so that it need not send the whole text with every key: the server applies both kinds all the same.
    protected override TextDocumentChangeRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        => new() { DocumentSelector = selector, SyncKind = TextDocumentSyncKind.Incremental };
}

/// <summary>
/// <c>textDocument/willSave</c>: the client is about to save a document.
/// </summary>
internal sealed class WillSaveTextDocumentHandler(IDocumentLifecycleService lifecycle, TextDocumentSelector selector) : WillSaveTextDocumentHandlerBase
{
    public override Task<Unit> Handle(WillSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        lifecycle.WillSave(request.TextDocument.Uri.ToUri(), request.Reason);
        return Unit.Task;
    }

    protected override TextDocumenWillSaveRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        => new() { DocumentSelector = selector };
}

/// <summary>
/// <c>textDocument/willSaveWaitUntil</c>: the client is about to save a document, and waits for the edits the server has for it to be saved with.
/// </summary>
internal sealed class WillSaveWaitUntilTextDocumentHandler(IDocumentLifecycleService lifecycle, TextDocumentSelector selector) : WillSaveWaitUntilTextDocumentHandlerBase
{
    public override async Task<TextEditContainer?> Handle(WillSaveWaitUntilTextDocumentParams request, CancellationToken cancellationToken)
        => new TextEditContainer(await lifecycle.WillSaveWaitUntilAsync(request.TextDocument.Uri.ToUri(), request.Reason, cancellationToken));

    protected override TextDocumentWillSaveWaitUntilRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        => new() { DocumentSelector = selector };
}

/// <summary>
/// <c>textDocument/didSave</c>: the client saved a document. The text is there when the client sends it, and is not when it does not: both are served.
/// </summary>
internal sealed class DidSaveTextDocumentHandler(IDocumentLifecycleService lifecycle, TextDocumentSelector selector) : DidSaveTextDocumentHandlerBase
{
    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        lifecycle.Saved(request.TextDocument.Uri.ToUri(), request.Text);
        return Unit.Task;
    }

    protected override TextDocumentSaveRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        => new() { DocumentSelector = selector, IncludeText = true };
}

/// <summary>
/// <c>textDocument/didClose</c>: the client closed a document, and its text is no longer the client's.
/// </summary>
internal sealed class DidCloseTextDocumentHandler(IDocumentLifecycleService lifecycle, TextDocumentSelector selector) : DidCloseTextDocumentHandlerBase
{
    public override async Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        await lifecycle.ClosedAsync(request.TextDocument.Uri.ToUri());
        return Unit.Value;
    }

    protected override TextDocumentCloseRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        => new() { DocumentSelector = selector };
}
