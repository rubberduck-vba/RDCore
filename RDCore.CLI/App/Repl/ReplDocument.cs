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
/// <para>
/// The server is told from more than the shell's thread - what is asked of it once the program is left alone runs on its own - and is told one thing at a time,
/// in order: a version and the text that is that version (<see cref="ReplProgram.Snapshot"/>), never a version read again after the text was sent.
/// </para>
/// </remarks>
public sealed class ReplDocument(ReplProgram program, IReplPlatformClient platform, ReplWorkspace? scratchWorkspace = null)
{
    // the version the server was last told the text of.
    private int _sentVersion;

    // one notification at a time: an open, a change, a save or a close waits for the one before it, and a change sends the program as it is by then.
    private readonly SemaphoreSlim _telling = new(1, 1);

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
        await _telling.WaitAsync(token);
        try
        {
            if (IsOpen)
            {
                await SynchronizeTellingAsync(token);
                return true;
            }

            if (scratchWorkspace is null)
            {
                return false;
            }

            await OpenTellingAsync(scratchWorkspace.ListingPath, token);
            IsScratch = true;
            return true;
        }
        finally
        {
            _telling.Release();
        }
    }

    /// <summary>
    /// Opens the program as the document at <paramref name="path"/>; the document that was open, if there was one, is closed.
    /// </summary>
    /// <param name="path">The absolute path of the <c>.rdc</c> file.</param>
    /// <param name="token">A token that cancels the notification.</param>
    public async Task OpenAsync(string path, CancellationToken token)
    {
        await _telling.WaitAsync(token);
        try
        {
            await OpenTellingAsync(path, token);
        }
        finally
        {
            _telling.Release();
        }
    }

    private async Task OpenTellingAsync(string path, CancellationToken token)
    {
        await CloseTellingAsync(token);

        IsScratch = false;
        var uri = new Uri(path);
        var (version, text) = program.Snapshot();
        await platform.OpenDocumentAsync(uri, text, version, token);
        _sentVersion = version;
        Uri = uri;
    }

    /// <summary>
    /// Tells the language server of what was edited since it was last told, which is nothing when nothing was.
    /// </summary>
    /// <param name="token">A token that cancels the notification.</param>
    public async Task SynchronizeAsync(CancellationToken token)
    {
        await _telling.WaitAsync(token);
        try
        {
            await SynchronizeTellingAsync(token);
        }
        finally
        {
            _telling.Release();
        }
    }

    // the program's text as the server holds it once this is done.
    private async Task<string> SynchronizeTellingAsync(CancellationToken token)
    {
        // the version is the text's: a line stored while the server is told is the next version's, which the next change sends.
        var (version, text) = program.Snapshot();
        if (Uri is not { } uri || version == _sentVersion)
        {
            return text;
        }

        await platform.ChangeDocumentAsync(uri, version, text, token);
        _sentVersion = version;
        return text;
    }

    /// <summary>
    /// Tells the language server that the document is about to be saved, once it has the text that is.
    /// </summary>
    /// <param name="token">A token that cancels the requests.</param>
    /// <returns>The text to write.</returns>
    public async Task<string> WillSaveAsync(CancellationToken token)
    {
        await _telling.WaitAsync(token);
        try
        {
            // the text that is saved is the one the server was told of last, in the same turn: a line stored meanwhile is the next change's.
            var text = await SynchronizeTellingAsync(token);
            if (Uri is not null)
            {
                // TODO(willSaveWaitUntil): the server answers with the edits it would make before the save; it has none today, and the first one it has is applied here.
                await platform.WillSaveDocumentAsync(Uri, token);
            }

            return text;
        }
        finally
        {
            _telling.Release();
        }
    }

    /// <summary>
    /// Tells the language server that the document was saved.
    /// </summary>
    /// <param name="text">The text that was written.</param>
    /// <param name="token">A token that cancels the notification.</param>
    public async Task SavedAsync(string text, CancellationToken token)
    {
        await _telling.WaitAsync(token);
        try
        {
            if (Uri is not null)
            {
                await platform.SaveDocumentAsync(Uri, text, token);
            }
        }
        finally
        {
            _telling.Release();
        }
    }

    /// <summary>
    /// Closes the document, when the program is one; what the program is from then on is lines at a prompt.
    /// </summary>
    /// <param name="token">A token that cancels the notification.</param>
    public async Task CloseAsync(CancellationToken token)
    {
        await _telling.WaitAsync(token);
        try
        {
            await CloseTellingAsync(token);
        }
        finally
        {
            _telling.Release();
        }
    }

    private async Task CloseTellingAsync(CancellationToken token)
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
