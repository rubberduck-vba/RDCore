using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RDCore.SDK.Client;

/// <summary>
/// An abstract class to simplify implementing a <c>RDCore</c> <em>LSP client</em> application.
/// </summary>
/// <param name="ServiceProvider"></param>
/// <param name="TokenSource"></param>
public abstract class RDCoreLanguageClientApp(IServiceProvider ServiceProvider, CancellationTokenSource TokenSource)
{
    protected IServiceProvider Services { get; } = ServiceProvider;
    protected CancellationTokenSource TokenSource { get; } = TokenSource;

    public async Task StartupAsync()
    {
        var logger = Services.GetRequiredService<ILogger>();
        var options = Services.GetRequiredService<IOptions<ServerStartupSettings>>();
        await StartLanguageServerProcessAsync(options, logger);
    }

    private async Task StartLanguageServerProcessAsync(IOptions<ServerStartupSettings> options, ILogger logger)
    {
        var process = Services.GetRequiredService<IRDCoreLanguageServerProcess>();

        await BeforeStartLanguageServerProcessAsync(options, logger, TokenSource.Token);
        process.Start();
    }

    /// <summary>
    /// This method is invoked after successfully initializing everything required to start monitoring a server process and connect the LSP client.
    /// </summary>
    /// <param name="options">The effective startup options that will be used. This value is immutable; use <c>ConfigureServerStartupOptions</c> if you must configure it further.</param>
    /// <param name="logger">A logger that outputs to the CLI console.</param>
    /// <param name="token">A token that signals that the task should be abourted.</param>
    protected virtual async Task BeforeStartLanguageServerProcessAsync(IOptions<ServerStartupSettings> options, ILogger logger, CancellationToken token)
    {
        logger.LogDebug("🧩 Extensibility: BeforeStartLanguageServerProcessAsync()");
    }

    /// <summary>
    /// This method is invoked after successfully initializing everything required to start monitoring a server process and connect the LSP client.
    /// </summary>
    /// <param name="options">The effective startup options that will be used. This value is immutable; use <c>ConfigureServerStartupOptions</c> if you must configure it further.</param>
    /// <param name="logger">A logger that outputs to the CLI console.</param>
    protected virtual async Task BeforeClientConnectAsync(IOptions<ServerStartupSettings> options, ILogger logger)
    {
        logger.LogDebug("🧩 Extensibility: BeforeClientConnectAsync()");
    }
}
