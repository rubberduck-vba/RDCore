namespace RDCore.LanguageServer.Workspace.States;

public enum DocumentStateValue
{
    /// <summary>
    /// File belongs to an open workspace but was not loaded, or was manually unloaded.
    /// </summary>
    Unloaded,
    /// <summary>
    /// File belongs to an open workspace and is correctly loaded, but not opened in the editor.
    /// </summary>
    Loaded,
    /// <summary>
    /// File belongs to an open workspace but could not be found in the workspace folder.
    /// </summary>
    Missing,
    /// <summary>
    /// File belongs to an open workspace and exists in the workspace folder, but could not be loaded.
    /// </summary>
    LoadError,
    /// <summary>
    /// File belongs to an open workspace and is currently opened in the editor.
    /// </summary>
    Opened,
}
