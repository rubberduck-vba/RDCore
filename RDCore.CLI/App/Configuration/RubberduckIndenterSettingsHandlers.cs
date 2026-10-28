using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Formatting;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;

namespace RDCore.CLI.App.Configuration;

/// <summary>
/// Handles <c>rdcore/rubberduck-indenter-settings</c>: the language server asks what the user had of Rubberduck's indenter settings, and the shell reads them where
/// Rubberduck keeps them (<see cref="RubberduckIndenterSettingsReader"/>) and says them as they are - on Windows; elsewhere it found none.
/// </summary>
/// <param name="reader">Reads Rubberduck's settings.</param>
internal sealed class RubberduckIndenterSettingsHandler(RubberduckIndenterSettingsReader reader) : IJsonRpcRequestHandler<RubberduckIndenterSettingsParams, RubberduckIndenterSettingsResult>
{
    /// <inheritdoc/>
    public Task<RubberduckIndenterSettingsResult> Handle(RubberduckIndenterSettingsParams request, CancellationToken cancellationToken)
        => Task.FromResult(reader.Read());
}

/// <summary>
/// What the shell says to the language server when its settings changed: that they did, so that it asks for them (<see cref="ILiveSettings"/>).
/// </summary>
/// <param name="Notify">Says it.</param>
internal sealed record class LiveSettingsNotifier(Func<CancellationToken, Task> Notify);

/// <summary>
/// Handles <c>rdcore/rubberduck-indenter-settings/apply</c>: the language server mapped the indenter settings the shell read to <c>Formatting</c> settings, and asks the shell
/// to take them as its own - the shell's settings are the user's: it sets them as <c>CONFIG</c> does, and says its settings changed.
/// </summary>
/// <param name="settings">The settings the user can set.</param>
/// <param name="liveSettings">The shell's own live settings.</param>
/// <param name="notifier">Tells the language server the live settings changed.</param>
internal sealed class RubberduckIndenterSettingsApplyHandler(IPlatformSettings settings, ILiveSettings liveSettings, LiveSettingsNotifier notifier)
    : IJsonRpcRequestHandler<RubberduckIndenterSettingsApplyParams, RubberduckIndenterSettingsApplyResult>
{
    /// <inheritdoc/>
    public async Task<RubberduckIndenterSettingsApplyResult> Handle(RubberduckIndenterSettingsApplyParams request, CancellationToken cancellationToken)
    {
        // the server asks for the settings of an import and for no others: a client owns its configuration, and this is no channel to write any of it.
        var accepted = request.Settings.Where(setting => IndenterSettingsImport.Imports(setting.Key)).ToDictionary(setting => setting.Key, setting => setting.Value);
        var applied = IndenterSettingsImport.ApplyTo(settings.Store, accepted);
        if (applied > 0)
        {
            liveSettings.Refresh();
            await notifier.Notify(cancellationToken);
        }

        return new RubberduckIndenterSettingsApplyResult { Applied = applied };
    }
}
