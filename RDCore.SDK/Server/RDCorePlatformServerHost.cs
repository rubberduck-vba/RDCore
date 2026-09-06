using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Handlers;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.Diagnostics;

namespace RDCore.SDK.Server;

/// <summary>
/// Simplifies implementing a <c>RDCore</c> <em>LSP server</em> application.
/// </summary>
/// <remarks>
/// 🧩 Override templated methods to customize your application.<br/>
/// <list type="bullet">
/// <item>Implement (<c>override</c>) <see cref="AppHost{TApp}.ConfigureExternalLogging(IServiceCollection, ILoggingBuilder, IConfiguration)"/> to override the default <see cref="ILoggingBuilder"/> providers.</item>
/// </list>
/// </remarks>
public class RDCorePlatformServerHost<TApp>() : AppHost<TApp>() 
    where TApp : class, IRDCoreServerApp
{
    /// <summary>
    /// The service that manages the operational state of the language server. Resolved from the built
    /// host so it is the same singleton the app, the lifecycle handlers and the health check share.
    /// </summary>
    protected IServerStateProvider ServerStateProvider
        => HostServices?.GetService<IServerStateProvider>()
           ?? throw new InvalidOperationException("The server state provider is not available until the host is built.");
    /// <summary>
    /// Gets the application exit code corresponding to the current <see cref="ServerState"/>.
    /// </summary>
    public override int ExitCode => HostServices?.GetService<IServerStateProvider>()?.State.ExitCode ?? 1;

    protected override void Configure(IConfigurationBuilder configuration, IServiceCollection services, string[] args)
    {
        var result = CommandLine.Parser.Default.ParseArguments<SdkAppCommandLineArgs>(args);
        var parsed = result.Value
            ?? throw new ArgumentException($"Could not parse command-line arguments: {string.Join(" ", args)}");

        // a server app cannot start without a pipe name and a workspace:
        _ = parsed.PipeName ?? throw new ArgumentNullException(nameof(SdkAppCommandLineArgs.PipeName));
        _ = parsed.WorkspaceUri ?? throw new ArgumentNullException(nameof(SdkAppCommandLineArgs.WorkspaceUri));

        configuration.AddInMemoryCollection(parsed.ToConfigurationOverrides());
    }
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ExecuteCommandHandler>();
    }
}
