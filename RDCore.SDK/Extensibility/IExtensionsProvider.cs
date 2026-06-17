using RDCore.SDK.Extensibility;

namespace RDCore.LanguageServer.Extensibility;

/// <summary>
/// A service that can discover and manage platform extensions.
/// </summary>
public interface IExtensionsProvider
{
    /// <summary>
    /// Scans the <em>extensions folder</em> for subfolders containing an <em>extension manifest</em>.
    /// </summary>
    IEnumerable<ExtensionInfo> Discover();
    /// <summary>
    /// Asynchronously starts and attempts to connect with the <em>server process</em> for the specified <see cref="ExtensionInfo"/>.
    /// </summary>
    /// <param name="extension">The deserialized <em>manifest</em> of the extension to start.</param>
    /// <returns>An asynchronous <see cref="Task"/> that completes when the specified extension was successfully started and connected, or failed to do so.</returns>
    Task StartAsync(ExtensionInfo extension);
    /// <summary>
    /// Enables the specified <see cref="ExtensionInfo"/> if the manifest and associated executable pass validation.
    /// </summary>
    /// <param name="extension">The deserialized <em>manifest</em> of the extension to enable.</param>
    /// <returns><c>true</c> if the specified extension <strong>passed validation</strong> and was enabled, <c>false</c> otherwise.</returns>
    bool Allow(ExtensionInfo extension);
    /// <summary>
    /// Blocks loading the specified <see cref="ExtensionInfo"/>.
    /// </summary>
    /// <param name="extension">The deserialized <em>manifest</em> of the extension to block.</param>
    /// <returns><c>true</c> if the specified extension could be blocked, <c>false</c> otherwise.</returns>
    bool Block(ExtensionInfo extension);
}
