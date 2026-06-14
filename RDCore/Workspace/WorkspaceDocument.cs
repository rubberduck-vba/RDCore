using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Symbols.Unbound;

namespace RDCore.LanguageServer.Workspace;

internal record class WorkspaceDocument
{
    public WorkspaceDocument(string relativePath, string workspaceRoot, int version = 1) : this(relativePath, workspaceRoot, string.Empty, version) { }
    public WorkspaceDocument(string relativePath, string workspaceRoot, string content, int version = 1)
    {
        Id = new TextDocumentIdentifier(Path.Combine(workspaceRoot, relativePath));
        Version = version;
        Text = content;

        WorkspaceRoot = workspaceRoot;
        RelativePath = relativePath;
    }

    public string WorkspaceRoot { get; }
    public string RelativePath { get; }

    /// <summary>
    /// A unique identifier for the document, represented as a URI.
    /// </summary>
    /// <remarks>
    /// The URI is expected to use the "file" scheme and contain an absolute file path to the document on disk.
    /// </remarks>
    public TextDocumentIdentifier Id { get; }
    /// <summary>
    /// Gets the name of the file, without its extension.
    /// </summary>
    public string Name => Path.GetFileNameWithoutExtension(Id.Uri.GetFileSystemPath());
    /// <summary>
    /// Gets the name of the file, including its extension.
    /// </summary>
    public string FileName => $"{Name}{Extension}";
    /// <summary>
    /// Gets the relative path of the file with respect to the workspace root directory.
    /// </summary>
    public string Folder => Path.GetDirectoryName(Id.Uri.GetFileSystemPath()) ?? string.Empty;
    /// <summary>
    /// Gets the file extension.
    /// </summary>
    public string Extension => Path.GetExtension(Id.Uri.GetFileSystemPath()).ToLowerInvariant();
    /// <summary>
    /// Gets the version of the file, which is incremented each time the file is modified.
    /// </summary>
    /// <remarks>
    /// Version resets to 1 when the file is saved. The version is incremented each time the file is modified, regardless of whether the file is currently opened in the editor. 
    /// The version is not persisted to disk and only exists in memory while the workspace is open. 
    /// The version is used to determine whether the file has been modified since it was last saved, and to ensure that edits are applied to the correct version of the file when it is opened in the editor.
    /// </remarks>
    public int Version { get; init; }

    public bool IsDirty => Version > 1;

    /// <summary>
    /// Gets the text content of the document.
    /// </summary>
    /// <remarks>
    /// The text content is only available when the document is in the <c>Loaded</c> or <c>Opened</c> state, and is empty otherwise.
    /// </remarks>
    public string Text { get; init; }

    /// <summary>
    /// Creates and returns a new WorkspaceDocument instance with the specified text and an incremented version number.
    /// </summary>
    public WorkspaceDocument WithText(string text) => this with { Text = text, Version = Version + 1 };
    /// <summary>
    /// Creates and returns a new WorkspaceDocument instance with the version number reset to 1, while keeping the same text content.
    /// </summary>
    public WorkspaceDocument AsInitialVersion() => this with { Version = 1 };
}
