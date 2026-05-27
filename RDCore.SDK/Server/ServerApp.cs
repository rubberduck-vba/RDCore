using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.Reflection;

namespace RDCore.SDK.Server;

/// <summary>
/// A <c>RDCore.SDK</c> <em>server application</em>.
/// </summary>
public abstract class ServerApp
{
    private static readonly Lazy<AssemblyName> _info = new(() => typeof(ServerApp).Assembly.GetName(), LazyThreadSafetyMode.PublicationOnly);
    public static AssemblyName Info => _info.Value;

    /// <summary>
    /// Manages the lifecycle state of the server application.
    /// </summary>
    public virtual IServerStateProvider ServerStateProvider { get; } = new ServerStateProvider(new Configuration.ServerOptions { Verbose = true, PipeName = "RDCoreSDK.ServerApp1" });

    /// <summary>
    /// Runs the <c>RDCore.SDK</c> client/server application.
    /// </summary>
    /// <remarks>
    /// <strong><c>RDCore.SDK</c> plugins should <c>await</c> this method inside a <c>try...catch</c> block</strong> in the application's entry point (<c>Program.cs</c>) 
    /// to block execution until the internal <c>Omnisharp</c> LSPserver exits.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Signals a <strong>normal exit</strong>; host application process should exit with code 0.</exception>
    /// <exception cref="Exception">Any other exception type is unexpected and if it is fatal, the host application process should exit with a non-zero error code.</exception>
    public async Task RunAsync()
    {

        var services = new ServiceCollection();
        ConfigureCoreServices(services);
        ConfigureAppServices(services);

        using var provider = services.BuildServiceProvider();
        var app = provider.GetRequiredService<ILanguageServerApp>();
        await app.RunAsync(provider);
    }



    private void ConfigureCoreServices(IServiceCollection services)
    {
        services
            .AddSingleton(provider => Info.Version!) // TODO get rid of this
            .AddSingleton<IServerStateProvider>(provider => ServerStateProvider) // TODO get rid of this

            .AddSingleton<IServerCommandProvider>(provider => new ServerCommandProvider(provider))

            .AddSingleton<ILanguageServerApp, ConsoleLanguageClientApp>()
            .AddSingleton<IHealthCheckService, HealthCheckService>()

            .AddLogging(ConfigureLogging);
    }

    /// <summary>
    /// Configures dependency injection for a <c>RDCore.SDK</c> client/server application.
    /// </summary>
    /// <param name="services">The <c>IServiceCollection</c> to configure services.</param>
    protected abstract void ConfigureAppServices(IServiceCollection services);

    /// <summary>
    /// Configures logging for a <c>RDCore.SDK</c> client/server application.
    /// </summary>
    /// <param name="builder">The <c>ILoggingBuilder</c> to configure logging providers.</param>
    protected virtual void ConfigureLogging(ILoggingBuilder builder)
    {
        if (ServerStateProvider.Options.Verbose)
        {
            builder.SetMinimumLevel(LogLevel.Trace);
        }
        else
        {
            builder.SetMinimumLevel(LogLevel.Information);
        }

        builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "[HH:mm:ss.fff] ";
        });

#if DEBUG
        builder.AddDebug();
#endif
    }
}


public class ConsoleLanguageClientApp : LanguageServerApp
{
    public ConsoleLanguageClientApp(IOptions<LanguageServerAppOptions> appSettings, IServerStateProvider serverStateProvider, IHealthCheckService healthCheckService, ILanguageServerProtocolTransportLayer transport, ILogger<LanguageServerApp> logger) 
        : base(appSettings, serverStateProvider, healthCheckService, transport, logger)
    {
        
    }

    protected override ServerInfo GetServerInfo()
    {
        throw new NotImplementedException();
    }

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        throw new NotImplementedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // release any held resources here
        }
    }

    protected override async Task RegisterServerCapabilitiesAsync(ILanguageServer server, ClientCapabilities clientCapabilities, CancellationToken token)
    {
        clientCapabilities.Workspace = new()
        {
            Diagnostics = new(isSupported: true),
            ExecuteCommand = new(isSupported: true),
        };
        clientCapabilities.Window = new()
        {
            ShowMessage = new(isSupported: true),
            WorkDoneProgress = new(isSupported: false) // TODO
        };
    }
}