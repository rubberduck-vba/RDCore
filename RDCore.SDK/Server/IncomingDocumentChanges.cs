using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Server;

/// <summary>
/// The changes to documents that the client has sent and the server has not applied yet: a request about a document one of them changes is answered
/// <c>ContentModified</c> (-32801) at once, instead of keeping the change, and every request the client sends after it, waiting until it ends.
/// </summary>
/// <remarks>
/// <para>
/// OmniSharp runs a notification that changes a document (<c>textDocument/didOpen</c>, <c>didChange</c>, <c>didClose</c>) in order, after the requests already
/// running, which it runs in parallel. An RDCore server answers every request (<see cref="ServerRequestScheduling.WithEveryRequestAnswered"/>), so a keystroke
/// that arrives while the document's diagnostics are being pulled waits for the pull, and so does the completion the client sends after it: on a module of
/// 2,100 lines, about five seconds (HexIDE's findings desk #31).
/// </para>
/// <para>
/// The server knows of a change as it arrives (<see cref="Tap"/>), long before its turn comes. A request made of what the document was says so with
/// <see cref="WhileCurrentAsync{T}"/>: when a change to its document arrives while it runs, or has arrived and is not applied yet when it starts, the request
/// stops and is answered <c>ContentModified</c>, which LSP 3.17 §Response Message has a server answer "if it detects a content change in its unprocessed
/// messages". The client asks again for the document as it is now. A change to another document changes nothing for it.
/// </para>
/// </remarks>
public sealed class IncomingDocumentChanges
{
    // what the error answers: the protocol's own words for it (LSP 3.17 §Response Message), which a client may show, but more likely asks again.
    private const string ContentModifiedMessage = "Content Modified";

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _unapplied = new(StringComparer.OrdinalIgnoreCase);

    // what each running request is made of: one document, or every document of the workspace (null).
    private readonly List<(string? Document, CancellationTokenSource Changed)> _watches = [];

    // the documents a handler applies a change to: every one, until the server says which.
    private volatile Func<Uri, bool> _serves = static _ => true;

    /// <summary>
    /// Says which documents the server's handlers apply a change to (<see cref="Applied"/>). A change to any other is no change of the server's: OmniSharp hands
    /// it to no handler, so it is never applied, and is not waited for.
    /// </summary>
    /// <param name="serves">Whether a handler applies a change to a document.</param>
    public void Serve(Func<Uri, bool> serves) => _serves = serves;

    /// <summary>
    /// Says that the server's handlers apply a change to the documents a selector matches, which is what they are registered for: the client names a
    /// document's language its own way, so a filter of the selector is matched by the document's path alone.
    /// </summary>
    /// <param name="selector">What the handlers of a document's notifications are registered for.</param>
    public void Serve(TextDocumentSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Serve(document => selector.IsMatch(new TextDocumentAttributes(document, string.Empty)));
    }

    /// <summary>
    /// A change to a document has arrived from the client, and is waiting for its turn: the requests about the document that are running are answered
    /// <c>ContentModified</c>, and so is each that starts before the change is applied. A change to a document the server does not serve
    /// (<see cref="Serve"/>) is nothing.
    /// </summary>
    /// <param name="document">The document the change is to.</param>
    public void Arrived(Uri document)
    {
        if (!_serves(document))
        {
            return;
        }

        var key = Key(document);
        CancellationTokenSource[] changed;
        lock (_gate)
        {
            _unapplied[key] = _unapplied.GetValueOrDefault(key) + 1;
            changed = [.. _watches.Where(watch => watch.Document is null || string.Equals(watch.Document, key, StringComparison.OrdinalIgnoreCase)).Select(watch => watch.Changed)];
        }

        foreach (var source in changed)
        {
            source.Cancel();
        }
    }

    /// <summary>
    /// A change to a document has been applied: the server's handler of the notification has run.
    /// </summary>
    /// <param name="document">The document the change was to.</param>
    public void Applied(Uri document)
    {
        var key = Key(document);
        lock (_gate)
        {
            // a change the tap did not see, a message it could not read, is applied all the same: nothing is owed for it.
            if (_unapplied.GetValueOrDefault(key) is var count and > 1)
            {
                _unapplied[key] = count - 1;
            }
            else
            {
                _unapplied.Remove(key);
            }
        }
    }

