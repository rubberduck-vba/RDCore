using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;

namespace RDCore.CLI.Host;

/// <summary>
/// The dialogs of the environment host's program, asked of the language server it serves (<c>rdcore/host/interaction/*</c>).
/// </summary>
/// <param name="app">The environment host's server app, connected to the language server.</param>
internal sealed class LanguageServerInteractionChannel(RDCoreServerApp app) : IHostInteractionChannel
{
    /// <inheritdoc/>
    public Task<MessageBoxResult> MessageBoxAsync(HostMessageBoxParams request, CancellationToken token)
        => app.SendRequestAsync<HostMessageBoxParams, MessageBoxResult>(request, token);

    /// <inheritdoc/>
    public Task<InputBoxResult> InputBoxAsync(HostInputBoxParams request, CancellationToken token)
        => app.SendRequestAsync<HostInputBoxParams, InputBoxResult>(request, token);
}
