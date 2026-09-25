using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The shell's view of the platform, over the one connection it has: the language client attached to
/// the language server.
/// </summary>
/// <param name="client">The running language client — <c>rdc.exe</c>'s own LSP client app.</param>
internal sealed class ReplPlatformClient(IRDCoreClientApp client) : IReplPlatformClient
{
    /// <inheritdoc/>
    public bool Provides<TCapability>() where TCapability : CorePlatformClientCapability
        => client.PlatformInfo?.Provides<TCapability>() ?? false;

    /// <inheritdoc/>
    public Task<SessionStatusResult> GetSessionStatusAsync(int waitMilliseconds, CancellationToken token)
        => client.SendRequestAsync<SessionStatusParams, SessionStatusResult>(
            new SessionStatusParams { WaitMilliseconds = waitMilliseconds }, token);

    /// <inheritdoc/>
    public Task<ExecuteSessionResult> ExecuteAsync(string source, string moduleName, string entryPoint, CancellationToken token)
        => client.SendRequestAsync<ExecuteSessionParams, ExecuteSessionResult>(
            new ExecuteSessionParams { Source = source, ModuleName = moduleName, EntryPoint = entryPoint }, token);
}