    /// <summary>
    /// Answers a request about a document with what <paramref name="answer"/> makes of it, unless a change to the document arrives first: then the request
    /// stops, and is answered <c>ContentModified</c> (-32801).
    /// </summary>
    /// <typeparam name="T">What the request answers.</typeparam>
    /// <param name="document">The document the request is about.</param>
    /// <param name="token">The request's own token, which the client cancels.</param>
    /// <param name="answer">Makes the answer, with a token that is cancelled when the client cancels the request or a change to the document arrives.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="RpcErrorException">A change to the document arrived before the answer was made: the error is <see cref="ErrorCodes.ContentModified"/>.</exception>
    public Task<T> WhileCurrentAsync<T>(Uri document, CancellationToken token, Func<CancellationToken, Task<T>> answer)
        => WatchAsync(Key(document), token, answer);

    /// <summary>
    /// Answers a request about every document of the workspace with what <paramref name="answer"/> makes of them, unless a change to any document arrives
    /// first: then the request stops, and is answered <c>ContentModified</c> (-32801).
    /// </summary>
    /// <remarks>
    /// A request such as <c>workspace/diagnostic</c> is made of every module; a keystroke in any of them would wait for all of them, and the completion the
    /// client sends after it too. The client asks again for the workspace as it is now.
    /// </remarks>
    /// <typeparam name="T">What the request answers.</typeparam>
    /// <param name="token">The request's own token, which the client cancels.</param>
    /// <param name="answer">Makes the answer, with a token that is cancelled when the client cancels the request or a change to any document arrives.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="RpcErrorException">A change to a document arrived before the answer was made: the error is <see cref="ErrorCodes.ContentModified"/>.</exception>
    public Task<T> WhileWorkspaceCurrentAsync<T>(CancellationToken token, Func<CancellationToken, Task<T>> answer)
        => WatchAsync(document: null, token, answer);

    private async Task<T> WatchAsync<T>(string? document, CancellationToken token, Func<CancellationToken, Task<T>> answer)
    {
        using var changed = new CancellationTokenSource();
        var watch = (document, changed);
        lock (_gate)
        {
            _watches.Add(watch);
            if (document is null ? _unapplied.Count > 0 : _unapplied.ContainsKey(document))
            {
                changed.Cancel();
            }
        }

        try
        {
            using var either = CancellationTokenSource.CreateLinkedTokenSource(token, changed.Token);
            either.Token.ThrowIfCancellationRequested();
            return await answer(either.Token).WaitAsync(either.Token);
        }
        catch (OperationCanceledException) when (changed.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw ContentModified();
        }
        finally
        {
            lock (_gate)
            {
                _watches.Remove(watch);
            }
        }
    }

    /// <summary>
    /// The error a request about a document is answered with when the document changed while the answer was made: <c>ContentModified</c> (-32801), which the
    /// client asks again after.
    /// </summary>
    /// <remarks>
    /// OmniSharp answers an <see cref="OperationCanceledException"/> <c>RequestCancelled</c>, its own <c>ContentModifiedException</c> included: an
    /// <see cref="RpcErrorException"/> is answered its code.
    /// </remarks>
    public static RpcErrorException ContentModified() => new(ErrorCodes.ContentModified, null!, ContentModifiedMessage);

    /// <summary>
    /// Wraps the stream the server reads its client's messages from, so that a change to a document is known as it arrives (<see cref="Arrived"/>). What is
    /// read is passed on unchanged.
    /// </summary>
    /// <param name="input">The stream the client's messages arrive on.</param>
    /// <returns>The stream to read them from.</returns>
    public Stream Tap(Stream input) => new DocumentChangeTap(input, this);

    // the same document, however its URI is spelled on the wire and in a handler: both go through OmniSharp's DocumentUri.
    private static string Key(Uri document) => document.AbsoluteUri;

    internal static Uri? DocumentOf(string uri)
    {
        try
        {
            return DocumentUri.From(uri).ToUri();
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
