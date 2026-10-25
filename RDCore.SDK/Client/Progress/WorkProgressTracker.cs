using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Client.Progress;

/// <summary>
/// Where a piece of work the server reports the progress of stands (<strong>LSP 3.17</strong> §Work Done Progress): what it is, what it is doing now, and how
/// much of it is done.
/// </summary>
/// <param name="Token">The token its progress is reported to.</param>
/// <param name="Title">What the work is.</param>
/// <param name="Message">What it is doing now; <see langword="null"/> when it has not said.</param>
/// <param name="Percentage">How much of it is done, from 0 to 100; <see langword="null"/> when that is not known.</param>
/// <param name="IsDone">Whether it is done: it ended, or the server that did it went away.</param>
/// <param name="Origin">Whose work it is: done for a request of the client's, or begun by the server of its own.</param>
public sealed record class WorkProgressState(ProgressToken Token, string Title, string? Message, int? Percentage, bool IsDone = false, WorkProgressOrigin Origin = WorkProgressOrigin.Request);

/// <summary>
/// Whose work a piece of work the server reports the progress of is - which says where a client shows it.
/// </summary>
public enum WorkProgressOrigin
{
    /// <summary>Work done for a request the client made, which gave it a token: the client waits for its answer.</summary>
    Request,

    /// <summary>Work the server began of its own (<c>window/workDoneProgress/create</c>): indexing a workspace, say. Nothing waits for it.</summary>
    Server,
}

/// <summary>
/// The work a server reports the progress of to a client, as it goes: each piece begun, every step it reports, and its end - folded into where each piece
/// stands (<see cref="WorkProgressState"/>), which is what a client shows.
/// </summary>
/// <remarks>
/// <para>
/// A piece of work is tracked by its token: one the server created for work of its own (<c>window/workDoneProgress/create</c>), or one the client gave a request
/// for the work done for it (<see cref="IWorkDoneProgressParams.WorkDoneToken"/>). Nothing is shown of a token until the server begins the work: it does not,
/// for work that is done sooner than progress is worth reporting.
/// </para>
/// <para>
/// <see cref="Changed"/> is raised on whatever thread the progress arrives on: a client that draws it serializes the drawing itself.
/// </para>
/// </remarks>
public sealed class WorkProgressTracker
{
    private readonly object _gate = new();
    private readonly List<WorkProgressState> _active = [];

    /// <summary>Raised when a piece of work begins, reports a step, or ends; with where it stands then.</summary>
    public event Action<WorkProgressState>? Changed;

    /// <summary>The work that is begun and not ended, in the order it began.</summary>
    public IReadOnlyList<WorkProgressState> Active
    {
        get
        {
            lock (_gate)
            {
                return [.. _active];
            }
        }
    }

    /// <summary>
    /// Tracks the progress reported to <paramref name="token"/>.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="progress">What is reported to it.</param>
    /// <param name="origin">Whose work it is.</param>
    /// <returns>What stops the tracking: work that has not ended is taken for ended when it is disposed - the request it was for is answered, say.</returns>
    public IDisposable Track(ProgressToken token, IObservable<WorkDoneProgress> progress, WorkProgressOrigin origin = WorkProgressOrigin.Request)
    {
        var observer = new Observer(this, token, origin);
        var subscription = progress.Subscribe(observer);
        return new Tracking(subscription, observer);
    }

    private void Fold(ProgressToken token, WorkDoneProgress progress, WorkProgressOrigin origin)
    {
        WorkProgressState? state;
        lock (_gate)
        {
            var index = _active.FindIndex(active => active.Token == token);
            var current = index >= 0 ? _active[index] : null;
            state = progress switch
            {
                WorkDoneProgressBegin begin when current is null => new WorkProgressState(token, begin.Title, begin.Message, begin.Percentage, Origin: origin),
                WorkDoneProgressReport report when current is not null
                    => current with { Message = report.Message ?? current.Message, Percentage = report.Percentage ?? current.Percentage },
                WorkDoneProgressEnd end when current is not null => current with { Message = end.Message ?? current.Message, IsDone = true },
                _ => null,
            };

            if (state is null)
            {
                return;
            }

            if (state.IsDone)
            {
                _active.RemoveAt(index);
            }
            else if (index >= 0)
            {
                _active[index] = state;
            }
            else
            {
                _active.Add(state);
            }
        }

        Changed?.Invoke(state);
    }

    // work that is not heard from any more - its progress is completed or failed, or it is not tracked - is done, if it was begun.
    private void Abandon(ProgressToken token, WorkProgressOrigin origin) => Fold(token, new WorkDoneProgressEnd(), origin);

    private sealed class Observer(WorkProgressTracker tracker, ProgressToken token, WorkProgressOrigin origin) : IObserver<WorkDoneProgress>
    {
        public void OnNext(WorkDoneProgress value) => tracker.Fold(token, value, origin);

        public void OnCompleted() => tracker.Abandon(token, origin);

        public void OnError(Exception error) => tracker.Abandon(token, origin);
    }

    private sealed class Tracking(IDisposable subscription, Observer observer) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                subscription.Dispose();
                observer.OnCompleted();
            }
        }
    }
}
