using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using RDCore.SDK.Formatting;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Workspace;

/// <summary>
/// Offers, once, to import the indenter settings the user had in Rubberduck, when the language server starts (<see cref="IndenterSettingsOffer"/>): to a client that
/// reads them (<see cref="SDK.Client.RubberduckIndenterSettings"/>), when the user never set a <c>Formatting</c> setting.
/// </summary>
/// <remarks>
/// A client says what it expects, and what its user set, as it starts: the offer waits for the handshake, and for the first time the client's settings are set. The
/// offering must be made before the client's settings can be set, so that it does not miss them.
/// </remarks>
internal sealed class IndenterSettingsOffering : IDisposable
{
    private readonly ILanguageServer _server;
    private readonly IPlatformClientCapabilitiesService _capabilities;
    private readonly IndenterSettingsOffer _offer;
    private readonly ILiveSettings _liveSettings;
    private readonly TimeSpan _handshakeWait;
    private readonly TimeSpan _settingsWait;
    private readonly Action<string> _log;
    private readonly TaskCompletionSource _settingsSaid = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="server">The language server.</param>
    /// <param name="capabilities">What the client expects of the platform.</param>
    /// <param name="offer">The offer, which remembers it was made.</param>
    /// <param name="liveSettings">The live settings, which hold what the client says its user set.</param>
    /// <param name="handshakeWait">How long the platform handshake is waited for.</param>
    /// <param name="settingsWait">How long the client's settings are waited for.</param>
    /// <param name="log">Says why an offer was not made, when it is not for want of anything to offer.</param>
    public IndenterSettingsOffering(
        ILanguageServer server, IPlatformClientCapabilitiesService capabilities, IndenterSettingsOffer offer, ILiveSettings liveSettings, TimeSpan handshakeWait, TimeSpan settingsWait, Action<string> log)
    {
        _server = server;
        _capabilities = capabilities;
        _offer = offer;
        _liveSettings = liveSettings;
        _handshakeWait = handshakeWait;
        _settingsWait = settingsWait;
        _log = log;
        _liveSettings.Changed += SettingsSaid;

        // the client's settings may be in already: the follower starts before this does.
        if (_liveSettings.HasReceived)
        {
            _settingsSaid.TrySetResult();
        }
    }

    private void SettingsSaid(object? sender, EventArgs e) => _settingsSaid.TrySetResult();

    /// <summary>
    /// Makes the offer, if there is one to make.
    /// </summary>
    /// <param name="token">A token that cancels the offer.</param>
    /// <returns>What was imported; <see langword="null"/> when nothing was offered, or the user chose not to import.</returns>
    public async Task<IndenterSettingsImportOutcome?> RunAsync(CancellationToken token)
    {
        var deadline = DateTime.UtcNow + _handshakeWait;
        while (!_capabilities.IsHandshakeCompleted && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, token);
        }

        var client = new RubberduckIndenterSettingsClient(_server, _capabilities);
        if (!client.ClientExpects || _offer.WasMade)
        {
            return null;
        }

        // what the client says its user set arrives with the first pull of its configuration; a client that has none to pull has nothing the user set.
        if (_server.ClientSettings.Capabilities?.Workspace?.Configuration.IsSupported == true)
        {
            try
            {
                await _settingsSaid.Task.WaitAsync(_settingsWait, token);
            }
            catch (TimeoutException)
            {
                _log("The client did not say its settings in time: the indenter settings of Rubberduck are not offered this time.");
                return null;
            }
        }

        return await _offer.OfferAsync(
            _liveSettings.Said.Keys.Any(key => key.StartsWith("Formatting:", StringComparison.OrdinalIgnoreCase)),
            client.ReadAsync,
            async (message, choices, askToken) => (await _server.Window.ShowMessageRequest(new ShowMessageRequestParams
            {
                Type = MessageType.Info,
                Message = message,
                Actions = new Container<MessageActionItem>(choices.Select(choice => new MessageActionItem { Title = choice })),
            }, askToken))?.Title,
            client.ApplyAsync,
            token);
    }

    /// <inheritdoc/>
    public void Dispose() => _liveSettings.Changed -= SettingsSaid;
}
