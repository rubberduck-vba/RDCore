using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Parsing.Handlers;

[Method(RDCorePlatformProtocol.ParseTokens)]
public class ParseTokensHandler(ISyntaxTokenizer tokenizer, ILogger<ParseTokensHandler> logger)
    : RDCoreRequestHandler<ParseTokensParams, ParseTokensResult>
{
    protected override Task<ParseTokensResult> HandleAsync(ParseTokensParams request, CancellationToken token)
    {
        if (request?.Text is not string text)
        {
            logger.LogWarning("📥 {method}: request had no Text.", RDCorePlatformProtocol.ParseTokens);
            throw new InvalidParametersException(request);
        }

        return Task.FromResult(new ParseTokensResult { Tokens = [.. tokenizer.Tokenize(text)] });
    }
}
