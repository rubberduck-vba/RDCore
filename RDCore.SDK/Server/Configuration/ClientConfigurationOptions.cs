using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Server;

namespace RDCore.SDK.Server.Configuration;

internal static class ClientConfigurationOptions
{
    // the configuration section a server asks its client for (LSP 3.17 §Configuration Request), when it is started and whenever the client says it changed.
    // workspace.configuration is what says a client answers workspace/configuration; workspace/didChangeConfiguration is a notification any client may send,
    // and workspace.didChangeConfiguration says only whether the client registers for it dynamically. OmniSharp 0.19.9 asks, and reads the notification,
    // only for a client that declares the latter: a client that declares workspace.configuration alone is one whose notification is registered statically.
    internal static LanguageServerOptions WithClientConfiguration(this LanguageServerOptions options, string section) => options
        .WithConfigurationSection(section)
        .OnInitialize((server, request, _) =>
        {
            if (Answers(request.Capabilities)
                && !server.Configuration.IsSupported
                && server.Configuration is ICapability<DidChangeConfigurationCapability> configuration)
            {
                configuration.SetCapability(new DidChangeConfigurationCapability(), request.Capabilities);
            }

            return Task.CompletedTask;
        });

    /// <summary>
    /// Whether a client answers <c>workspace/configuration</c>: it says <c>workspace.configuration</c> is <see langword="true"/>. OmniSharp reads a
    /// <c>false</c> as a capability that is declared, with the value <see langword="false"/>.
    /// </summary>
    internal static bool Answers(ClientCapabilities? capabilities) => capabilities?.Workspace?.Configuration is { IsSupported: true, Value: true };
}
