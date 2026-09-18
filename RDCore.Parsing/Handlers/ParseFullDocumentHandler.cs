using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Parsing.Handlers;

[Method(RDCorePlatformProtocol.ParseFullDocument)]
public class ParseFullDocumentHandler(
    IModuleParser moduleParser,
    ILogger<ParseFullDocumentHandler> logger,
    IOptions<SdkServerOptions> serverOptions)
    : RDCoreRequestHandler<ParseDocumentParams, PlatformJsonEnvelope>
{
    protected override async Task<PlatformJsonEnvelope> HandleAsync(ParseDocumentParams request, CancellationToken token)
    {
        if (request?.DocumentUri is not Uri uri)
        {
            logger.LogWarning("📥 {method}: request had no DocumentUri.", RDCorePlatformProtocol.ParseFullDocument);
            throw new InvalidParametersException(request);
        }
        if (request.Fragment is not string content)
        {
            logger.LogWarning("📥 {method}: request for {uri} had no Fragment.", RDCorePlatformProtocol.ParseFullDocument, uri);
            throw new InvalidParametersException(request);
        }

        logger.LogInformation("📥 {method}: {uri}", RDCorePlatformProtocol.ParseFullDocument, uri);
        try
        {
            var result = moduleParser.Parse(uri, content);
            logger.LogInformation("📤 {uri}: {status}", uri,
                result.IsSuccess ? "ok" : $"{result.SyntaxErrors.Length} syntax error(s)");

            // the AST is polymorphic; the transport serializer can't round-trip it. Wrap a
            // System.Text.Json string the transport carries verbatim.
            return PlatformJsonEnvelope.Of(result);
        }
        catch (Exception exception)
        {
            // a parser or serialization failure on one module degrades to a failed result carrying the
            // detail, rather than a bare JSON-RPC "-32603 Internal error" the caller can't act on.
            logger.LogError(exception, "❌ {method} failed for {uri}", RDCorePlatformProtocol.ParseFullDocument, uri);
            return PlatformJsonEnvelope.Of(ModuleParseResult.Failed(
                new SourceLocation(uri, SourceRange.Empty), exception.ToString(), serverOptions.Value.WireErrorDetail));
        }
    }
}
