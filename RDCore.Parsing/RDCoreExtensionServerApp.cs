using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;

namespace RDCore.Parsing;

internal class RDCoreExtensionServerApp(
    IOptions<SdkServerOptions> options,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<RDCoreExtensionServerApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<RDCoreExtensionServerApp> logger)
    : RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        // TODO
    }

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
    {
    }

    protected override void Dispose(bool disposing) { }
}
