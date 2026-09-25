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
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
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
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;

// expose internals to RDCore.Tests and the LSP handler container's dynamic proxies:
[assembly: InternalsVisibleTo("RDCore.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

// platform capabilities provided by rdc.exe in environment-host mode:
[assembly: ProvidesCorePlatformClientCapability<DefineSymbols>]
// the environment host owns the runtime session, so it answers for its state:
[assembly: ProvidesCorePlatformClientCapability<SessionStatus>]
[assembly: ProvidesCorePlatformClientCapability<SessionExecute>]
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

            // rdc.exe with no arguments: the interactive shell. It is an LSP client like any other,
            // so it needs a workspace to attach to - it scaffolds a private, scratch one of its own.
            ReplWorkspace? scratchWorkspace = null;
            if (args.Length == 0)
            {
                var fileSystem = new FileSystem();
                scratchWorkspace = await ReplWorkspace.CreateAsync(fileSystem, new ProjectFileWriter(fileSystem));
                args = ["--workspace", scratchWorkspace.Root];
            }

            using var clientHost = new RDCoreConsoleClientHost(scratchWorkspace);
            return await clientHost.RunAsync(args);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            return -1;
        }
    }
}

/// <summary>
/// <c>rdc.exe</c> in client mode: brings the platform up against a workspace, then drops into the
/// interactive RD-VBA shell (see <see cref="ReplShell"/>).
/// </summary>
/// <param name="scratchWorkspace">
/// The private workspace the shell scaffolded for itself when it was given none, which this host then
/// owns and deletes; <c>null</c> when the shell attached to a real workspace.
/// </param>
internal class RDCoreConsoleClientHost(ReplWorkspace? scratchWorkspace = null) : RDCoreLanguageClientHost<RDCoreConsoleClientApp>()
{
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
            .AddSingleton<IConsoleShellFrame, ConsoleShellFrame>()
            .AddSingleton<ShowSplashCommand>()
            // the interactive shell and everything it acts on:
            .AddSingleton<ReplProgram>()
            .AddSingleton<IReplConsole, ReplConsole>()
            .AddSingleton<IReplPlatformClient>(provider => new ReplPlatformClient(provider.GetRequiredService<RDCoreConsoleClientApp>()))
            .AddSingleton<IReplCommand, HelpReplCommand>()
            .AddSingleton<IReplCommand, ListReplCommand>()
            .AddSingleton<IReplCommand, RunReplCommand>()
            .AddSingleton<IReplCommand, NewReplCommand>()
            .AddSingleton<IReplCommand, ExitReplCommand>()
            .AddSingleton<IReplCommandDispatcher, ReplCommandDispatcher>()
            .AddSingleton<ReplShell>()
            // the shell owns the break keys - Ctrl+C is BREAK, not quit - so the default console
            // lifetime must not be listening for them too. EXIT is how a session ends.
            .AddSingleton<IHostLifetime, ReplHostLifetime>();
    }

    // client mode renders its logs through the same Spectre-backed writer; framework lifetime
    // chatter is quieted, but RDCore platform bring-up stays visible at the configured trace level.
    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        builder.ClearProviders();
        services.AddSingleton<ILoggerProvider, RDCoreConsoleLoggerProvider>();
        builder.AddFilter("Microsoft", LogLevel.Warning);
        // the CLI host's own bootstrap narration ("application resolved", "host started") is noise
        // for an interactive shell.
        builder.AddFilter("RDCore.CLI", LogLevel.Warning);
        // so is the connection state machine narrating its own bring-up: an interactive shell shows a
        // banner when the platform is up, not a running commentary on how it got there. A warning or
        // an error still surfaces, and the language server keeps the full trace in its own log file.
        builder.AddFilter("RDCore.SDK.Client", LogLevel.Warning);
        base.ConfigureExternalLogging(services, builder, configuration);
    }

    protected override async Task BeforeAppStartAsync(IServiceProvider provider)
    {
        var themes = provider.GetRequiredService<IAppThemeService>();
        await themes.InitializeAsync(CancellationToken.None);

        // the C64-style deep-blue shell frame, in the theme's own 24-bit colours. Restored on the way
        // out — including on Ctrl+C, which never reaches the host's own teardown.
        var frame = provider.GetRequiredService<IConsoleShellFrame>();
        frame.Apply(themes.Theme.ShellBackground, themes.Theme.ShellForeground);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => frame.Restore();

        provider.GetRequiredService<ShowSplashCommand>().Execute(new() { Show = true });
    }

    /// <summary>
    /// Runs the interactive shell for the lifetime of the connection.
    /// </summary>
    /// <remarks>
    /// The base implementation parks until a Ctrl+C or a SIGTERM, which is what a client with no user
    /// interface wants; the shell is that wait, with a prompt. It is not called: its own log line tells
    /// the user to press Ctrl+C to exit, which is exactly what Ctrl+C no longer does here, and the
    /// graceful LSP shutdown it performs afterwards is two lines this does itself.
    /// </remarks>
    protected override async Task AfterAppRunAsync(IServiceProvider provider)
    {
        var lifetime = provider.GetRequiredService<IHostApplicationLifetime>();
        try
        {
            await provider.GetRequiredService<ReplShell>().RunAsync(lifetime.ApplicationStopping);
        }
        finally
        {
            lifetime.StopApplication();
            provider.GetRequiredService<IConsoleShellFrame>().Restore();
            // graceful LSP shutdown/exit of the language server before the host tears down.
            await provider.GetRequiredService<RDCoreConsoleClientApp>().ShutdownAsync();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            scratchWorkspace?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal class RDCoreConsoleClientApp(
    IOptions<SdkAppOptions> options,
    IChildConnectionFactory connectionFactory,
    ILogger<RDCoreConsoleClientApp> logger)
    : RDCoreClientApp(options, connectionFactory, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.ClientApp;

    // what rdc.exe asks the language server to serve beyond LSP. The language server records these
    // and refuses a request family the client never advertised, so the negotiation is real in both
    // directions: PlatformInfo tells us back what it actually provides.
    protected override CorePlatformClientCapabilities GetExpectedCapabilities() => new()
    {
        LanguageServer = new LanguageServerCapabilities
        {
            SessionStatus = new SessionStatus(true),
            SessionExecute = new SessionExecute(true),
        },
    };

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
        services
            .Configure<VerboseMessageOptions>(configuration.GetSection("Configuration:VerboseMessages"))
            .AddVerboseMessages()
            .AddSingleton<IEnvironmentSessionProvider, EnvironmentSessionProvider>();
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
        => builder
            .WithHandler<DefineSymbolsHandler>()
            .WithHandler<HostSessionStatusHandler>()
            .WithHandler<HostExecuteHandler>();

    // bridge the outer-container singleton into the language-server handler container so a handler
    // resolves the same session provider the app composes on initialize.
    protected override void ConfigureServices(IServiceCollection services)
        => services
            .AddSingleton(sessionProvider)
            .AddSingleton(_ => ExternalServices.GetRequiredService<IVerboseMessageBuilder>());

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