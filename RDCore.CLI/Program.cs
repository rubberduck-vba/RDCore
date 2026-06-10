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
using RDCore.SDK.Client;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;

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
            // normal exit: VIVAT CUCUMIS!
            host.LogIfEnabled(LogLevel.Information, Resources.RDCore_Slogan);
        }
        catch (Exception exception)
        {
            // something went wrong:
            host.LogIfEnabled(LogLevel.Error, exception.ToString());
            return -1;
        }

        // clean exit:
        host.LogIfEnabled(LogLevel.Information, Resources.TrademarkNotice);
        return 0;
    }
}

internal class RDCoreConsoleClientHost(CancellationTokenSource ProcessTokenSource) 
    : RDCoreLanguageClientHost<RDCoreConsoleClientApp>(ProcessTokenSource)
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IOptions<SdkAppOptions> options)
    {
        services
            .AddSingleton<IAppThemeService, AppThemeService>()
            .AddSingleton<IAppThemeLoaderService, AppThemeLoaderService>()
            .AddSingleton<IConsoleMessageWriter, DefaultConsoleMessageWriter>()
            .AddSingleton<ILoggerProvider, RDCoreConsoleLoggerProvider>()
            .AddSingleton<ShowSplashCommand>();
    }

    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, SdkAppOptions options)
    {
        builder.SetMinimumLevel(options.Server.TraceLevel);
    }
}

internal class RDCoreConsoleClientApp(
    IOptions<SdkServerOptions> options,
    IRDCoreLanguageServerProcess serverProcess,
    IHealthCheckService<ILanguageClientApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<RDCoreConsoleClientApp> logger,
    ShowSplashCommand splash) 
    : LanguageClientApp(options, serverProcess, healthCheckService, transportLayer, logger)
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
        throw new NotImplementedException();
    }
}