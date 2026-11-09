using RDCore.External.Interaction;
using RDCore.External.Settings;
using RDCore.External.Windows.Interaction;
using RDCore.External.Windows.Settings;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.CLI.Host;

/// <summary>
/// What the machine this host runs on has of the outside world a program reaches without a server or a library: its dialogs, its desktop, and its registry.
/// </summary>
/// <remarks>
/// Windows has all three, and they are its own. Anywhere else there is nobody to show a dialog to (<see cref="NoUserInteraction"/>), no desktop to act on
/// (<see cref="NoDesktop"/>), and no registry: the legacy settings are the workspace's, whatever the environment asks for.
/// </remarks>
internal static class MachinePlatform
{
    /// <summary>
    /// The dialogs of this machine: Windows' own, or none.
    /// </summary>
    public static IUserInteraction Interaction { get; } = OperatingSystem.IsWindows() ? new WindowsUserInteraction() : NoUserInteraction.Instance;

    /// <summary>
    /// The desktop of this machine: Windows', or none.
    /// </summary>
    public static IDesktop Desktop { get; } = OperatingSystem.IsWindows() ? new WindowsDesktop() : NoDesktop.Instance;

    /// <summary>
    /// Where the legacy settings members keep their settings: the registry when the environment asks for it and this is Windows, and the workspace application settings
    /// otherwise (<strong>RD-VBAL §2.1.1</strong>).
    /// </summary>
    /// <param name="environment">The environment, which says whether the registry is asked for.</param>
    /// <param name="workspace">The workspace application settings.</param>
    public static IProgramSettings ProgramSettings(IRuntimeEnvironmentProfile environment, IWorkspaceSettings workspace)
        => environment.LegacyRegistrySettings && OperatingSystem.IsWindows() ? new RegistryProgramSettings() : new JsonProgramSettings(workspace);
}
