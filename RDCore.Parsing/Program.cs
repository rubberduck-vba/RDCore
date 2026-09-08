using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.Parsing.Handlers;
using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO;
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
        try
        {
            code = await host.RunAsync(args);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.ToString());
            code = -1;
        }

        // the shutdown sequence is bounded and returns promptly; this only guards against a wedged
        // background thread keeping the process alive past a clean exit.
        ProcessWatchdog.Arm(code);

        try
        {
            host.Dispose();
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.ToString());
        }

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
        builder.AddFile(
            System.IO.Path.Combine(RDCore.SDK.Platform.PlatformEnvironment.Default.LogsDirectory, "RDCore.ParseServer.log"),
            ResolveTraceLevel(configuration));
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
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton(provider => provider.GetRequiredService<IFileSystem>().File);

        // the wire-error scrub mode is an outer-container option; register the resolved value (boxed —
        // it is an enum) so the parser and the handler, both built by the OmniSharp-internal
        // container, can take it.
        services.AddSingleton(typeof(SourcePathScrubMode), options.Value.Server.WireErrorDetail);
        services.AddSingleton<IModuleParser, ModuleParser>();

        // handlers resolve ILogger<T> from the OmniSharp-internal container, which otherwise has no
        // sink — route it to the same RDCore.ParseServer.log the outer host writes.
        services.AddLogging(builder => builder.AddFile(
            System.IO.Path.Combine(RDCore.SDK.Platform.PlatformEnvironment.Default.LogsDirectory, "RDCore.ParseServer.log"),
            LogLevel.Debug));
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
