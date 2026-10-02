using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.LanguageServer.Workspace;

/// <summary>
/// A document the language server tracks: its text, and the version of it the client and the server agree on.
/// </summary>
/// <remarks>
/// While a client has the document open, the text is the client's (<strong>LSP 3.17</strong> §Text Document Synchronization): the server has no say in what it
/// says, and the <see cref="Version"/> is the client's own, which only goes up. That the text is not what is on disk is a different fact,
/// <see cref="IsDirty"/>, and the version says nothing of it.
/// </remarks>
internal record class WorkspaceDocument
{
    public WorkspaceDocument(string relativePath, string workspaceRoot, int version = 1) : this(relativePath, workspaceRoot, string.Empty, version) { }
    public WorkspaceDocument(string relativePath, string workspaceRoot, string content, int version = 1)
    {
        // a path that is absolute is the document's own: a document the client opens need not be in the workspace folder.
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
    /// The version of the text: the one the client gave it while it has the document open, and which only goes up; a count of the edits the server made to it
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// Whatever is derived from the text - a parse, the symbols and the model of the host, the diagnostics - is of one version of it, and is only as good as that
    /// version is the current one.
    /// </remarks>
    public int Version { get; init; }

    /// <summary>
    /// Whether the text has changes that are not saved: it is not what is on disk.
    /// </summary>
    public bool IsDirty { get; init; }

    /// <summary>
    /// Gets the text content of the document.
    /// </summary>
    /// <remarks>
    /// The text content is only available when the document is in the <c>Loaded</c> or <c>Opened</c> state, and is empty otherwise.
    /// </remarks>
    public string Text { get; init; }

    /// <summary>
    /// The document with the specified text, an edit the server made: one version later, and with changes that are not saved.
    /// </summary>
    public WorkspaceDocument WithText(string text) => this with { Text = text, Version = Version + 1, IsDirty = true };

    /// <summary>
    /// The document with the specified text, as the client has it at the specified version: with changes that are not saved.
    /// </summary>
    /// <param name="text">The text the client has.</param>
    /// <param name="version">The version the client gives it.</param>
    public WorkspaceDocument WithText(string text, int version) => this with { Text = text, Version = version, IsDirty = true };

    /// <summary>
    /// The document with changes that are saved: what is on disk. The version is the same, for the text is.
    /// </summary>
    public WorkspaceDocument AsSaved() => this with { IsDirty = false };
}
