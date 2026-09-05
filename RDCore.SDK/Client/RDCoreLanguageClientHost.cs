using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;

namespace RDCore.SDK.Client;

/// <summary>
/// Simplifies implementing a <c>RDCore</c> <em>LSP client</em> application.
/// </summary>
/// <remarks>
/// 🧩 <c>override</c> templated methods to customize your application.<br/>
/// <list type="bullet">
/// <item>Implement <see cref="AppHost{TApp}.Configure(IConfiguration, Microsoft.Extensions.DependencyInjection.IServiceCollection, string[])"/> to override the default <see cref="IConfiguration"/> providers.</item>
/// <item>Implement <see cref="AppHost{TApp}.ConfigureExternalLogging(Microsoft.Extensions.DependencyInjection.IServiceCollection, ILoggingBuilder, IConfiguration)(Microsoft.Extensions.DependencyInjection.IServiceCollection, ILoggingBuilder, IConfiguration)"/> to override the default <see cref="ILoggingBuilder"/> providers.</item>
/// </list>
/// </remarks>
/// <typeparam name="TApp">A specific class type implementing <see cref="IRDCoreClientApp"/>.</typeparam>
public abstract class RDCoreLanguageClientHost<TApp>() : AppHost<TApp>()
    where TApp : class, IRDCoreClientApp
{
    protected sealed override void Configure(IConfigurationBuilder configuration, IServiceCollection services, string[] args)
    {
        var commandLineArgs = CommandLine.Parser.Default.ParseArguments<SdkAppCommandLineArgs>(args);
        var overrides = new Dictionary<string, string?>
        {
            ["Configuration:Workspace:WorkspaceUri"] = commandLineArgs.Value.WorkspaceUri ?? throw new ArgumentNullException("args[WorkspaceUri]"),
            ["Configuration:Server:TraceLevel"] = commandLineArgs.Value.TraceLevel?.ToString() ?? LogLevel.None.ToString(),
            ["Configuration:Server:Verbose"] = commandLineArgs.Value.Verbose?.ToString() ?? false.ToString(),
        };
        foreach (var (key, value) in ConfigureOverrides(args, commandLineArgs.Value))
        {
            overrides[key] = value;
        }
        configuration.AddInMemoryCollection(overrides);
    }

    /// <summary>
    /// Override to supply additional command-line argument configuration overrides.
    /// </summary>
    /// <param name="initialArgs">The command-line arguments as received.</param>
    /// <param name="baseArgs">The arguments as base SDK app configuration.</param>
    /// <returns></returns>
    protected virtual IEnumerable<(string, string?)> ConfigureOverrides(string[] initialArgs, SdkAppCommandLineArgs baseArgs) => [];

    /// <summary>
    /// Keeps the client <em>process</em> alive for the lifetime of the JSON-RPC connection.
    /// </summary>
    /// <remarks>
    /// <see cref="RDCoreClientApp.RunAsync(IServiceProvider, string[])"/> returns as soon as the language client is
    /// connected and initialized. A <strong>standalone</strong> client process (e.g. <c>rdc.exe</c>) must not exit at
    /// that point, otherwise the transport pipe closes and the language server tears down. Block here until the host
    /// is asked to stop (Ctrl+C / <c>SIGTERM</c>, surfaced as <see cref="IHostApplicationLifetime.ApplicationStopping"/>)
    /// or <see cref="AppHost{TApp}.ProcessTokenSource"/> is cancelled.
    /// </remarks>
    protected override async Task AfterAppRunAsync(IServiceProvider provider)
    {
        var lifetime = provider.GetRequiredService<IHostApplicationLifetime>();
        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        static void Signal(object? state) => ((TaskCompletionSource)state!).TrySetResult();
        using var lifetimeRegistration = lifetime.ApplicationStopping.Register(Signal, shutdown);
        using var processRegistration = ProcessTokenSource.Token.Register(Signal, shutdown);

        LogIfEnabled(LogLevel.Information, "🔌 Language client connected. Press Ctrl+C to disconnect and exit.");
        await shutdown.Task;
        LogIfEnabled(LogLevel.Information, "🔌 Shutdown signal received; disconnecting language client...");
    }
}
