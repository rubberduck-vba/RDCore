using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;

namespace RDCore.CLI.App.Configuration;

/// <summary>
/// Handles <c>rdcore/settings/listValue</c>, which the language server sends for the code action that disables a diagnostic: it asks the shell to add an item
/// to the list of a setting, or to remove one from it. The shell's settings are the user's: it changes the list as <c>CONFIG</c> would, and says its settings changed.
/// </summary>
/// <remarks>
/// The shell keeps the user's settings of the platform. A list of a workspace's settings is not one it keeps: it answers that it did not apply the change.
/// </remarks>
/// <param name="settings">The settings the user can set.</param>
/// <param name="liveSettings">The shell's own live settings.</param>
/// <param name="notifier">Tells the language server the live settings changed.</param>
internal sealed class ListSettingValueHandler(IPlatformSettings settings, ILiveSettings liveSettings, LiveSettingsNotifier notifier)
    : IJsonRpcRequestHandler<ListSettingValueRequest, ListSettingValueResult>
{
    /// <inheritdoc/>
    public async Task<ListSettingValueResult> Handle(ListSettingValueRequest request, CancellationToken cancellationToken)
    {
        if (request.Scope != SettingScope.Platform)
        {
            return new ListSettingValueResult { Applied = false };
        }

        var change = request.Remove ? settings.Store.Remove(request.Key, request.Value) : settings.Store.Add(request.Key, request.Value);
        if (change.IsSet)
        {
            liveSettings.Refresh();
            await notifier.Notify(cancellationToken);
        }

        return new ListSettingValueResult { Applied = change.IsSet };
    }
}
