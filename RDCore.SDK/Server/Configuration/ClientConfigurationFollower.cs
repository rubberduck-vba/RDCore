using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Formatting;

namespace RDCore.SDK.Server.Configuration;

/// <summary>
/// Follows the configuration a client owns (<strong>LSP 3.17</strong> §Configuration Request): when the server is initialized, and whenever the client says it changed, the
/// live settings the client said are set on the component's settings (<see cref="ILiveSettings"/>), with the tab size of the client's editor for an indentation the user never set
/// (<see cref="FormattingTabSize"/>).
/// </summary>
/// <remarks>
/// <para>
/// The settings are set once, as a whole: whoever listens for the change (<see cref="ILiveSettings.Changed"/>, <c>changed</c>) sees the indentation it ends up with, never
/// the one before the editor's tab size was known. A change that arrives while the editor is being asked is the last word: the older one is not set.
/// </para>
/// <para>
/// A follower that is disposed stops: a change that is being followed is cancelled, and sets nothing - a connection that is lost, or a server that shuts down, is not a client
/// whose editor has no tab size.
/// </para>
/// </remarks>
public sealed class ClientConfigurationFollower : IDisposable
{
    private readonly ILanguageServer _server;
    private readonly ILiveSettings _liveSettings;
    private readonly Func<Task> _changed;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _lifetime = new();
    private IDisposable? _subscription;
    private readonly object _gate = new();
    private readonly object _applying = new();
    private int _version;
    private int _disposed;

    /// <summary>
    /// A follower of the configuration of a server's client.
    /// </summary>
    /// <param name="server">The language server, whose configuration the client answers.</param>
    /// <param name="liveSettings">The component's live settings.</param>
    /// <param name="changed">Called once the settings were set.</param>
    /// <param name="log">Says what happened.</param>
    public ClientConfigurationFollower(ILanguageServer server, ILiveSettings liveSettings, Func<Task> changed, Action<string> log)
    {
        _server = server;
        _liveSettings = liveSettings;
        _changed = changed;
        _log = log;
    }

    /// <summary>
    /// Follows the configuration of a server's client, if the component has live settings to set.
    /// </summary>
    /// <param name="services">The services of the component (<see cref="ILiveSettings"/> is among them).</param>
    /// <param name="server">The language server.</param>
    /// <param name="changed">Called once the settings were set.</param>
    /// <param name="log">Says what happened.</param>
    /// <returns>The follower, already started; <see langword="null"/> when the component has no live settings.</returns>
    public static ClientConfigurationFollower? Follow(IServiceProvider? services, ILanguageServer server, Func<Task> changed, Action<string> log)
    {
        if (services?.GetService<ILiveSettings>() is not { } liveSettings)
        {
            return null;
        }

        var follower = new ClientConfigurationFollower(server, liveSettings, changed, log);
        follower.Start();
        return follower;
    }

    /// <summary>
    /// Starts following: the client's answers set the settings from now on.
    /// </summary>
    public void Start() => _subscription ??= ChangeToken.OnChange(_server.Configuration.GetReloadToken, () => _ = FollowAsync());

    /// <summary>
    /// Sets the live settings the client said, with the editor's tab size for an indentation the user never set.
    /// </summary>
    /// <returns>Whether the settings were set: not when a newer change came in while the editor's tab size was asked, nor when the follower was disposed.</returns>
    public async Task<bool> FollowAsync()
    {
        int version;
        lock (_gate)
        {
            version = ++_version;
        }

        try
        {
            var token = _lifetime.Token;
            var said = LiveSettingsProtocol.Read(_server.Configuration);
            var tabSize = await FormattingTabSize.RequestEditorTabSizeAsync(_server, token);
            var adopted = FormattingTabSize.AdoptedFrom(said, tabSize);

            // the settings are set one change at a time, and a change is set only if no newer one came in and the follower is not disposed - decided under a lock that is
            // held for no longer than that: a listener of the change, which runs while the settings are set, can wait for another thread to follow or to dispose.
            lock (_applying)
            {
                lock (_gate)
                {
                    if (version != _version || _disposed == 1)
                    {
                        return false;
                    }
                }

                _liveSettings.Apply(said, adopted);
            }

            lock (_gate)
            {
                if (_disposed == 1)
                {
                    return false;
                }
            }

            _log($"⚙️ The client's live settings were applied ({said.Count} value(s)){(adopted.Count > 0 ? $"; the indentation is the client's tab size ({adopted.Values.Single()}), which no setting overrides" : string.Empty)}.");
            await _changed();
            return true;
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
        catch (Exception exception)
        {
            _log($"The client's live settings could not be applied: {exception.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed == 1)
            {
                return;
            }

            _disposed = 1;
        }

        _lifetime.Cancel();
        _subscription?.Dispose();
        _lifetime.Dispose();
    }
}
