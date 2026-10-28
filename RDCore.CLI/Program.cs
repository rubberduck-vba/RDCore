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
using RDCore.CLI.App.Configuration;
using RDCore.CLI.App.Console;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.CLI.Themes;
using RDCore.External;
using RDCore.External.Automation;
using RDCore.External.Client;
using RDCore.External.Hosting;
using RDCore.External.Native;
using RDCore.External.Windows.Automation;
using RDCore.External.Windows.Forms;
using RDCore.External.Windows.Native;
using RDCore.SDK;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
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
// it composes its session from the project file, and does so again when the project file changes:
[assembly: ProvidesCorePlatformClientCapability<ProjectReload>]
// the environment host owns the runtime session, so it answers for its state:
[assembly: ProvidesCorePlatformClientCapability<SessionStatus>]
[assembly: ProvidesCorePlatformClientCapability<SessionExecute>]
[assembly: ProvidesCorePlatformClientCapability<SessionDiscard>]
// it can run a program so that it waits at a Stop, and it is the one that resumes, steps or ends it:
[assembly: ProvidesCorePlatformClientCapability<ProgramDebugging>]
[assembly: ProvidesCorePlatformClientCapability<SessionMemoryAccess>]
// and it runs the semantic analysis pass over the code it holds, so it answers for what the pass found:
[assembly: ProvidesCorePlatformClientCapability<SemanticAnalysis>]
// native command-mode verbs provided by rdc.exe:
[assembly: ProvidesCorePlatformClientCapability<CliCommand>]

