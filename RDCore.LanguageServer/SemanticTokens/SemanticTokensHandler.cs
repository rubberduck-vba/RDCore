using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server;

namespace RDCore.LanguageServer.SemanticTokens;

/// <summary>
/// Serves <c>textDocument/semanticTokens/full</c> (<strong>LSP 3.17</strong> §Semantic Tokens), which is how a client colours a document by what its text is: the
/// keywords, the comments, the literals, and the names of what the module declares.
/// </summary>
/// <remarks>
/// Ranges are not served: the tokens of the whole document are cheap to give, and OmniSharp's builder encoded the first token of a range relative to the range
/// rather than to the start of the document, as LSP has it, so a client put it on the wrong line (HexIDE's findings desk #25). Deltas are not served either; a
/// client that asks for the document again gets the document.
/// </remarks>
internal sealed class SemanticTokensHandler(ISemanticTokensService tokens, IncomingDocumentChanges incoming) : SemanticTokensHandlerBase
{
    protected override async Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        // a change to the document that arrives meanwhile ends the request, answered ContentModified: the client asks again for the text it has now.
        var document = identifier.TextDocument.Uri.ToUri();
        foreach (var token in await incoming.WhileCurrentAsync(document, cancellationToken, token => tokens.GetAsync(document, token)))
        {
            builder.Push(token.Line, token.Character, token.Length, token.Type, token.Modifiers);
        }
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
        => Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            Legend = new SemanticTokensLegend
            {
                TokenTypes = new Container<SemanticTokenType>(SemanticTokenLegend.TokenTypes.Select(type => new SemanticTokenType(type))),
                TokenModifiers = new Container<SemanticTokenModifier>(SemanticTokenLegend.TokenModifiers.Select(modifier => new SemanticTokenModifier(modifier))),
            },
            Full = new SemanticTokensCapabilityRequestFull { Delta = false },
            Range = false,
        };
}
