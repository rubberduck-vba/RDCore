using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.CLI.App.Commands;
using RDCore.CLI.App.Console;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.CLI.Themes;
using RDCore.SDK;
using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO;
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
// native command-mode verbs provided by rdc.exe:
[assembly: ProvidesCorePlatformClientCapability<CliCommand>]

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

            // a leading non-option token is a command verb (e.g. rdc.exe describe-ext …): command mode,
            // which needs neither a workspace nor an LSP connection.
            if (args is [{ Length: > 0 } verb, ..] && !verb.StartsWith('-'))
            {
                using var commandHost = new RDCoreConsoleCommandHost();
                return await commandHost.RunAsync(args);
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
            .Configure<AppOptions>(configuration.GetSection("Configuration:CLI"))
            .AddSingleton<IAppThemeService, AppThemeService>()
            .AddSingleton<IAppThemeLoaderService, AppThemeLoaderService>()
            .AddSingleton(Spectre.Console.AnsiConsole.Console)
            .AddSingleton<IConsoleMessageWriter, SpectreConsoleMessageWriter>()
            .AddSingleton<ShowSplashCommand>();
    }

    // client mode renders its logs through the same Spectre-backed writer; framework lifetime
    // chatter is quieted, but RDCore platform bring-up stays visible at the configured trace level.
    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        builder.ClearProviders();
        services.AddSingleton<ILoggerProvider, RDCoreConsoleLoggerProvider>();
        builder.AddFilter("Microsoft", LogLevel.Warning);
        // the CLI host's own bootstrap narration ("application resolved", "host started") is noise
        // for an interactive shell; connection/platform logs (RDCore.SDK.Client.*) stay visible.
        builder.AddFilter("RDCore.CLI", LogLevel.Warning);
        base.ConfigureExternalLogging(services, builder, configuration);
    }

    protected override async Task BeforeAppStartAsync(IServiceProvider provider)
    {
        var themes = provider.GetRequiredService<IAppThemeService>();
        await themes.InitializeAsync(CancellationToken.None);
        ApplyShellFrame(themes.Theme);

        provider.GetRequiredService<ShowSplashCommand>().Execute(new() { Show = true });
    }

    // the C64-style deep-blue shell frame — nearest ConsoleColor of the theme's 24-bit shell colours.
    private static void ApplyShellFrame(AppTheme theme)
    {
        try
        {
            System.Console.BackgroundColor = theme.ShellBackground;
            System.Console.ForegroundColor = theme.ShellForeground;
            System.Console.Clear();
        }
        catch (System.IO.IOException)
        {
        }
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
/// <c>rdc.exe &lt;verb&gt; …</c>: command mode. No workspace, no LSP connection — resolves the verb
/// against the native + extension command providers and runs it.
/// </summary>
internal class RDCoreConsoleCommandHost : AppHost<RDCoreConsoleCommandApp>
{
    public override int ExitCode => HostServices?.GetService<RDCoreConsoleCommandApp>()?.ExitCode ?? 0;

    // configuration comes from appsettings.json (added by AppHost.RunAsync); per-command switches
    // such as --unsafe-dev-mode are parsed by the command itself, so there is nothing to bind here.
    protected override void Configure(IConfigurationBuilder configuration, IServiceCollection services, string[] args)
    {
    }

    protected override async Task BeforeAppStartAsync(IServiceProvider provider)
        => await provider.GetRequiredService<IAppThemeService>().InitializeAsync(CancellationToken.None);

    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        services
            .Configure<AppOptions>(configuration.GetSection("Configuration:CLI"))
            .AddSingleton<IAppThemeService, AppThemeService>()
            .AddSingleton<IAppThemeLoaderService, AppThemeLoaderService>()
            .AddSingleton(Spectre.Console.AnsiConsole.Console)
            .AddSingleton<IConsoleMessageWriter, SpectreConsoleMessageWriter>()
            // native verbs first: NativeCliCommandProvider is enumerated before the extension one, so
            // a native verb wins a name collision.
            .AddSingleton<ICliCommand, DescribeExtensionCommand>()
            .AddSingleton<ICliCommand, NewWorkspaceCommand>()
            .AddSingleton<ICliCommandProvider, NativeCliCommandProvider>()
            .AddSingleton<ICliCommandProvider, ExtensionCliCommandProvider>()
            .AddSingleton<ICliCommandDispatcher, CliCommandDispatcher>();
    }

    // route ILogger<T> in command mode through the same Spectre-backed console writer, so CLI code
    // and framework logs share one rendering. Framework categories are held down to warnings.
    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        // drop the host builder's default console provider; command-mode logs render through the
        // Spectre-backed console writer instead.
        builder.ClearProviders();
        services.AddSingleton<ILoggerProvider, RDCoreConsoleLoggerProvider>();
        // a CLI verb shouldn't narrate hosting/discovery chatter — CLI code writes user-facing output
        // through IConsoleMessageWriter directly; only surface warnings and errors from ILogger.
        builder.AddFilter("Microsoft", LogLevel.Warning);
        builder.AddFilter("RDCore", LogLevel.Warning);
        // extension discovery narrates itself ("no manifest…", "…is valid") — not for a CLI verb.
        builder.AddFilter("RDCore.SDK.Extensibility", LogLevel.Error);
        base.ConfigureExternalLogging(services, builder, configuration);
    }
}

internal sealed class RDCoreConsoleCommandApp(
    ICliCommandDispatcher dispatcher,
    ILogger<RDCoreConsoleCommandApp> logger) : IRDCoreApp
{
    public CoreServerComponent PlatformComponent => CoreServerComponent.ClientApp;

    /// <summary>The dispatched command's exit code, surfaced by the host once <see cref="RunAsync"/> returns.</summary>
    public int ExitCode { get; private set; }

    public async Task RunAsync(IServiceProvider provider, string[] args)
    {
        var verb = args[0];
        var verbArgs = args.Skip(1).ToArray();
        ExitCode = await dispatcher.DispatchAsync(verb, verbArgs, CancellationToken.None);
    }

    public void LogIfEnabled(LogLevel logLevel, string message)
    {
        if (logger.IsEnabled(logLevel))
        {
            logger.Log(logLevel, "{message}", message);
        }
    }

    public void Dispose() { }
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
        builder.AddFile(
            System.IO.Path.Combine(PlatformEnvironment.Default.LogsDirectory, "RDCore.EnvironmentHost.log"),
            ResolveTraceLevel(configuration));
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