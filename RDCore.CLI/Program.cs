using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.CLI.App.Commands;
using RDCore.CLI.App.Messages;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.CLI.Themes.Model;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;

// expose internals to RDCore.Tests and the LSP handler container's dynamic proxies:
[assembly: InternalsVisibleTo("RDCore.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

// platform capabilities provided by rdc.exe in environment-host mode:
[assembly: ProvidesCorePlatformClientCapability<DefineSymbols>]

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
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        base.ConfigureAdditionalExternalServices(services, configuration);

        // the runtime session this host owns for the workspace it was launched against; composed on
        // the LSP initialize handshake, then populated as the language server sends symbol descriptors.
        services.AddSingleton<IEnvironmentSessionProvider, EnvironmentSessionProvider>();
    }

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
    IProjectFileLoader projectFileLoader,
    IEnvironmentSessionProvider sessionProvider,
    ILogger<RDCoreConsoleEnvironmentHostApp> logger)
    : RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.EnvironmentHost;

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
        => builder.WithHandler<DefineSymbolsHandler>();

    // bridge the outer-container singleton into the language-server handler container so a handler
    // resolves the same session provider the app composes on initialize.
    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton(sessionProvider);

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities) { }

    // composes the runtime session from the workspace the language server initialized against;
    // a load failure is logged, not fatal — the host still completes the handshake.
    protected override async Task OnLanguageServerInitializeAsync(ILanguageServer server, InitializeParams request, CancellationToken cancellationToken)
    {
        await ComposeSessionAsync(request);
        await base.OnLanguageServerInitializeAsync(server, request, cancellationToken);
    }

    private async Task ComposeSessionAsync(InitializeParams request)
    {
        if (request.RootUri is null)
        {
            LogIfEnabled(LogLevel.Warning, "No RootUri in the initialize request; the runtime session will not be composed.");
            return;
        }

        try
        {
            var project = await projectFileLoader.LoadAsync(request.RootUri.GetFileSystemPath());
            sessionProvider.Compose(project.ProjectInfo, new Uri(project.Uri));
            LogIfEnabled(LogLevel.Information, "✅ Runtime session composed from the workspace project");
        }
        catch (Exception exception)
        {
            LogIfEnabled(LogLevel.Error, $"❌ Runtime session could not be composed:\n{exception}");
        }
    }

    protected override void Dispose(bool disposing) { }
}