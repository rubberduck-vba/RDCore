using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.LanguageServer.Completion;

/// <summary>
/// Serves <c>textDocument/completion</c> (<strong>LSP 3.17</strong> §Completion Request): what can be written at the cursor of a document - the names in scope
/// there, the members of what a member is accessed on, the types a declaration can have, and the keywords of the language.
/// </summary>
/// <remarks>
/// <para>
/// A list is asked for as a name is typed and again after a <c>.</c> or a <c>!</c>, which are the trigger characters: a client narrows the list by what is typed
/// after that, so the whole list is answered, and it is complete. An item is whole as it is answered: there is nothing to resolve.
/// </para>
/// <para>
/// An argument whose parameter is an <c>Enum</c> is offered its constants first, the first of them preselected (<c>MsgBox "Hello", </c> offers
/// <c>VbMsgBoxStyle</c>'s). The list is asked for as such an argument starts, too: a <c>(</c>, a <c>,</c> and a space are trigger characters, and the list they
/// ask for is answered only there (<see cref="ICompletionService.ArgumentTriggerCharacters"/>).
/// </para>
/// </remarks>
internal sealed class CompletionHandler(ICompletionService completion) : CompletionHandlerBase
{
    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
        => completion.CompleteDocumentAsync(request.TextDocument.Uri.ToUri(), request.Position, cancellationToken,
            request.Context is { TriggerKind: CompletionTriggerKind.TriggerCharacter } context ? context.TriggerCharacter : null);

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken) => Task.FromResult(request);

    protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        TriggerCharacters = new Container<string>([".", "!", .. ICompletionService.ArgumentTriggerCharacters]),
        ResolveProvider = false,
    };
}
