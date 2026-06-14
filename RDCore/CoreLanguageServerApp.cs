using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;

namespace RDCore.LanguageServer;

/// <summary>
/// The RDCore <strong>RD-VBA Language Server</strong> application.
/// </summary>
/// <remarks>
/// 👉 This application implements a <em>Language Server Protocol (LSP)</em> <strong>server</strong> and is responsible for 
/// <strong>orchestrating communications</strong> between the IDE editor and the applications and services of the RDCore platform.
/// </remarks>
internal sealed class CoreLanguageServerApp(
    IOptions<SdkServerOptions> options,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<CoreLanguageServerApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<CoreLanguageServerApp> logger)
    : RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
    }

    protected override void Dispose(bool disposing)
    {
    }

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
    {
    }
}