namespace RDCore.CLI;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var mode = Environment.GetEnvironmentVariable(RDCoreServerProcess.ModeEnvironmentVariable);

            // RDCORE_MODE=external (set by the environment host when it launches rdc.exe): run as its external host,
            // the process that makes the calls a program makes to the outside world.
            if (string.Equals(mode, RDCoreServerProcess.ExternalMode, StringComparison.OrdinalIgnoreCase))
            {
                using var externalHost = new RDCoreConsoleExternalHost();
                return await externalHost.RunAsync(args);
            }

            // RDCORE_MODE=host (set by the language server when it launches rdc.exe): run as the RD-VBA
            // environment host, an LSP server. Otherwise rdc.exe is an LSP client (a workspace URI is required).
            if (string.Equals(mode, RDCoreServerProcess.HostMode, StringComparison.OrdinalIgnoreCase))
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
                Console.WriteLine("Initializing...");
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
            // an interactive shell is written in the platform's BASIC, and the language is what decides the rest: a variable that a line
            // assigns is the one the next line reads, the standard library is RDC rather than VBA, and a bare Print is a statement. The
            // servers are told, when they are started and in the initializationOptions of the initialize request.
            ("Configuration:Workspace:Language", SupportedLanguages.BASIC.Id),
        ];

    protected override SettingsSchema Settings => SettingsSchema.Platform.Combine(AppOptions.Schema);

    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        services
            .Configure<AppOptions>(configuration.GetSection($"{SettingsSchema.Root}:{AppOptions.Section}"))
            .AddSingleton<IAppThemeService, AppThemeService>()
            .AddSingleton<IAppThemeLoaderService, AppThemeLoaderService>()
            .AddSingleton(Spectre.Console.AnsiConsole.Console)
            .AddSingleton<IConsoleMessageWriter, SpectreConsoleMessageWriter>()
            .AddSingleton<IConsoleShellFrame, ConsoleShellFrame>()
            .AddSingleton<ShowSplashReplCommand>()
            .AddSingleton<ShowMemoryReplCommand>()
            // the interactive shell and everything it acts on:
            .AddSingleton<ReplProgram>()
            .AddSingleton<ReplDocument>()
            .AddSingleton<IReplConsole, ReplConsole>()
            // the dialogs a program opens are asked at the shell's console.
            .AddSingleton<IConsoleKeys, SystemConsoleKeys>()
            .AddSingleton(provider => new ReplUserInteraction(
                provider.GetRequiredService<IConsoleKeys>(),
                System.Console.Out,
                ansi: provider.GetRequiredService<Spectre.Console.IAnsiConsole>().Profile.Capabilities.Ansi,
                consoleWidth: () => System.Console.IsOutputRedirected ? 0 : System.Console.WindowWidth))
            .AddSingleton<IReplPlatformClient>(provider => new ReplPlatformClient(provider.GetRequiredService<RDCoreConsoleClientApp>()))
            .AddSingleton<IReplCommand, HelpReplCommand>()
            .AddSingleton<IReplCommand, ShowSplashReplCommand>()
            .AddSingleton<IReplCommand, ListReplCommand>()
            .AddSingleton<IReplCommand, FoldReplCommand>()
            .AddSingleton<IReplCommand, BreakReplCommand>()
            .AddSingleton<IReplCommand, ContReplCommand>()
            .AddSingleton<IReplCommand, StepReplCommand>()
            .AddSingleton<IReplCommand, StackReplCommand>()
            .AddSingleton<IReplCommand, VarsReplCommand>()
            .AddSingleton<IReplCommand, FrameReplCommand>()
            .AddSingleton<IReplCommand, RunReplCommand>()
            .AddSingleton<IReplCommand, AnalyzeReplCommand>()
            .AddSingleton<IReplCommand, DuckyReplCommand>()
            .AddSingleton<IReplCommand, PeekReplCommand>()
            .AddSingleton<IReplCommand, PokeReplCommand>()
            .AddSingleton<IReplCommand, NewReplCommand>()
            .AddSingleton<IReplCommand, LoadReplCommand>()
            .AddSingleton<IReplCommand, SaveReplCommand>()
            .AddSingleton<IReplCommand, ExitReplCommand>()
            .AddSingleton<IReplCommand, ShowMemoryReplCommand>()
            .AddSingleton<IReplCommand, ThemeReplCommand>()
            .AddSingleton<IReplCommand, ClearReplCommand>()
            .AddSingleton<IPlatformSettings, PlatformSettings>()
            // the shell is the client of a running platform: a live setting takes effect as it is set.
            .AddSingleton(provider => new ConfigCommand(provider.GetRequiredService<IPlatformSettings>(), appliesLive: true))
            .AddSingleton<IReplCommand, ConfigReplCommand>()
            .AddSingleton<IReplCommandDispatcher, ReplCommandDispatcher>()
            .AddSingleton<ReplShell>()
            // the shell owns the break keys - Ctrl+C is BREAK, not quit - so the default console
            // lifetime must not be listening for them too. EXIT is how a session ends.
            .AddSingleton<IHostLifetime, ReplHostLifetime>();

        if (scratchWorkspace is not null)
        {
            // where the program is a document while it is no file's.
            services.AddSingleton(scratchWorkspace);
        }

        // the prompt is a line editor, with completion lists that drop down under it, where it is typed at a console that can draw them; a script piped to the
        // shell, or a console that cannot, reads its lines as they come.
        if (!System.Console.IsInputRedirected && !System.Console.IsOutputRedirected && Spectre.Console.AnsiConsole.Console.Profile.Capabilities.Ansi)
        {
            services
                .AddSingleton<ReplCompletion>()
                .AddSingleton(provider => new ConsolePromptEditor(
                    provider.GetRequiredService<IConsoleKeys>(),
                    System.Console.Out,
                    () => System.Console.WindowWidth,
                    provider.GetRequiredService<ReplCompletion>().CompleteAsync,
                    ConsoleBorder.For(System.Console.Out) == ConsoleBorder.Ascii ? ConsoleBorder.Ascii : ConsoleBorder.Single)
                {
                    // a numbered line typed at the prompt is offered the line of the program it numbers, in ghost text.
                    Ghost = new ReplGhostText(provider.GetRequiredService<ReplProgram>()).For,
                    // what the call being typed takes is shown under the line, as it is typed.
                    Signature = provider.GetRequiredService<ReplCompletion>().SignatureHelpAsync,
                    // the options of a command's argument are listed as the space after the command is typed, before the first letter of one is.
                    ListAfterSpace = provider.GetRequiredService<ReplCompletion>().ListsAfterSpace,
                })
                // the progress of the work the language server reports, drawn under the prompt's line, or where a command's output is about to be written.
                .AddSingleton(provider => new ReplProgressDisplay(
                    provider.GetRequiredService<RDCoreConsoleClientApp>().WorkProgress,
                    System.Console.Out,
                    () => System.Console.WindowWidth,
                    provider.GetRequiredService<ConsolePromptEditor>(),
                    unicode: ConsoleBorder.For(System.Console.Out) != ConsoleBorder.Ascii));
        }
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
        // the C64-style deep-blue shell frame, in the theme's own 24-bit colours. Restored on the way
        // out — including on Ctrl+C, which never reaches the host's own teardown.
        var frame = provider.GetRequiredService<IConsoleShellFrame>();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => frame.Restore();

        var themes = provider.GetRequiredService<IAppThemeService>();
        await themes.InitializeAsync(CancellationToken.None);
        frame.Apply(themes.Theme.ShellBackground, themes.Theme.ShellForeground);
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
    ReplUserInteraction interaction,
    IReplConsole console,
    ILogger<RDCoreConsoleClientApp> logger)
    : RDCoreClientApp(options, connectionFactory, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.ClientApp;

    // the shell shows the progress of the work the language server does, where its console can draw it.
    protected override bool SupportsWorkDoneProgress => !System.Console.IsOutputRedirected;

    // what rdc.exe asks the language server to serve beyond LSP. The language server records these
    // and refuses a request family the client never advertised, so the negotiation is real in both
    // directions: PlatformInfo tells us back what it actually provides.
    protected override CorePlatformClientCapabilities GetExpectedCapabilities() => new()
    {
        LanguageServer = new LanguageServerCapabilities
        {
            SessionStatus = new SessionStatus(true),
            SessionExecute = new SessionExecute(true),
            SessionDiscard = new SessionDiscard(true),
            ProgramDebugging = new ProgramDebugging(true),
            SessionAnalyze = new SessionAnalyze(true),
            // the line at the prompt is completed as it is typed.
            SessionCompletion = new SessionCompletion(true),
            // and what the call being typed at it takes is shown under it.
            SessionSignatureHelp = new SessionSignatureHelp(true),
            SessionMemoryAccess = new SessionMemoryAccess(true),
            // the shell answers the dialogs a program opens, at its console.
            SessionInteraction = new SessionInteraction(true),
        },
    };

    // the handlers of the dialogs are built by the language client's own container; what they answer with is the shell's, in the outer one.
    protected override void ConfigureServices(IServiceCollection services)
        => services
            .AddSingleton(interaction)
            .AddSingleton(console);

    protected override ClientCapabilities ConfigureClientCapabilities(ClientCapabilities capabilities)
    {
        // TODO
        return capabilities;
    }

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
        => builder
            .WithHandler<ReplMessageBoxHandler>()
            .WithHandler<ReplInputBoxHandler>();

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

    // `rdc.exe <verb> <the verb's own options>` is not the platform's command line: --description and --overwrite are
    // options of describe-ext, which the platform's parser has never heard of and would reject as mistakes before the
    // verb ran. The verb parses - and answers for - its own.
    protected override bool TryAnswerCommandLine(string[] args, out int exitCode)
    {
        exitCode = 0;
        return false;
    }

    protected override async Task BeforeAppStartAsync(IServiceProvider provider)
        => await provider.GetRequiredService<IAppThemeService>().InitializeAsync(CancellationToken.None);

    protected override SettingsSchema Settings => SettingsSchema.Platform.Combine(AppOptions.Schema);

    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        services
            .Configure<AppOptions>(configuration.GetSection($"{SettingsSchema.Root}:{AppOptions.Section}"))
            .AddSingleton<IAppThemeService, AppThemeService>()
            .AddSingleton<IConsoleShellFrame, ConsoleShellFrame>()
            .AddSingleton<IAppThemeLoaderService, AppThemeLoaderService>()
            .AddSingleton(Spectre.Console.AnsiConsole.Console)
            .AddSingleton<IConsoleMessageWriter, SpectreConsoleMessageWriter>()
            // native verbs first: NativeCliCommandProvider is enumerated before the extension one, so
            // a native verb wins a name collision.
            .AddSingleton<ICliCommand, DescribeExtensionCommand>()
            .AddSingleton<ICliCommand, NewWorkspaceCommand>()
            .AddSingleton<ICliCommand, ImportProjectCommand>()
            .AddSingleton<ICliCommand, ExportLibraryCommand>()
            .AddSingleton<ICliCommand, BenchCommand>()
            .AddSingleton<IPlatformSettings, PlatformSettings>()
            .AddSingleton<ConfigCommand>()
            .AddSingleton<ICliCommand, ConfigCliCommand>()
            // rdc bench runs a workspace in-process, as the environment host runs one: it needs what the host's errors are described with.
            .Configure<VerboseMessageOptions>(configuration.GetSection("Configuration:VerboseMessages"))
            .AddVerboseMessages()
            .AddSingleton<RDCore.CLI.App.Bench.BenchRunner>()
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
            // the program's calls to the outside world are made by an external host of this one, which is rdc.exe again, in external mode:
            // whatever the outside world does to that process, this one - and the session it owns - is still there.
            .AddSingleton(provider => new ExternalHost(
                provider.GetRequiredService<IChildConnectionFactory>().Create,
                ExternalHost.DefaultExecutable,
                provider.GetRequiredService<ILogger<ExternalHost>>()))
            // the dialogs and the desktop are the machine's, and are not reached through the external host: nothing about showing a message box can take a
            // process down. Whether the client answers the dialogs instead is the session's to say (EnvironmentSessionProvider).
            // the forms are objects of the external host's servers, hosted there: the remote server is the form host too.
            .AddSingleton(provider =>
            {
                var servers = new RemoteAutomationServer(provider.GetRequiredService<ExternalHost>());
                return new ExternalWorld(servers, new RemoteNativeLibraryHost(provider.GetRequiredService<ExternalHost>()))
                {
                    Forms = servers,
                    Interaction = MachinePlatform.Interaction,
                    Desktop = MachinePlatform.Desktop,
                };
            })
            .AddSingleton<IEnvironmentSessionProvider, EnvironmentSessionProvider>();
    }

    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        builder.AddFile(
            System.IO.Path.Combine(PlatformEnvironment.Default.LogsDirectory, "RDCore.EnvironmentHost.log"),
            LogLevel.Trace);
        base.ConfigureExternalLogging(services, builder, configuration);
    }
}

