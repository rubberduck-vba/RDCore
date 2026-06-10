using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services.States;

namespace RDCore.SDK.Server;

/// <summary>
/// Simplifies implementing a <c>RDCore</c> <em>LSP server</em> application.
/// </summary>
/// <param name="ProcessTokenSource">A <see cref="CancellationTokenSource"/> created in the application entry point.</param>
/// <remarks>
/// 🧩 Use this class directly as-is, or <c>override</c> templated methods to customize your application.<br/>
/// <list type="bullet">
/// <item>Implement (<c>override</c>) <see cref="AppHost{TApp}.ConfigureLogging"/> to override the default <see cref="ILoggingBuilder"/> providers.</item>
/// </list>
/// <c>TApp</c> is <see cref="ILanguageServerApp"/>.
/// </remarks>
public class RDCoreLanguageServerHost(CancellationTokenSource ProcessTokenSource)
    : AppHost<ILanguageServerApp>(ProcessTokenSource)
{
    /// <summary>
    /// Gets a service that manages the operational state of the language server.
    /// </summary>
    protected IServerStateProvider ServerStateProvider { get; private set; } = default!;
    /// <summary>
    /// Gets the application exit code corresponding to the current <see cref="ServerState"/>.
    /// </summary>
    public int ExitCode => ServerStateProvider.State.ExitCode;

    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IOptions<SdkAppOptions> options)
    {
        ServerStateProvider = new ServerStateProvider(options.Value.Server);
        services.AddSingleton<IServerStateProvider>(provider => ServerStateProvider);
    }
}
