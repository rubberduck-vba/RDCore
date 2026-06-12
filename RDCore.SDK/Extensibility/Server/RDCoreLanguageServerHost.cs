using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RDCore.SDK.Extensibility.Configuration;
using RDCore.SDK.Extensibility.Server.Services.States;

namespace RDCore.SDK.Extensibility.Server;

/// <summary>
/// Simplifies implementing a <c>RDCore</c> <em>LSP server</em> application.
/// </summary>
/// <param name="ProcessTokenSource">A <see cref="CancellationTokenSource"/> created in the application entry point.</param>
/// <remarks>
/// 🧩 Use this class directly as-is, or <c>override</c> templated methods to customize your application.<br/>
/// <c>TApp</c> is <see cref="ILanguageServerApp"/>.
/// </remarks>
public class RDCoreLanguageServerHost<TApp>(CancellationTokenSource ProcessTokenSource)
    : AppHost<TApp>(ProcessTokenSource)
where TApp: LanguageServerApp
{
    /// <summary>
    /// Gets a service that manages the operational state of the language server.
    /// </summary>
    protected IServerStateProvider ServerStateProvider { get; private set; } = default!;
    /// <summary>
    /// Gets the application exit code corresponding to the current <see cref="ServerState"/>.
    /// </summary>
    public int ExitCode => ServerStateProvider.State.ExitCode;

    /// <summary>
    /// Configures only the services needed to resolve the <see cref="IRDCoreApp"/> instance.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> being built this <em>application host</em>.</param>
    protected sealed override void ConfigureExternalServices(IServiceCollection services)
    {
        base.ConfigureExternalServices(services);
        services.AddSingleton<ILanguageServerApp, TApp>();

        services.AddSingleton(provider => ServerStateProvider = new ServerStateProvider(provider.GetRequiredService<IOptions<SdkServerOptions>>()));
    }
}
