namespace RDCore.SDK.Extensibility.Configuration;

/// <summary>
/// Workspace-level configuration settings, bound from <c>appsettings.json</c> or overridden from command-line arguments.
/// </summary>
public record class SdkWorkspaceOptions
{
    private const string _defaultLocation = "%USERAPPDATA%/RDCore/Workspaces";
    private const string _defaultWorkspaceUri = "/Project1";

    /// <summary>
    /// The default location of RDCore workspaces (source files) on disk.
    /// </summary>
    /// <remarks>
    /// 👉 This location is used as a root when the <c>WorkspaceUri</c> is <strong>relative</strong>.
    /// </remarks>
    public string DefaultLocation { get; set; } = _defaultLocation;
    /// <summary>
    /// The location of the workspace to work with if none is supplied.
    /// </summary>
    /// <remarks>
    /// This setting is for <strong>client applications</strong>. The workspace location a LSP server application should work with should be supplied by a command-line argument <em>or</em> as a parameter in the <c>Initialize</c> request.<br/>
    /// ⚠️ If a workspace <c>Uri</c> is not supplied, a server application should exit with an error code.
    /// </remarks>
    public string WorkspaceUri { get; set; } = _defaultWorkspaceUri;
}
