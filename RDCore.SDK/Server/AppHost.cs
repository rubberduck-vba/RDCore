using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Client;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.IO.Abstractions;
using System.Reflection;
using IFile = System.IO.Abstractions.IFile;

namespace RDCore.SDK.Server;

/// <summary>
/// A <c>RDCore.SDK</c> application host.
/// </summary>
/// <remarks>
/// 👉 This class is inherited by both <see cref="RDCoreLanguageServerHost"/> and <see cref="RDCoreLanguageClientHost"/>
/// to encapsulate a common interface to simplify implementing any kind of SDK application.<br/>
/// </remarks>
/// <typeparam name="TApp">The type of <see cref="IRDCoreApp"/> being hosted.</typeparam>
public abstract class AppHost<TApp>(CancellationTokenSource ProcessTokenSource) : IDisposable
    where TApp : IRDCoreApp
{
    private bool _disposed;
    private IHost? _host;
    private Task? _hostTask;
    private TApp? _app;
    
    private static readonly Lazy<AssemblyName> _info = new(() => Assembly.GetEntryAssembly()?.GetName()!, LazyThreadSafetyMode.PublicationOnly);
    
    /// <summary>
    /// Gets the <see cref="AssemblyName"/> of this application.
    /// </summary>
    /// <remarks>
    /// 👉 This provides the <c>Name</c> and <c>Version</c> values for both
    /// <see cref="ServerInfo"/> and <see cref="ClientInfo"/> unless the application overrides this default.
    /// </remarks>
    public static AssemblyName Info => _info.Value;

    public void LogIfEnabled(LogLevel logLevel, string message)
        => _app?.LogIfEnabled(logLevel, message);

    /// <summary>
    /// Runs the <c>RDCore.SDK</c> client/server application.
    /// </summary>
    /// <remarks>
    /// <strong><c>RDCore.SDK</c> plugins should <c>await</c> this method inside a <c>try...catch</c> block</strong> in the application's entry point (<c>Program.cs</c>) 
    /// to block execution until the internal <c>Omnisharp</c> LSPserver exits.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Signals a <strong>normal exit</strong>; host application process should exit with code 0.</exception>
    /// <exception cref="Exception">Any other exception type is unexpected and if it is fatal, the host application process should exit with a non-zero error code.</exception>
    public async Task RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder();

        var configuration = builder.Configuration;
        configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        var options = Configure(configuration, builder.Services, args)!;

        ConfigureExternalServices(builder.Services, options);

        _host = builder.Build();
        _app = _host.Services.GetRequiredService<TApp>();

        try
        {
            _hostTask = _host.StartAsync()
                .ContinueWith(hostTask => _app.RunAsync(_host.Services), 
                ProcessTokenSource.Token, 
                TaskContinuationOptions.NotOnFaulted, 
                TaskScheduler.Current);

            await _hostTask;
        }
        finally
        {
            await _host.StopAsync();
        }
    }

    /// <summary>
    /// Override to supply application-specific <c>IConfiguration</c> configuration.
    /// </summary>
    /// <param name="builder">An <see cref="IConfigurationBuilder"/> to configure application settings.</param>
    /// <param name="args">Any command-line arguments that were supplied to the application, parsed into <see cref="SdkAppOptions"/>.</param>
    /// <remarks>
    /// 🧩 The base implementation binds and configures <c>appsettings.json</c> options, with command-line arguments as overrides.
    /// </remarks>
    /// <returns>The effective <see cref="SdkAppOptions"/> configuration.</returns>
    protected virtual IOptions<SdkAppOptions> Configure(IConfiguration configuration, IServiceCollection services, string[] args) 
    {
        var overrides = CommandLine.Parser.Default.ParseArguments<SdkAppCommandLineArgs>(args);
        var canOverride = !overrides.Errors.Any();
        var options = new SdkAppOptions();

        var config = configuration.GetSection("Configuration");
        services.Configure<SdkAppOptions>(config);
        config.Bind(options);

        var serverConfig = configuration.GetSection("Configuration").GetSection("Server");
        services.Configure<SdkServerOptions>(serverConfig);
        serverConfig.Bind(options.Server);

        var workspaceConfig = configuration.GetSection("Configuration").GetSection("Workspace");
        services.Configure<SdkWorkspaceOptions>(workspaceConfig);
        workspaceConfig.Bind(options.Workspace);

        var platformConfig = configuration.GetSection("Configuration").GetSection("Platform");
        services.Configure<SdkPlatformOptions>(platformConfig);
        platformConfig.Bind(options.Platform);

        if (canOverride)
        {
            config.Bind(overrides);
        }

        return Options.Create(options);
    }

    /// <summary>
    /// Configures only the services needed to resolve the <see cref="IRDCoreApp"/> instance.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="options"></param>
    protected virtual void ConfigureExternalServices(IServiceCollection services, IOptions<SdkAppOptions> options)
    {
        services
            .AddSingleton<IHealthCheckService<ILanguageClientApp>, HealthCheckService<ILanguageClientApp>>()
            .AddSingleton<IHealthCheckService<ILanguageServerApp>, HealthCheckService<ILanguageServerApp>>()
            .AddSingleton<ILanguageServerProtocolTransportLayer, RDCorePlatformDefaultTransportLayer>()

            .AddLogging(builder => ConfigureExternalLogging(services, builder, options.Value));
    }

    /// 👉 The <strong>external services</strong> are not registered with the <c>OmniSharp</c> language server host.
    /// <br/>Their purpose is to bootstrap the application and support functionality at the <em>entry point</em> level by
    /// registering the services that must be injected in the <see cref="IRDCoreApp"/> application and, optionally, <em>external</em> logging support.
    /// <param name="services">The <c>IServiceCollection</c> to configure services.</param>
    /// <param name="options">The current application configuration.</param>
    protected virtual void ConfigureAdditionalExternalServices(IServiceCollection services, IOptions<SdkAppOptions> options)
    {
    }

    /// <summary>
    /// Configures the <em>external service provider</em> logging targets for a <c>RDCore.SDK</c> client/server application.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to configure logging providers.</param>
    /// <remarks>
    /// 👉 The <strong>external services</strong> are not registered with the <c>OmniSharp</c> language server host.
    /// <br/>Their purpose is to bootstrap the application and support functionality at the <em>entry point</em> level by
    /// registering the services that must be injected in the <see cref="IRDCoreApp"/> application and, optionally, <em>external</em> logging support.
    /// <br/><br/>🧩 The default/base implementation:
    /// <list type="bullet">
    /// <item>Sets the effective <em>minimum log level</em> as per the supplied <see cref="SdkServerOptions.TraceLevel"/>.</item>
    /// <item>Adds a <c>Debug</c> logger in debug builds.</item>
    /// </list>
    /// </remarks>
    protected virtual void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, SdkAppOptions options)
    {
        builder
        .SetMinimumLevel(options.Server.TraceLevel)
#if DEBUG
        .AddDebug()
#endif
        ;
    }

    /// <summary>
    /// The standard .NET <em>Dispose Pattern</em>. Override to cleanly dispose of any instance-level <see cref="IDisposable"/> references.
    /// </summary>
    /// <remarks>
    /// ⚠️ Overrides <strong>MUST</strong> invoke the <c>base.Dispose(bool)</c> base implementation.
    /// </remarks>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _hostTask?.Dispose();
                _host?.Dispose();

                // TODO verify the app doesn't get disposed twice:
                _app?.Dispose();
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {        
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
