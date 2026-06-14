using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.IO.Abstractions;
using System.Reflection;
using System.Text;

namespace RDCore.SDK.Server
{
    /// <summary>
    /// A <c>RDCore.SDK</c> application host.
    /// </summary>
    /// <remarks>
    /// 👉 This class is inherited by both <see cref="RDCoreLanguageServerHost{TApp}"/> and <see cref="RDCoreLanguageClientHost{TApp}"/>
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
            try
            {
                var builder = Host.CreateApplicationBuilder();

                var configuration = builder.Configuration;
                configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                Configure(configuration, builder.Services, args);

                ConfigureExternalServices(builder.Services, configuration);
                ConfigureAdditionalExternalServices(builder.Services, configuration);

                _host = builder.Build();
                _app = _host.Services.GetRequiredService<TApp>();

                await BeforeAppStartAsync(_host.Services);

                try
                {
                    _hostTask = _host.StartAsync();
                    await _app.RunAsync(_host.Services);
                    await _hostTask;
                }
                finally
                {
                    await _host.StopAsync();
                }
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
                Console.OutputEncoding = Encoding.Unicode;
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
        protected virtual void Configure(IConfiguration configuration, IServiceCollection services, string[] args) 
        {
            var overrides = CommandLine.Parser.Default.ParseArguments<SdkAppCommandLineArgs>(args);
            var canOverride = !overrides.Errors.Any();

            var config = configuration.GetSection("Configuration");
            services.Configure<SdkAppOptions>(config);

            if (canOverride)
            {
                config.Bind(overrides);
            }
        }

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
            services
                .AddTransient<TApp>()
                .AddTransient<IServerStateProvider, ServerStateProvider>()
                .AddTransient<IRDCoreLanguageServerProcess, RDCoreLanguageServerProcess>() // FIXME this one needs a provider or factory
                .AddTransient<IHealthCheckService<TApp>, HealthCheckService<TApp>>()
                .AddTransient<ILanguageServerProtocolTransportLayer, RDCorePlatformDefaultTransportLayer>()
                .AddSingleton<IFileSystem, FileSystem>()
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
            var traceLevelConfig = configuration["Server:TraceLevel"];
            if (Enum.TryParse<LogLevel>(traceLevelConfig, out var config))
            {
                builder.SetMinimumLevel(config);
            }
#if DEBUG
            builder.AddDebug();
#endif
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
}
