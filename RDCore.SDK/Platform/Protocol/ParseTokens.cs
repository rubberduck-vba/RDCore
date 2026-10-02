using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/parser/tokens</c>: the language server asks the parsing server what the tokens of a text are.
/// </summary>
/// <remarks>
/// The answer is lexical and nothing else (<see cref="SyntaxTokenKind"/>): the parsing server is stateless, and what a name means is the language server's to
/// add. It is how the text of a listing is highlighted, which the syntax tree cannot say - it keeps neither the keywords nor the comments.
/// </remarks>
[Method(RDCorePlatformProtocol.ParseTokens, Direction.ClientToServer)]
public record class ParseTokensParams : IRequest, IRequest<ParseTokensResult>
{
    /// <summary>The URI of the document, which identifies it and does not say where its text comes from.</summary>
    public Uri? DocumentUri { get; init; }

    /// <summary>The text to tokenize, as the caller holds it.</summary>
    public string? Text { get; init; }
}

/// <summary>
/// The tokens of a text, in the order they are in it.
/// </summary>
public record class ParseTokensResult
{
    /// <summary>The tokens of the text, by position; whitespace is not one.</summary>
    public List<SyntaxToken> Tokens { get; init; } = [];
}
