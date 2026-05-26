using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.CLI.App.Commands;
using RDCore.CLI.App.Messages;
using RDCore.CLI.App.Messages.Model;
using RDCore.CLI.Themes.Model;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services.States;
using System.IO.Abstractions;
using System.Text;

namespace RDCore.CLI;

public class Program
{
    private static readonly ConsoleMessageBuilder _messageBuilder = new();
    public static async Task<int> Main(string[] args)
    {
        var fileSystem = new FileSystem();
        var config = Options.Create(new AppOptions());
        var loader = new AppThemeLoaderService(config, fileSystem);
        var themeService = new AppThemeService(config, loader);
        var themes = new AppThemeService(config, loader);
        var writer = new ConsoleMessageWriter(themeService);
        
        var splash = new ShowSplashCommand(writer, themes);
        splash.Execute();

        // TODO move to appsettings.json
        var options = new ServerOptions 
        {
            ConnectTimeoutSeconds = 5,
            HealthCheckIntervalSeconds = 5,
            MaximumInstances = 1,
            ShutdownTimeoutSeconds = 5,
            Verbose = true
        };
        var stateProvider = new ServerStateProvider(options);

        try
        {
            await new RDCoreConsoleClientServerApp().RunAsync();
        }
        catch (OperationCanceledException)
        {
            writer.WriteMessage(new ConsoleMessageBuilder()
                .WithKind(MessageKind.Information)
                .WithTitle(Resources.RDCore_Slogan)
                .WithMessageBody(Resources.CopyrightNotice));
        }
        catch (Exception exception)
        {
            writer.WriteMessage(_messageBuilder
                .WithKind(MessageKind.Error)
                .WithTitle(exception)
                .WithMessageBody(exception)
                .WithStackTrace(exception));
       }

        return stateProvider.State.ExitCode;
    }
}

internal class RDCoreConsoleClientServerApp : ServerApp
{
    protected override void ConfigureAppServices(IServiceCollection services)
    {
        //throw new NotImplementedException();
    }

    protected override void ConfigureLogging(ILoggingBuilder builder)
    {
        base.ConfigureLogging(builder);
    }
}