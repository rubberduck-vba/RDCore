namespace RDCore.LanguageServer.Extensibility;

/// <summary>
/// A service that can discover and manage platform extensions.
/// </summary>
public interface IExtensionsProvider
{
    /// <summary>
    /// Scans the <em>extensions folder</em> for subfolders containing an <em>extension manifest</em>.
    /// </summary>
    IEnumerable<ExtensionInfo> DiscoverExtensions();
}
