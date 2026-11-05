using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace;

namespace RDCore.LanguageServer.Runtime;

/// <summary>
/// The dialogs of a program that the client has been asked to show and has not answered yet (<c>rdcore/session/interaction</c>): while one is open, a
/// diagnostics pull of the client is declined at once, instead of waiting for the program, which waits for the dialog.
/// </summary>
/// <remarks>
/// <para>
/// What is found of a module is of the text the environment host has, and the host does not take another text while its program waits. A pull that waited
/// for it would be answered only once the dialog is, and a client that waits for a pull's answer before it shows the dialog would never show it. So a pull
/// that comes while a dialog is open, or is running when one opens, is answered <c>ServerCancelled</c> (-32802, <strong>LSP 3.17</strong> §Document
/// Diagnostics) with <c>retriggerRequest: false</c>: a client that retriggers would ask again and again while the dialog is open.
/// </para>
/// <para>
/// Once the last dialog closes, a client whose pull was declined is asked to pull its diagnostics again (<see cref="PullAgain"/>). A client that takes no
/// refresh is declined all the same: the first pull it makes once no dialog is open is answered.
/// </para>
/// </remarks>
internal sealed class OpenDialogs
{
    private readonly object _gate = new();

    // what is cancelled when a dialog opens: each running pull's.
    private readonly List<CancellationTokenSource> _pulls = [];
    private int _open;
    private bool _declined;

    /// <summary>What asks the client to pull again, once the last dialog closes after a pull was declined; nothing until it is given.</summary>
    public Action? PullAgain { get; set; }

    /// <summary>Asks the client a dialog: the pulls that run meanwhile, and that come, are declined until it is answered, or the asking fails.</summary>
    /// <typeparam name="T">What the client answers.</typeparam>
    /// <param name="ask">Asks the client.</param>
    /// <returns>What the client answered.</returns>
    public async Task<T> WhileOpenAsync<T>(Func<Task<T>> ask)
    {
        // a pull is taken for declined where that is decided, under the gate, so the last dialog to close knows whether one was. A pull that is answered just
        // as the dialog opens is taken for declined too: a refresh too many costs a pull, and one too few leaves the client's diagnostics stale.
        CancellationTokenSource[] running;
        lock (_gate)
        {
            _open++;
            running = [.. _pulls];
            _declined |= running.Length > 0;
        }

        try
        {
            foreach (var pull in running)
            {
                try
                {
                    await pull.CancelAsync();
                }
                catch (ObjectDisposedException)
                {
                    // the pull was over before the dialog opened.
                }
                catch (AggregateException)
                {
                    // what was registered on the pull's token threw as it was cancelled - $/cancelRequest, as the host connection goes down: the pull is cancelled
                    // all the same, and the dialog is asked, and counted, as any other.
                }
            }

            return await ask();
        }
        finally
        {
            bool pullAgain;
            lock (_gate)
            {
                _open--;
                pullAgain = _open == 0 && _declined;
                if (pullAgain)
                {
                    _declined = false;
                }
            }

            if (pullAgain)
            {
                PullAgain?.Invoke();
            }
        }
    }

    /// <summary>
    /// Answers a diagnostics pull with what <paramref name="answer"/> makes, unless a dialog is open when it starts or opens before it is answered: then the
    /// pull stops, and is answered <c>ServerCancelled</c> (-32802).
    /// </summary>
    /// <typeparam name="T">What the pull answers.</typeparam>
    /// <param name="token">The pull's own token, which the client cancels.</param>
    /// <param name="answer">Makes the answer, with a token that is cancelled when the client cancels the pull or a dialog opens.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="RpcErrorException">A dialog was open: the error is <see cref="ErrorCodes.ServerCancelled"/>.</exception>
    public async Task<T> UnlessADialogIsOpenAsync<T>(CancellationToken token, Func<CancellationToken, Task<T>> answer)
    {
        using var opened = new CancellationTokenSource();
        lock (_gate)
        {
            if (_open > 0)
            {
                _declined = true;
                throw Declined();
            }

            _pulls.Add(opened);
        }

        try
        {
            using var either = CancellationTokenSource.CreateLinkedTokenSource(token, opened.Token);
            either.Token.ThrowIfCancellationRequested();
            return await answer(either.Token).WaitAsync(either.Token);
        }
        catch (OperationCanceledException) when (opened.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw Declined();
        }
        finally
        {
            lock (_gate)
            {
                _pulls.Remove(opened);
            }
        }
    }

    /// <summary>
    /// The error a diagnostics pull is answered with while a dialog is open: <c>ServerCancelled</c> (-32802), with <c>retriggerRequest: false</c>, so that the
    /// client waits to be asked to pull again.
    /// </summary>
    public static RpcErrorException Declined()
        => new(ErrorCodes.ServerCancelled, new DiagnosticServerCancellationData { RetriggerRequest = false }, WorkspaceMessages.PullDeclinedWhileADialogIsOpen);
}
