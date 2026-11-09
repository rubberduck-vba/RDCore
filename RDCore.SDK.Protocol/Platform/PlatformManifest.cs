namespace RDCore.SDK.Platform;

/// <summary>
/// The platform manifest, <c>rdcore.json</c> at the platform root: where each component of a deployed platform is.
/// </summary>
/// <remarks>
/// Written when the platform is deployed (<c>PlatformPublish.ps1</c>), in <c>camelCase</c>, and read regardless of case. The paths are relative to the platform root, with
/// either slash style.
/// </remarks>
public record class PlatformManifest
{
    /// <summary>The name of the manifest file at the platform root.</summary>
    public const string FileName = "rdcore.json";

    /// <summary>The version of the deployed platform.</summary>
    public string PlatformVersion { get; init; } = string.Empty;

    /// <summary>When the manifest was written, in UTC.</summary>
    public DateTime GeneratedUtc { get; init; } = default;

    /// <summary>The folder of the platform's extensions: one subfolder each, named for the extension.</summary>
    public string ExtensionsDirectory { get; init; } = string.Empty;

    /// <summary>The executable of the environment host: <c>rdc.exe</c>, which runs in host mode for it.</summary>
    public string HostService { get; init; } = string.Empty;

    /// <summary>The executable of the language server, which a client starts and connects to.</summary>
    public string LangService { get; init; } = string.Empty;

    /// <summary>The executable of the parse server.</summary>
    public string ParseServer { get; init; } = string.Empty;

    /// <summary>
    /// The extension the platform bundles: the name of its folder under <see cref="ExtensionsDirectory"/>.
    /// </summary>
    /// <remarks>
    /// The core extension reports the platform's first-party diagnostics. The prefixes of their codes (<c>RDC</c>, <c>RDX</c>) are its own: no other extension
    /// that declares one is started (<strong>RD-VBAL §1.1.4</strong>). A manifest that does not name it bundles <see cref="DefaultCoreExtension"/>.
    /// </remarks>
    public string CoreExtension { get; init; } = DefaultCoreExtension;

    /// <summary>
    /// The extension a platform bundles when its manifest does not say: <c>RDCore.Diagnostics</c>.
    /// </summary>
    public const string DefaultCoreExtension = "RDCore.Diagnostics";
}
