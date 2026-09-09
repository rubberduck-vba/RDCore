using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Platform;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using RDCore.SDK.Workspace;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Reflection;
using System.Text;

namespace RDCore.SDK.Server;

/// <summary>
/// A <c>RDCore.SDK</c> application host.
/// </summary>
/// <remarks>
/// 👉 This class is inherited by both <see cref="RDCorePlatformServerHost{TApp}"/> and <see cref="RDCoreLanguageClientHost{TApp}"/>
/// to encapsulate a common interface to simplify implementing any kind of SDK application.<br/>
/// </remarks>
/// <typeparam name="TApp">The type of <see cref="IRDCoreApp"/> being hosted.</typeparam>
public abstract class AppHost<TApp>() : IDisposable
    where TApp : class, IRDCoreApp
{
    private bool _disposed;
    private IHost? _host;
    private Task? _hostTask;
    private TApp? _app;

    private static readonly Lazy<AssemblyName> _info = new(() => Assembly.GetEntryAssembly()?.GetName()!, LazyThreadSafetyMode.PublicationOnly);

    protected readonly CancellationTokenSource ProcessTokenSource = new();

    /// <summary>
    /// The built host's service provider, or <c>null</c> before <see cref="BuildAndRunAsync"/> has built the host.
    /// </summary>
    protected IServiceProvider? HostServices => _host?.Services;

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
    /// Gets the application's exit code.
    /// </summary>
    public virtual int ExitCode => 0;

    /// <summary>
    /// 🧩 A method that runs after configuration but before the application is resolved and actually started.<br/>
    /// Base implementation returns a <see cref="Task.CompletedTask"/>
    /// </summary>
    /// <param name="provider">The constructed service provider.</param>
    protected virtual Task BeforeAppStartAsync(IServiceProvider provider) => Task.CompletedTask;

    /// <summary>
    /// 🧩 A method that runs after <see cref="IRDCoreApp.RunAsync"/> returns, but before the host is stopped.<br/>
    /// Base implementation returns a <see cref="Task.CompletedTask"/>.
    /// </summary>
    /// <remarks>
    /// 👉 A <strong>server</strong> app blocks inside <c>RunAsync</c> until its LSP server exits, so the base no-op is correct for it.<br/>
    /// 👉 A <strong>standalone LSP client</strong> process (e.g. <c>rdc.exe</c>) returns from <c>RunAsync</c> as soon as the
    /// JSON-RPC connection is established; it <c>override</c>s this method to keep the process alive for the lifetime of that connection.
    /// </remarks>
    /// <param name="provider">The constructed service provider.</param>
    protected virtual Task AfterAppRunAsync(IServiceProvider provider) => Task.CompletedTask;

    /// <summary>
    /// Builds the host, resolves and runs the <c>TApp</c> application.
    /// </summary>
    /// <remarks>
    /// Overrides should invoke the base implementation to run the base protocol.
    /// </remarks>
    protected virtual async Task BuildAndRunAsync(HostApplicationBuilder builder, string[] args)
    {
        _host = builder.Build();
        _app = _host.Services.GetRequiredService<TApp>();
        LogIfEnabled(LogLevel.Information, "Application resolved successfully. Starting application host...");

        await BeforeAppStartAsync(_host.Services);

        try
        {            
            _hostTask = _host.StartAsync(ProcessTokenSource.Token);
            LogIfEnabled(LogLevel.Information, "Host started; starting application...");

            await _app.RunAsync(_host.Services, args);
            await AfterAppRunAsync(_host.Services);
            await _hostTask;
        }
        catch (OperationCanceledException)
        {
            LogIfEnabled(LogLevel.Information, "Operation was cancelled.");
        }
        catch (Exception exception)
        {
            LogIfEnabled(LogLevel.Error, exception.ToString());
        }
        finally
        {
            // bounded: a wedged hosted service (e.g. the OmniSharp Rx pipeline) must not hang process exit.
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
            {
                LogIfEnabled(LogLevel.Warning, $"Host did not stop cleanly: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// Runs the <c>RDCore.SDK</c> client/server application.
    /// </summary>
    /// <remarks>
    /// <strong><c>RDCore.SDK</c> plugins should <c>await</c> this method inside a <c>try...catch</c> block</strong> in the application's entry point (<c>Program.cs</c>) 
    /// to block execution until the internal <c>Omnisharp</c> LSPserver exits.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Signals a <strong>normal exit</strong>; host application process should exit with code 0.</exception>
    /// <exception cref="Exception">Any other exception type is unexpected and if it is fatal, the host application process should exit with a non-zero error code.</exception>
    public async Task<int> RunAsync(string[] args)
    {
        // UTF-8 for console output: correct for a terminal and for a redirected pipe alike
        // (UTF-16 corrupts both). Guarded because setting it can throw on some redirected handles.
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
        }

        try
        {
            var builder = Host.CreateApplicationBuilder();
            var configuration = builder.Configuration;
            configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            Configure(configuration, builder.Services, args);

            // bound the generic host's shutdown so a wedged background task cannot hang the process
            // (this also bounds ConsoleLifetime's ProcessExit wait).
            builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));

            ConfigureExternalServices(builder.Services, configuration);
            ConfigureAdditionalExternalServices(builder.Services, configuration);

            await BuildAndRunAsync(builder, args);
        }
        catch (OperationCanceledException)
        {
            // normal exit
        }
        catch (Exception exception)
        {
            // something went wrong:
            Console.WriteLine(exception.ToString());
            return -1;
        }
        finally
        {
            Console.WriteLine("V I V A T  ♥  C U C U M I S ™\n©Copyright 2026 9562-7303 Québec inc.");
        }

        return ExitCode;
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
    protected abstract void Configure(IConfigurationBuilder configuration, IServiceCollection services, string[] args);

    /// <summary>
    /// Configures only the services needed to resolve the <see cref="IRDCoreApp"/> instance.
    /// </summary>
    /// <param name="services">The service provider of the application host being configured.</param>
    /// <param name="configuration">The current application configuration.</param>
    /// <remarks>
    /// 🧩 If you don't intend to <strong>overwrite the core service registrations</strong>, 
    /// you probably want to <c>override</c> <see cref="ConfigureAdditionalExternalServices"/> instead.
    /// </remarks>
    protected virtual void ConfigureExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        var config = configuration.GetSection("Configuration");
        services.Configure<SdkAppOptions>(config);
        services.Configure<SdkServerOptions>(config.GetSection("Server"));

        services
            .AddSingleton<IRuntimeEnvironmentProfile>(sp =>
                RuntimeEnvironmentProfile.From(sp.GetRequiredService<IOptions<SdkAppOptions>>().Value.Environment))
            .AddSingleton<TApp>()
            // stateful: owns the lifecycle state and the process/shutdown token sources. The server app,
            // the LSP lifecycle handlers, and the health check must all observe the same instance.
            .AddSingleton<IServerStateProvider, ServerStateProvider>()
            .AddTransient<IRDCoreServerProcess, RDCoreServerProcess>()
            .AddTransient<IHealthCheckService<TApp>, HealthCheckService<TApp>>()
            .AddTransient<ILanguageServerProtocolTransportLayer, RDCorePlatformDefaultTransportLayer>()
            .AddSingleton<IChildConnectionFactory, ChildConnectionFactory>()
            .AddSingleton<IFileSystem, FileSystem>()
            .AddSingleton<IProjectFileLoader, ProjectFileLoader>()
            .AddSingleton<IProjectFileWriter, ProjectFileWriter>()
            .AddSingleton<IPlatformEnvironment, PlatformEnvironment>()
            .AddSingleton<IPlatformCompositionService, PlatformCompositionService>()
            .AddSingleton<IExtensionsProvider, ExtensionsClient>()
            .AddSingleton<IExtensionManifestValidationService, ExtensionManifestValidationService>()
            .AddLogging(builder => ConfigureExternalLogging(services, builder, configuration));
    }

    /// <summary>
    /// Configures any additional services that must be injected in the application constructor.<br/>
    /// 🧩 The base implementation does nothing.
    /// </summary>
    /// <remarks>
    /// 👉 The purpose of <strong>external services</strong> is to bootstrap the application and support functionality at the <em>entry point</em> level by
    /// registering the services that must be injected in the <see cref="IRDCoreApp"/> application.
    /// </remarks>
    /// <param name="services">The service provider of the application host being configured.</param>
    /// <param name="configuration">The current application configuration.</param>
    protected virtual void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <summary>
    /// Configures the <em>external service provider</em> logging targets for a <c>RDCore.SDK</c> client/server application.
    /// </summary>
    /// <param name="services">The service provider of the application host being configured.</param>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to configure logging providers.</param>
    /// <param name="configuration">The current application configuration.</param>
    /// <remarks>
    /// 👉 The <strong>external services</strong> are not registered with the <c>OmniSharp</c> language server host.
    /// <br/>Their purpose is to bootstrap the application and support functionality at the <em>entry point</em> level by
    /// registering the services that must be injected in the <see cref="IRDCoreApp"/> application.
    /// <br/><br/>🧩 The default/base implementation:
    /// <list type="bullet">
    /// <item>Sets the effective <em>minimum log level</em> as per the supplied <see cref="SdkServerOptions.TraceLevel"/>.</item>
    /// <item>Adds a <c>Debug</c> logger in debug builds.</item>
    /// </list>
    /// </remarks>
    protected virtual void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        builder.SetMinimumLevel(ResolveTraceLevel(configuration));

        // Server:JsonRpcTrace flips OmniSharp's protocol logging on without turning the whole log to Debug.
        if (configuration.GetValue("Configuration:Server:JsonRpcTrace", false))
        {
            builder.AddFilter("OmniSharp", LogLevel.Trace);
        }

        builder.AddDebug();
    }

    /// <summary>
    /// The effective minimum log level — <see cref="SdkServerOptions.TraceLevel"/> from configuration,
    /// or <see cref="LogLevel.Information"/> when it is unset or invalid. Pass this to the
    /// <c>AddFile(path, level)</c> overload in a <see cref="ConfigureExternalLogging"/> override; the
    /// bare <c>AddFile(path)</c> pins the file at <see cref="LogLevel.Information"/> and hides
    /// JSON-RPC protocol tracing.
    /// </summary>
    protected static LogLevel ResolveTraceLevel(IConfiguration configuration)
        => Enum.TryParse<LogLevel>(configuration["Configuration:Server:TraceLevel"], out var level) ? level : LogLevel.Information;

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
                // the host owns the TApp singleton's lifetime and disposes it; disposing _app here
                // as well ran RDCoreClientApp/RDCoreServerApp.Dispose() twice.
                _host?.Dispose();
                ProcessTokenSource.Dispose();
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
