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
    /// Gets a service that manages the operational state of the language server.
    /// </summary>
    protected IServerStateProvider ServerStateProvider { get; private set; } = default!;
    /// <summary>
    /// Gets the application exit code corresponding to the current <see cref="ServerState"/>.
    /// </summary>
    public override int ExitCode => ServerStateProvider.State.ExitCode;

    protected override void Configure(IConfigurationBuilder configuration, IServiceCollection services, string[] args)
    {
        var parsed = CommandLine.Parser.Default.ParseArguments<SdkAppCommandLineArgs>(args).Value;

        // a server app cannot start without a pipe name and a workspace:
        _ = parsed.PipeName ?? throw new ArgumentNullException(nameof(SdkAppCommandLineArgs.PipeName));
        _ = parsed.WorkspaceUri ?? throw new ArgumentNullException(nameof(SdkAppCommandLineArgs.WorkspaceUri));

        configuration.AddInMemoryCollection(parsed.ToConfigurationOverrides());
    }
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        ServerStateProvider = new ServerStateProvider(configuration);
        services.AddSingleton<ExecuteCommandHandler>();
    }
}
