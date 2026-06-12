using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Extensibility.Client;
using RDCore.SDK.Extensibility.Configuration;
using RDCore.SDK.Extensibility.Server.Services.States;
using System.IO.Abstractions;
using System.Reflection;

namespace RDCore.SDK.Extensibility.Server;

/// <summary>
/// A <c>RDCore.SDK</c> application host.
/// </summary>
/// <remarks>
/// 👉 This class is inherited by both <see cref="RDCoreLanguageServerHost{TApp}"/> and <see cref="RDCoreLanguageClientHost{TApp}"/>
/// to encapsulate a common interface to simplify implementing any kind of SDK application.<br/>
/// </remarks>
/// <typeparam name="TApp">The type of <see cref="IRDCoreApp"/> being hosted.</typeparam>
public abstract class AppHost<TApp>(CancellationTokenSource ProcessTokenSource) : IDisposable
    where TApp : RDCoreApp
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
        
        Configure(configuration, builder.Services, args);
        ConfigureExternalServices(builder.Services);
        ConfigureAdditionalExternalServices(builder.Services);

        _host = builder.Build();
        _app = _host.Services.GetRequiredService<TApp>();

        try
        {
            _hostTask = _host.StartAsync()
                .ContinueWith(hostTask => _app.RunAsync(_host.Services, ProcessTokenSource.Token), 
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
    /// <param name="configuration">An <see cref="IConfiguration"/> to configure application settings.</param>
    /// <param name="services">The <see cref="IServiceCollection"/> being built for this <em>application host</em>.</param>
    /// <param name="args">Any command-line arguments that were supplied to the application, parsed into <see cref="SdkAppOptions"/>.</param>
    /// <remarks>
    /// 🧩 The base implementation binds and configures <c>appsettings.json</c> options, with command-line arguments as overrides.
    /// </remarks>
    /// <returns>The effective <see cref="SdkAppOptions"/> configuration.</returns>
    protected virtual void Configure(IConfiguration configuration, IServiceCollection services, string[] args) 
    {
        static void OverrideValueIfSupplied<T>(T? value, Action<T> setter) where T : struct
        {
            if (value.HasValue)
            {
                setter(value.Value);
            }
        }
        static void OverrideStringIfSupplied(string? value, Action<string> setter)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                setter(value);
            }
        }

        var overrides = CommandLine.Parser.Default.ParseArguments<SdkAppCommandLineArgs>(args);
        var canOverride = !overrides.Errors.Any();
        var options = new SdkAppOptions();

        var config = configuration.GetSection("Configuration");
        services.Configure<SdkAppOptions>(config);
        services.PostConfigure<SdkAppOptions>(options =>
        {
            if (canOverride)
            {
                options.Platform.Transport.PipeConfig.PipeName = overrides.Value.PipeName ?? options.Platform.Transport.PipeConfig.PipeName;

                OverrideValueIfSupplied(overrides.Value.ClientProcessId, value => options.Server.ClientProcessId = value);
                OverrideValueIfSupplied(overrides.Value.ConnectTimeoutSeconds, value => options.Server.ConnectTimeoutSeconds =  value);
                OverrideValueIfSupplied(overrides.Value.HealthCheckIntervalSeconds, value => options.Server.HealthCheckIntervalSeconds =  value);
                OverrideValueIfSupplied(overrides.Value.ShutdownTimeoutSeconds, value => options.Server.ShutdownTimeoutSeconds = value);
                OverrideValueIfSupplied(overrides.Value.TraceLevel, value => options.Server.TraceLevel = value);
                OverrideValueIfSupplied(overrides.Value.Verbose, value => options.Server.Verbose = value);

                OverrideStringIfSupplied(overrides.Value.DefaultLocation, value => options.Workspace.DefaultLocation = value);

                // dev mode (unsigned extensions) is either overridden via command-line args, or inactive:
                OverrideValueIfSupplied(overrides.Value.UnsafeDevMode, value => options.Server.UnsafeDevMode = value);
            }
            if (typeof(TApp).IsAssignableTo(typeof(ILanguageClientApp)))
            {
                options.Server.ClientProcessId = Environment.ProcessId;
                options.Platform.Transport.PipeConfig.PipeName = options.Platform.Transport.PipeConfig.GetRandomPipeName();
            }
        });
    }

    /// <summary>
    /// Configures only the services needed to resolve the <see cref="IRDCoreApp"/> instance.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> being built this <em>application host</em>.</param>
    protected virtual void ConfigureExternalServices(IServiceCollection services)
    {
        if (typeof(TApp) is ILanguageServerApp)
        {
            services
                .AddSingleton<IHealthCheckService<ILanguageServerApp>, HealthCheckService<ILanguageServerApp>>();
        }
        else
        {
            services
                .AddSingleton<IRDCoreLanguageServerProcess, RDCoreLanguageServerProcess>()
                .AddSingleton<IHealthCheckService<ILanguageClientApp>, HealthCheckService<ILanguageClientApp>>();
        }

        services
            .AddSingleton<IServerStateProvider, ServerStateProvider>()
            .AddSingleton<IFileSystem, FileSystem>()
            .AddSingleton<ILanguageServerProtocolTransportLayer, RDCorePlatformDefaultTransportLayer>();
    }

    /// <summary>
    /// <em>External services</em> bootstrap the application and support functionality at the <em>entry point</em> level by
    /// registering the services that must be injected in the <see cref="IRDCoreApp"/> application.
    /// </summary>
    /// <param name="services">The <c>IServiceCollection</c> to configure services.</param>
    protected virtual void ConfigureAdditionalExternalServices(IServiceCollection services)
    {
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

    /// <summary>
    /// Disposes any managed resources held in this instance.
    /// </summary>
    public void Dispose()
    {        
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
