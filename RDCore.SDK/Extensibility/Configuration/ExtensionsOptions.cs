namespace RDCore.SDK.Extensibility.Configuration;

/// <summary>
/// Platform-level extension settings, bound from <c>appsettings.json</c>.
/// </summary>
public record class ExtensionsOptions
{
    private const string _defaultExtensionsLocation = "../extensions";
    private const string _defaultExtensionManifestName = "extension.manifest.json";

    /// <summary>
    /// The location RDCore extensions are discovered from. Plugins/extensions are identified by a folder containing a RDCore extension manifest.<br/>
    /// </summary>
    public string Path { get; set; } = _defaultExtensionsLocation;
    /// <summary>
    /// The name of the RDCore extension manifest file.
    /// </summary>
    /// <remarks>
    /// ⚠️ This value is constant.
    /// </remarks>
    public string Manifest { get; } = _defaultExtensionManifestName;
    /// <summary>
    /// A list of allowed extension titles.
    /// </summary>
    /// <remarks>
    /// 👉 RDCore will only load extensions present in this list.
    /// </remarks>
    public string[] Allowed { get; set; } = ["RDCore.Diagnostics"];
    /// <summary>
    /// A list of blocked extension titles.
    /// </summary>
    /// <remarks>
    /// 👉 RDCore may block a repeatedly problematic extension from being loaded automatically.
    /// </remarks>
    public string[] Blocked { get; set; } = [];
}
