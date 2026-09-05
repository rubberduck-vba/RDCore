using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.Parsing.Handlers;
using RDCore.SDK.Client;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;

// for warnings about antlr-generated parser rule context types not requiring CLSCompliantAttribute because not present on assembly.
[assembly: CLSCompliant(false)]

// expose internals to RDCore.Tests and CastleWindsor proxies:
[assembly: InternalsVisibleTo("RDCore.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

// list all the platform capabilities provided by this server here:
[assembly: ProvidesCorePlatformClientCapability<ParseFullDocument>]


namespace RDCore.Parsing;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var host = new RDCoreParserAppHost();
        int code;
        try { code = await host.RunAsync(args); }
        finally { host.Dispose(); }
        // background threads can otherwise delay process exit.
        Environment.Exit(code);
        return code;
    }
}

public class RDCoreParserAppHost : RDCorePlatformServerHost<RDCoreParserApp>
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        base.ConfigureAdditionalExternalServices(services, configuration);
    }

    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        builder.AddFile("..\\Logs\\RDCore.ParseServer.log");
        base.ConfigureExternalLogging(services, builder, configuration);
    }
}

public class RDCoreParserApp(
    IOptions<SdkAppOptions> options,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<RDCoreParserApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<RDCoreParserApp> logger)
: RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.ParsingServer;

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        builder.WithHandler<ParseFullDocumentHandler>();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        // dependencies of ParseFullDocumentHandler:
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton(provider => provider.GetRequiredService<IFileSystem>().File);
        services.AddSingleton<IModuleParser, ModuleParser>();
    }

    protected override void Dispose(bool disposing)
    {
    }

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
    {
        //clientCapabilities.Parsing = new()
        //{
        //    ParseFullDocument = new(IsSupported: true)
        //};
    }
}
