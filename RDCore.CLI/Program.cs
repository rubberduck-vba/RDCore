using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.CLI.App.Commands;
using RDCore.CLI.App.Messages;
using RDCore.CLI.Themes.Model;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.IO.Abstractions;

namespace RDCore.CLI;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            // RDCORE_MODE=host (set by the language server when it launches rdc.exe): run as the RD-VBA
            // environment host, an LSP server. Otherwise rdc.exe is an LSP client (a workspace URI is required).
            if (string.Equals(Environment.GetEnvironmentVariable(RDCoreServerProcess.ModeEnvironmentVariable), "host", StringComparison.OrdinalIgnoreCase))
            {
                using var environmentHost = new RDCoreConsoleEnvironmentHost();
                return await environmentHost.RunAsync(args);
            }

            using var clientHost = new RDCoreConsoleClientHost();
            return await clientHost.RunAsync(args);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            return -1;
        }
    }
}

internal class RDCoreConsoleClientHost() : RDCoreLanguageClientHost<RDCoreConsoleClientApp>()
{
    protected async override Task BuildAndRunAsync(HostApplicationBuilder builder, string[] args)
    {
        if (args.Length == 0)
        {
            // TODO REPL / command/program mode
            throw new NotSupportedException("This mode is not supported yet; workspace root uri argument is not optional.");
        }
        else
        {
            // we can only build and run the protocol client if we have a workspace.
            await base.BuildAndRunAsync(builder, args);
        }
    }

    protected override IEnumerable<(string, string?)> ConfigureOverrides(string[] initialArgs, SdkAppCommandLineArgs baseArgs) 
        => [
            ("CLI:UnsafeDevMode", baseArgs.UnsafeDevMode?.ToString() ?? false.ToString()),
            // ...
        ];

    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddSingleton<IAppThemeService, AppThemeService>()
            .AddSingleton<IAppThemeLoaderService, AppThemeLoaderService>()
            .AddSingleton<IConsoleMessageWriter, DefaultConsoleMessageWriter>()
            //.AddSingleton<ILoggerProvider, RDCoreConsoleLoggerProvider>()
            .AddSingleton<ShowSplashCommand>();
    }

    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        builder.AddSimpleConsole(options => options.ColorBehavior = LoggerColorBehavior.Enabled);
        builder.SetMinimumLevel(LogLevel.Trace /*Enum.Parse<LogLevel>(configuration["Server:TraceLevel"] ?? "None")*/);
    }

    protected override async Task BeforeAppStartAsync(IServiceProvider provider)
    {
        var command = provider.GetRequiredService<ShowSplashCommand>();
        command.Execute(new() { Show = true });
    }
}

internal class RDCoreConsoleClientApp(
    IOptions<SdkAppOptions> options,
    IChildConnectionFactory connectionFactory,
    ILogger<RDCoreConsoleClientApp> logger)
    : RDCoreClientApp(options, connectionFactory, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.ClientApp;

    protected override void ConfigureServices(IServiceCollection services)
    {
    }

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
        // TODO
        LogIfEnabled(LogLevel.Information, "Requesting syntax trees...");
    }

    protected override void Dispose(bool disposing) { }
}

/// <summary>
/// <c>rdc.exe --host</c>: the RD-VBA runtime environment host, an LSP server owned and launched by the language server.
/// </summary>
internal class RDCoreConsoleEnvironmentHost : RDCorePlatformServerHost<RDCoreConsoleEnvironmentHostApp>
{
    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        builder.AddFile(System.IO.Path.Combine(PlatformEnvironment.Default.LogsDirectory, "RDCore.EnvironmentHost.log"));
        base.ConfigureExternalLogging(services, builder, configuration);
    }
}

internal class RDCoreConsoleEnvironmentHostApp(
    IOptions<SdkAppOptions> options,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<RDCoreConsoleEnvironmentHostApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<RDCoreConsoleEnvironmentHostApp> logger)
    : RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.EnvironmentHost;

    // TODO (roadmap B): own the RD-VBA runtime environment; reference RDCore.Runtime; handle rdcore/host/symbols/define.
    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder) { }
    protected override void ConfigureServices(IServiceCollection services) { }
    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities) { }
    protected override void Dispose(bool disposing) { }
}