using RDCore.External.Automation;
using RDCore.External.Interaction;
using RDCore.External.Native;
using RDCore.External.Settings;

namespace RDCore.External;

/// <summary>
/// What a runtime reaches outside the platform: the objects of automation servers, the functions of native libraries, the person running the program, the desktop it
/// runs on, and the settings it keeps.
/// </summary>
/// <remarks>
/// The runtime does not know the platform it runs on, nor which process its calls are made in: its host says, by composing it with one of these. A runtime that
/// is given none reaches nothing (<see cref="None"/>): its dialogs answer themselves, its desktop does nothing, and its settings keep nothing - which is what an
/// analysis runs with.
/// </remarks>
/// <param name="Automation">What reaches the automation servers that the objects of a referenced library are held by.</param>
/// <param name="Libraries">What calls the functions of the native libraries that <c>Declare</c> statements name.</param>
public sealed record class ExternalWorld(IAutomationServer Automation, INativeLibraryHost Libraries)
{
    /// <summary>
    /// Who answers the dialogs of <c>MsgBox</c> and <c>InputBox</c>. <see cref="NoUserInteraction"/> unless the host says otherwise.
    /// </summary>
    public IUserInteraction Interaction { get; init; } = NoUserInteraction.Instance;

    /// <summary>
    /// The title of a dialog the program gives none: the name of the workspace project, as the host says it. <c>"RDCore"</c> unless the host says otherwise.
    /// </summary>
    public string DialogTitle { get; init; } = DefaultDialogTitle;

    /// <summary>
    /// The title of a dialog when neither the program nor the host gives one.
    /// </summary>
    public const string DefaultDialogTitle = "RDCore";

    /// <summary>
    /// The desktop of <c>DoEvents</c>, <c>Beep</c>, <c>AppActivate</c>, <c>SendKeys</c> and <c>Shell</c>. <see cref="NoDesktop"/> unless the host says otherwise.
    /// </summary>
    public IDesktop Desktop { get; init; } = NoDesktop.Instance;

    /// <summary>
    /// Where <c>SaveSetting</c>, <c>GetSetting</c>, <c>GetAllSettings</c> and <c>DeleteSetting</c> keep their settings. <see cref="NoSettings"/> unless the host
    /// says otherwise.
    /// </summary>
    public IProgramSettings ProgramSettings { get; init; } = NoSettings.Instance;

    /// <summary>
    /// The workspace application settings of <c>GetJsonSetting</c>, <c>GetAllJsonSettings</c> and <c>SaveJsonSetting</c>. <see cref="NoSettings"/> unless the host
    /// says otherwise.
    /// </summary>
    public IWorkspaceSettings WorkspaceSettings { get; init; } = NoSettings.Instance;

    /// <summary>
    /// What <c>Command</c> returns: the arguments of the command that started the program (<strong>MS-VBAL §6.1.2.8.1.3</strong>, "implementation dependent").
    /// Empty unless the host says otherwise, as it is in a program an Office application runs.
    /// </summary>
    public string CommandLine { get; init; } = string.Empty;

    /// <summary>
    /// The outside world of a runtime that reaches none of it.
    /// </summary>
    public static ExternalWorld None { get; } = new(UnavailableAutomationServer.Instance, UnavailableNativeLibraryHost.Instance);
}
