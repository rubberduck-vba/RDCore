using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.CLI.App.Commands;
using RDCore.CLI.App.Messages;
using RDCore.CLI.Themes.Model;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services.States;
using System.IO.Abstractions;

namespace RDCore.CLI;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var fileSystem = new FileSystem();
        var config = Options.Create(new AppOptions());
        var loader = new AppThemeLoaderService(config, fileSystem);
        var themeService = new AppThemeService(config, loader);
        var themes = new AppThemeService(config, loader);
        var writer = new DefaultConsoleMessageWriter(themeService);
        
        var splash = new ShowSplashCommand(writer, themes);
        splash.Execute();

        // TODO move to appsettings.json
        var sdkServerOptions = new ServerOptions 
        {
            PipeName = "rdc.LanguageServer",
            ConnectTimeoutSeconds = 5,
            HealthCheckIntervalSeconds = 5,
            MaximumInstances = 1,
            ShutdownTimeoutSeconds = 5,
            Verbose = true
        };
        var stateProvider = new ServerStateProvider(sdkServerOptions);

        try
        {
            await new RDCoreConsoleClientServerApp().RunAsync();
        }
        catch (OperationCanceledException)
        {
            // normal exit: VIVAT CUCUMIS!
            writer.WriteSlogan();
        }
        catch (Exception exception)
        {
            // something went wrong:
            writer.WriteException(exception);
        }

        // clean exit:
        writer.WriteLegalNotice();
        return stateProvider.State.ExitCode;
    }
}

internal static class Bootstrapper
{

}

internal class RDCoreConsoleClientServerApp : ServerApp
{
    
    protected override void ConfigureAppServices(IServiceCollection services)
    {
    }

    protected override void ConfigureLogging(ILoggingBuilder builder)
    {
        base.ConfigureLogging(builder);
    }
}