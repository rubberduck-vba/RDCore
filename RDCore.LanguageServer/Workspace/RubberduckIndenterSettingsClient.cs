using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Workspace;

/// <summary>
/// The client's side of the import of Rubberduck's indenter settings: the two requests the language server sends it
/// (<see cref="RubberduckIndenterSettingsParams"/>, <see cref="RubberduckIndenterSettingsApplyParams"/>).
/// </summary>
/// <remarks>
/// Only a client that expects <see cref="SDK.Client.RubberduckIndenterSettings"/> is asked.
/// </remarks>
/// <param name="server">The language server, which sends the requests.</param>
/// <param name="capabilities">What the client expects of the platform.</param>
internal sealed class RubberduckIndenterSettingsClient(ILanguageServerFacade server, IPlatformClientCapabilitiesService capabilities)
{
    /// <summary>Whether the client reads the settings and applies the ones it is given.</summary>
    public bool ClientExpects => capabilities.Expects(group => group.RubberduckIndenterSettings);

    /// <summary>Asks the client what it found of Rubberduck's indenter settings.</summary>
    public Task<RubberduckIndenterSettingsResult> ReadAsync(CancellationToken token)
        => server.SendRequest(new RubberduckIndenterSettingsParams(), token);

    /// <summary>Asks the client to take the settings as its own.</summary>
    public Task<RubberduckIndenterSettingsApplyResult> ApplyAsync(RubberduckIndenterSettingsApplyParams settings, CancellationToken token)
        => server.SendRequest(settings, token);
}
