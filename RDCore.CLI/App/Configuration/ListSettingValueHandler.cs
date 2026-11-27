using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;

namespace RDCore.CLI.App.Configuration;

/// <summary>
/// Handles <c>rdcore/settings/listValue</c>, which the language server sends for the code action that disables a diagnostic: it asks the shell to add an item
/// to the list of a setting, or to remove one from it. The shell changes the list as <c>CONFIG</c> would, and says its settings changed.
/// </summary>
/// <remarks>
/// A list of the user's settings is changed in the user's file; a list of a workspace's settings, in the file of the shell's workspace
/// (<see cref="WorkspaceSettingsStore"/>), whose documents are the only ones the shell has.
/// </remarks>
/// <param name="settings">The settings the user can set.</param>
/// <param name="liveSettings">The shell's own live settings.</param>
/// <param name="notifier">Tells the language server the live settings changed.</param>
/// <param name="options">The shell's settings, which name its workspace.</param>
internal sealed class ListSettingValueHandler(
    IPlatformSettings settings, ILiveSettings liveSettings, LiveSettingsNotifier notifier, IOptions<SdkAppOptions> options)
    : IJsonRpcRequestHandler<ListSettingValueRequest, ListSettingValueResult>
{
    /// <inheritdoc/>
    public async Task<ListSettingValueResult> Handle(ListSettingValueRequest request, CancellationToken cancellationToken)
    {
        var change = request.Scope switch
        {
            SettingScope.Platform => request.Remove ? settings.Store.Remove(request.Key, request.Value) : settings.Store.Add(request.Key, request.Value),
            SettingScope.Workspace when WorkspaceSettingsStore.FolderOf(options.Value.Workspace.WorkspaceUri) is { } folder => request.Remove
                ? settings.Workspace.Remove(new Uri(folder), request.Key, request.Value)
                : settings.Workspace.Add(new Uri(folder), request.Key, request.Value),
            _ => null,
        };

        // the language server reads the workspace's list again when it is told the settings changed, as it reads the user's.
        if (change is { IsSet: true })
        {
            liveSettings.Refresh();
            await notifier.Notify(cancellationToken);
        }

        return new ListSettingValueResult { Applied = change is { IsSet: true }, Reason = ReasonOf(change, request) };
    }

    private const string ExtensionsAllowedKey = $"{nameof(SdkAppOptions.Platform)}.{nameof(SdkPlatformOptions.Extensions)}.{nameof(ExtensionsOptions.Allowed)}";

    // an item the user did not add, said in the user's language: an extension's is one they can block instead.
    private static string? ReasonOf(UserSettingChange? change, ListSettingValueRequest request)
        => change is { Problem: UserSettingProblem.NotTheUsers, Setting: { } setting }
            ? string.Format(
                setting.Key.Equals(ExtensionsAllowedKey, StringComparison.OrdinalIgnoreCase)
                    ? Resources.Config_NotTheUsers_Extension
                    : Resources.Config_NotTheUsers,
                request.Value, setting.Key)
            : null;
}
