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
/// The settings are set once, as a whole: whoever listens for the change (<see cref="ILiveSettings.Changed"/>, <paramref name="changed"/>) sees the indentation it ends up with, never
/// the one before the editor's tab size was known. A change that arrives while the editor is being asked is the last word: the older one is not set.
/// </remarks>
/// <param name="server">The language server, whose configuration the client answers.</param>
/// <param name="liveSettings">The component's live settings.</param>
/// <param name="formatting">The component's formatting settings, which the live settings are set on.</param>
/// <param name="changed">Called once the settings were set.</param>
/// <param name="log">Says what happened.</param>
public sealed class ClientConfigurationFollower(ILanguageServer server, ILiveSettings liveSettings, SdkFormattingOptions formatting, Func<Task> changed, Action<string> log) : IDisposable
{
    private IDisposable? _subscription;
    private int _version;

    /// <summary>
    /// Starts following: the client's answers set the settings from now on.
    /// </summary>
    public void Start() => _subscription ??= ChangeToken.OnChange(server.Configuration.GetReloadToken, () => _ = FollowAsync());

    /// <summary>
    /// Sets the live settings the client said, with the editor's tab size for an indentation the user never set.
    /// </summary>
    /// <returns>Whether the settings were set: not when a newer change came in while the editor's tab size was asked.</returns>
    public async Task<bool> FollowAsync()
    {
        var version = Interlocked.Increment(ref _version);
        try
        {
            var said = LiveSettingsProtocol.Read(server.Configuration);
            var tabSize = await FormattingTabSize.RequestEditorTabSizeAsync(server);
            if (version != Volatile.Read(ref _version))
            {
                return false;
            }

            var settings = FormattingTabSize.WithEditorTabSize(said, tabSize);
            liveSettings.Apply(settings);
            log($"⚙️ The client's live settings were applied ({said.Count} value(s)){(settings.Count > said.Count ? $"; the indentation is the client's tab size ({formatting.IndentSpaces}), which no setting overrides" : string.Empty)}.");
            await changed();
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log($"The client's live settings could not be applied: {exception.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _subscription?.Dispose();
}