/// <summary>
/// <c>rdc.exe</c> in external mode: the external host of an environment host, an LSP server owned and launched by it, that makes the calls its program makes to
/// the outside world with the servers of the platform it runs on.
/// </summary>
internal class RDCoreConsoleExternalHost : RDCorePlatformServerHost<ExternalHostApp>
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        base.ConfigureAdditionalExternalServices(services, configuration);
        services
            .AddSingleton(AutomationService())
            .AddSingleton(new NativeCallService(
                OperatingSystem.IsWindows() ? new WindowsNativePlatform() : UnsupportedNativePlatform.Instance));
    }

    // on Windows, the servers and the form host live on one apartment: a form's controls are objects of the servers, called on the thread its window is pumped on.
    private static ExternalAutomationService AutomationService()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ExternalAutomationService(UnavailableAutomationServer.Instance);
        }

        var apartment = new ComApartment();
        return new ExternalAutomationService(new ComAutomationServer(apartment), new WindowsFormHost(apartment));
    }

    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        builder.AddFile(
            System.IO.Path.Combine(PlatformEnvironment.Default.LogsDirectory, "RDCore.ExternalHost.log"),
            LogLevel.Trace);
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
            .WithHandler<HostExecuteHandler>()
            .WithHandler<HostDiscardHandler>()
            .WithHandler<HostProjectReloadHandler>()
            .WithHandler<HostDebugResumeHandler>()
            .WithHandler<HostDebugPauseHandler>()
            .WithHandler<HostDebugGotoHandler>()
            .WithHandler<HostDebugBreakpointsHandler>()
            .WithHandler<HostDebugStackHandler>()
            .WithHandler<HostDebugVariablesHandler>()
            .WithHandler<HostDebugEvaluateHandler>()
            .WithHandler<HostDebugExecuteHandler>()
            .WithHandler<HostDebugErrorBreakHandler>()
            .WithHandler<HostDebugTerminateHandler>()
            .WithHandler<HostSemanticsHandler>()
            .WithHandler<HostPeekHandler>()
            .WithHandler<HostPokeHandler>();

    // bridge the outer-container singleton into the language-server handler container so a handler
    // resolves the same session provider the app composes on initialize.
    protected override void ConfigureServices(IServiceCollection services)
        => services
            .AddSingleton(sessionProvider)
            .AddSingleton(projectFileLoader)
            .AddSingleton(_ => ExternalServices.GetRequiredService<IOptions<SdkAppOptions>>())
            .AddSingleton(_ => ExternalServices.GetRequiredService<IVerboseMessageBuilder>());

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities) { }

    // what a program run with streamed output prints is told to the language server as it is printed.
    protected override void OnLanguageServerStarted(ILanguageServer server)
    {
        sessionProvider.OutputStreamed = (lines, total) => _ = SendNotificationAsync(new HostOutputNotification { Lines = lines, Total = total }, CancellationToken.None);
        // and the dialogs it opens are asked of the language server, which asks its client if the client shows dialogs.
        sessionProvider.Interaction = new LanguageServerInteractionChannel(this);
        base.OnLanguageServerStarted(server);
    }

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
            // a folder with no project file is the project it holds, as the language server makes it up.
            var language = ExternalServices.GetRequiredService<IOptions<SdkAppOptions>>().Value.Workspace.SupportedLanguage;
            var project = await projectFileLoader.LoadOrSynthesizeAsync(request.RootUri.GetFileSystemPath(), language);
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