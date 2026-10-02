namespace RDCore.CLI.App.Repl;

/// <summary>
/// The program buffer as a document of the language server: the <c>.rdc</c> file that was loaded or saved, open for as long as the program is that file's.
/// </summary>
/// <remarks>
/// An editor opens a document, tells the server of every edit, and closes it (<strong>LSP 3.17</strong> §Text Document Synchronization); the shell does the same
/// for the program it holds, which is what makes the server's view of it - its parse, its diagnostics, the host's symbols - the one of the text in the
/// buffer rather than of whatever is on disk. The versions are the buffer's own (<see cref="ReplProgram.Version"/>): the shell is the client, and the client
/// numbers them.
/// <para>
/// A program that was neither loaded nor saved is not a document. It is lines at a prompt, and what is asked of the server about them is asked with their
/// text, as the fragment it is.
/// </para>
/// </remarks>
public sealed class ReplDocument(ReplProgram program, IReplPlatformClient platform, ReplWorkspace? scratchWorkspace = null)
{
    private int _sentVersion;

    /// <summary>The URI of the document, or <see langword="null"/> when the program is not one.</summary>
    public Uri? Uri { get; private set; }

    /// <summary>Whether the program is a document, open in the language server.</summary>
    public bool IsOpen => Uri is not null;

    /// <summary>
    /// Whether the document is the one the shell opens for a program that is no file's, which is in the shell's scratch workspace and which a <c>SAVE</c> with
    /// no name is therefore not a save of.
    /// </summary>
    public bool IsScratch { get; private set; }

    /// <summary>
    /// Opens the program as a document in the shell's scratch workspace, unless it is one already: what a listing of the program is highlighted by, which the
    /// language server tells of a document.
    /// </summary>
    /// <param name="token">A token that cancels the notification.</param>
    /// <returns><see langword="false"/> when the program is not a document and cannot be: the shell is attached to a workspace of its own, and has no scratch one.</returns>
    public async Task<bool> EnsureOpenAsync(CancellationToken token)
    {
        if (IsOpen)
        {
            await SynchronizeAsync(token);
            return true;
        }

        if (scratchWorkspace is null)
        {
            return false;
        }

        await OpenAsync(scratchWorkspace.ListingPath, token);
        IsScratch = true;
        return true;
    }

    /// <summary>
    /// Opens the program as the document at <paramref name="path"/>; the document that was open, if there was one, is closed.
    /// </summary>
    /// <param name="path">The absolute path of the <c>.rdc</c> file.</param>
    /// <param name="token">A token that cancels the notification.</param>
    public async Task OpenAsync(string path, CancellationToken token)
    {
        await CloseAsync(token);

        IsScratch = false;
        var uri = new Uri(path);
        await platform.OpenDocumentAsync(uri, program.ToSourceText(), program.Version, token);
        _sentVersion = program.Version;
        Uri = uri;
    }

    /// <summary>
    /// Tells the language server of what was edited since it was last told, which is nothing when nothing was.
    /// </summary>
    /// <param name="token">A token that cancels the notification.</param>
    public async Task SynchronizeAsync(CancellationToken token)
    {
        if (Uri is null || _sentVersion == program.Version)
        {
            return;
        }

        await platform.ChangeDocumentAsync(Uri, program.Version, program.ToSourceText(), token);
        _sentVersion = program.Version;
    }

    /// <summary>
    /// Tells the language server that the document is about to be saved, once it has the text that is.
    /// </summary>
    /// <param name="token">A token that cancels the requests.</param>
    /// <returns>The text to write.</returns>
    public async Task<string> WillSaveAsync(CancellationToken token)
    {
        await SynchronizeAsync(token);
        if (Uri is not null)
        {
            // TODO(willSaveWaitUntil): the server answers with the edits it would make before the save; it has none today, and the first one it has is applied here.
            await platform.WillSaveDocumentAsync(Uri, token);
        }

        return program.ToSourceText();
    }

    /// <summary>
    /// Tells the language server that the document was saved.
    /// </summary>
    /// <param name="text">The text that was written.</param>
    /// <param name="token">A token that cancels the notification.</param>
    public async Task SavedAsync(string text, CancellationToken token)
    {
        if (Uri is not null)
        {
            await platform.SaveDocumentAsync(Uri, text, token);
        }
    }

    /// <summary>
    /// Closes the document, when the program is one; what the program is from then on is lines at a prompt.
    /// </summary>
    /// <param name="token">A token that cancels the notification.</param>
    public async Task CloseAsync(CancellationToken token)
    {
        if (Uri is not { } uri)
        {
            return;
        }

        Uri = null;
        IsScratch = false;
        await platform.CloseDocumentAsync(uri, token);
    }
}
