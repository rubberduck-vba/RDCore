using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RDCore.Tests")]
namespace RDCore.Parsing;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var host = new RDCoreParserAppHost();
        return await host.RunAsync(args);
    }
}

public class RDCoreParserAppHost : RDCoreLanguageServerHost<RDCoreParserApp> 
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        base.ConfigureAdditionalExternalServices(services, configuration);
    }
}

public class RDCoreParserApp : RDCoreServerApp
{
    public RDCoreParserApp(
        //IOptions<SdkServerOptions> options, 
        IServerStateProvider serverStateProvider, 
        IHealthCheckService<RDCoreParserApp> healthCheckService, 
        ILanguageServerProtocolTransportLayer transportLayer, 
        ILogger<RDCoreParserApp> logger) :
        base(serverStateProvider, healthCheckService, transportLayer, logger)
    {
    }

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