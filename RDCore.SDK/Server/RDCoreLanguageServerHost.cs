using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDCore.SDK.Server.Handlers;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;

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
public class RDCoreLanguageServerHost<TApp>() : AppHost<TApp>() 
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

    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        ServerStateProvider = new ServerStateProvider(configuration);
        services
            .AddSingleton<IServerCommandProvider, ServerCommandProvider>()
            .AddSingleton<ExecuteCommandHandler>();
    }
}
