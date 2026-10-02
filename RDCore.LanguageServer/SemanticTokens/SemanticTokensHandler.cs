using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Source;

namespace RDCore.LanguageServer.SemanticTokens;

/// <summary>
/// Serves <c>textDocument/semanticTokens/full</c> and <c>/range</c> (<strong>LSP 3.17</strong> §Semantic Tokens), which is how a client colours a document by what its
/// text is: the keywords, the comments, the literals, and the names of what the module declares.
/// </summary>
/// <remarks>
/// A range is answered with the tokens of the whole document: the tokens are cheap to give and the client is only ever shown the ones that are in view. Deltas are
/// not served; a client that asks for the document again gets the document.
/// </remarks>
internal sealed class SemanticTokensHandler(ISemanticTokensService tokens) : SemanticTokensHandlerBase
{
    protected override async Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        foreach (var token in await tokens.GetAsync(identifier.TextDocument.Uri.ToUri(), cancellationToken))
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
            Range = true,
        };
}
