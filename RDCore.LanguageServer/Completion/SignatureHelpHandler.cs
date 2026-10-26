using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.LanguageServer.Completion;

/// <summary>
/// Serves <c>textDocument/signatureHelp</c> (<strong>LSP 3.17</strong> §Signature Help): what the call being written at the cursor of a document takes - the
/// signature of what is called, the parameter the argument at the cursor is given to, and what they do.
/// </summary>
/// <remarks>
/// A signature help is asked for as an argument list opens and as an argument starts: after a <c>(</c>, a <c>,</c>, and the space after the name of a call
/// statement, which takes its arguments without parentheses. A <c>)</c> asks again, for the call around the one it closes.
/// </remarks>
internal sealed class SignatureHelpHandler(ICompletionService completion) : SignatureHelpHandlerBase
{
    public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
        => completion.SignatureHelpDocumentAsync(request.TextDocument.Uri.ToUri(), request.Position, cancellationToken);

    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(SignatureHelpCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        TriggerCharacters = new Container<string>("(", ",", " "),
        RetriggerCharacters = new Container<string>(")"),
    };
}
