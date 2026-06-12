using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using RDCore.CLI.App.Commands;
using RDCore.CLI.App.Messages;
using RDCore.CLI.Themes.Model;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Extensibility.Client;
using RDCore.SDK.Extensibility.Configuration;
using RDCore.SDK.Extensibility.Server;

namespace RDCore.CLI;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var processTokenSource = new CancellationTokenSource();
        using var host = new RDCoreConsoleClientHost(processTokenSource);
        
        try
        {
            await host.RunAsync(args);
        }
        catch (OperationCanceledException)
        {
            // normal exit
        }
        catch (Exception exception)
        {
            // something went wrong:
            Console.WriteLine(exception.ToString());
            return -1;
        }
        finally
        {
            Console.WriteLine(Resources.RDCore_Slogan);
            Console.WriteLine(Resources.TrademarkNotice);
        }

        // clean exit:
        return 0;
    }
}

internal class RDCoreConsoleClientHost(CancellationTokenSource ProcessTokenSource) 
    : RDCoreLanguageClientHost<RDCoreConsoleClientApp>(ProcessTokenSource)
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services) => services
        .AddSingleton<RDCoreConsoleClientApp>()
        .AddSingleton<ILanguageClientApp, RDCoreConsoleClientApp>()
        .AddSingleton<IAppThemeService, AppThemeService>()
        .AddSingleton<IAppThemeLoaderService, AppThemeLoaderService>()
        .AddSingleton<IConsoleMessageWriter, DefaultConsoleMessageWriter>()
        .AddSingleton<ILoggerProvider, RDCoreConsoleLoggerProvider>()
        .AddSingleton<ShowSplashCommand>();
}

internal class RDCoreConsoleClientApp(
    IOptions<SdkServerOptions> options,
    IRDCoreLanguageServerProcess serverProcess,
    IHealthCheckService<ILanguageClientApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<RDCoreConsoleClientApp> logger,
    ShowSplashCommand splash) 
    : LanguageClientApp(options, logger, serverProcess, healthCheckService, transportLayer)
{
    protected override ClientCapabilities ConfigureClientCapabilities(ClientCapabilities capabilities)
    {
        // TODO
        return capabilities;
    }

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        // TODO
    }

    protected override async Task OnLanguageClientStartedAsync(ILanguageClient client, CancellationToken token)
    {
        splash.Execute();
    }

    protected override void Dispose(bool disposing) { }

    protected override void ConfigureServices(IServiceCollection services)
    {
    }
}