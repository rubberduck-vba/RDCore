using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Extensibility.Configuration;
using RDCore.SDK.Extensibility.Server;
using RDCore.SDK.Extensibility.Server.Services.States;

namespace RDCore.LanguageServer;

/// <summary>
/// The RDCore <strong>RD-VBA Language Server</strong> application.
/// </summary>
/// <remarks>
/// 👉 This application implements a <em>Language Server Protocol (LSP)</em> <strong>server</strong> and is responsible for 
/// <strong>orchestrating communications</strong> between the IDE editor and the applications and services of the RDCore platform.
/// </remarks>
internal sealed class CoreLanguageServerAppHost(CancellationTokenSource ProcessTokenSource) 
    : RDCoreLanguageServerHost<CoreLanguageServerApp>(ProcessTokenSource)
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services)
    {
    }
}

internal sealed class CoreLanguageServerApp(
    IOptions<SdkServerOptions> options, 
    ILogger<LanguageServerApp> logger,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<ILanguageServerApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer)
    : LanguageServerApp(options, logger, serverStateProvider, healthCheckService, transportLayer)
{
    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
    }

    protected override void Dispose(bool disposing)
    {
    }

    protected override async Task RegisterServerCapabilitiesAsync(ILanguageServer server, ClientCapabilities clientCapabilities, CancellationToken token)
    {
    }
}