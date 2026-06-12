using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Extensibility.Configuration;
using RDCore.SDK.Extensibility.Server;
using RDCore.SDK.Extensibility.Server.Services.States;

namespace RDCore.SDK.Extensibility.Extensions;

public class RDCoreExtensionServerApp(
    IOptions<SdkServerOptions> options,
    ILogger<RDCoreExtensionServerApp> logger,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<ILanguageServerApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer)
    : LanguageServerApp(options, logger, serverStateProvider, healthCheckService, transportLayer)
{
    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        // TODO
    }

    protected override async Task RegisterServerCapabilitiesAsync(ILanguageServer server, ClientCapabilities clientCapabilities, CancellationToken token)
    {
        // TODO
    }

    protected override void Dispose(bool disposing) { }
}
