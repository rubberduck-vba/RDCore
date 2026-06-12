namespace RDCore.SDK.Extensibility.Configuration;

/// <summary>
/// Configuration settings, bound from <c>appsettings.json</c> or overridden from command-line arguments.
/// </summary>
public record class SdkAppOptions
{
    /// <summary>
    /// LSP Server options.
    /// </summary>
    /// <remarks>
    /// Most of these settings may be overridden through command-line arguments.
    /// </remarks>
    public SdkServerOptions Server { get; set; } = new();
    /// <summary>
    /// Workspace options.
    /// </summary>
    public SdkWorkspaceOptions Workspace { get; set; } = new();
    /// <summary>
    /// Platform options.
    /// </summary>
    public SdkPlatformOptions Platform { get; set; } = new();
}
