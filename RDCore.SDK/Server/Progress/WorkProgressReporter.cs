using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;

namespace RDCore.SDK.Server.Progress;

/// <summary>
/// One step of work that reports its progress: what it is doing now, and how much of it is done.
/// </summary>
/// <param name="Message">What the work is doing now, in a few words; <see langword="null"/> keeps what was said before.</param>
/// <param name="Percentage">How much of the work is done, from 0 to 100; <see langword="null"/> when that is not known, or keeps what was said before.</param>
public readonly record struct WorkProgressStep(string? Message, int? Percentage = null);

/// <summary>
/// Work that reports its progress to the client while it is done (<strong>LSP 3.17</strong> §Work Done Progress): it reports each step it takes, and it is
/// completed when it is done - which is reported, whatever the work came to.
/// </summary>
/// <remarks>
/// The work is an <see cref="IProgress{T}"/>, which is what the code that does it is given: it needs to know nothing of LSP, and reports to whatever it is given,
/// which can report to nothing. Disposing of it completes it, so that work that throws completes as surely as work that does not.
/// </remarks>
public interface IWorkProgress : IProgress<WorkProgressStep>, IDisposable
{
    /// <summary>
    /// Completes the work, which says so to the client - with <paramref name="message"/>, when there is one - and reports nothing more of it.
    /// </summary>
    /// <param name="message">What the work came to, in a few words.</param>
    void Complete(string? message = null);
}

/// <summary>
/// Reports the progress of a server's work to its client (<strong>LSP 3.17</strong> §Work Done Progress, §window/workDoneProgress/create, §$/progress): of
/// work done for a request that gave a token for it (<see cref="IWorkDoneProgressParams.WorkDoneToken"/>), and of work the server begins of its own.
/// </summary>
/// <remarks>
/// <para>
/// Progress is for work that takes longer than an answer does (<see cref="Threshold"/>): work that is done sooner is reported nothing of at all - no token is
/// created for it, and its client sees no progress flicker in and out. Work that is still going on then begins, with the last step it reported, and every step
/// after; and once begun, it ends, whatever comes of it.
/// </para>
/// <para>
/// A client that gave no token, or one that does not support progress (<c>window.workDoneProgress</c>), is reported nothing: the work is done all the same.
/// </para>
/// </remarks>
public interface IWorkProgressReporter
{
    /// <summary>How long work goes on before its progress is reported.</summary>
    TimeSpan Threshold { get; }

    /// <summary>
    /// Attaches the reporter to the server whose client it reports to: until it is, nothing is reported.
    /// </summary>
    /// <param name="manager">The server's work done manager.</param>
    void Attach(IServerWorkDoneManager manager);

    /// <summary>
    /// Begins work the server does of its own - indexing a workspace, say - whose progress it reports to a token it creates
    /// (<c>window/workDoneProgress/create</c>).
    /// </summary>
    /// <param name="title">What the work is, as the client shows it.</param>
    IWorkProgress Begin(string title);

    /// <summary>
    /// Begins work done for a request, whose progress is reported to the token the request gave for it; work for a request that gave none is reported nothing.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="title">What the work is, as the client shows it.</param>
    IWorkProgress BeginFor(IWorkDoneProgressParams request, string title);
}

/// <inheritdoc cref="IWorkProgressReporter"/>
/// <param name="threshold">How long work goes on before its progress is reported; <see cref="DefaultThreshold"/> unless said otherwise.</param>
public sealed class WorkProgressReporter(TimeSpan? threshold = null) : IWorkProgressReporter
{
    /// <summary>How long work goes on before its progress is reported, unless said otherwise: longer than an answer takes, shorter than a wait is noticed.</summary>
    public static TimeSpan DefaultThreshold { get; } = TimeSpan.FromMilliseconds(300);

    private IServerWorkDoneManager? _manager;

    /// <inheritdoc/>
    public TimeSpan Threshold { get; } = threshold ?? DefaultThreshold;

    /// <inheritdoc/>
    public void Attach(IServerWorkDoneManager manager) => _manager = manager;

    /// <inheritdoc/>
    public IWorkProgress Begin(string title)
        => _manager is { IsSupported: true } manager
            ? new DeferredWorkProgress(title, Threshold, begin => manager.Create(begin, Failed, null!, CancellationToken.None))
            : NoWorkProgress.Instance;

    /// <inheritdoc/>
    public IWorkProgress BeginFor(IWorkDoneProgressParams request, string title)
        => _manager is { } manager && request.WorkDoneToken is not null
            ? new DeferredWorkProgress(title, Threshold, begin => Task.FromResult(manager.For(request, begin, Failed, null!)))
            : NoWorkProgress.Instance;

    // work that failed is ended all the same, and says that it failed.
    private static WorkDoneProgressEnd Failed(Exception exception) => new() { Message = exception.Message };

    // work whose progress is reported to nothing: a client that gave no token for it, or that cannot be reported to.
    private sealed class NoWorkProgress : IWorkProgress
    {
        public static NoWorkProgress Instance { get; } = new();

        public void Report(WorkProgressStep value) { }

        public void Complete(string? message = null) { }

        public void Dispose() { }
    }

    // work whose progress is reported once it has gone on for the threshold: until then its steps are kept, the last of each kept, and nothing is sent.
    private sealed class DeferredWorkProgress : IWorkProgress
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _waiting = new();
        private readonly string _title;
        private string? _message;
        private int? _percentage;
        private string? _ending;
        private IWorkDoneObserver? _observer;
        private bool _completed;

        public DeferredWorkProgress(string title, TimeSpan threshold, Func<WorkDoneProgressBegin, Task<IWorkDoneObserver>> start)
        {
            _title = title;
            _ = StartAsync(threshold, start);
        }

        private async Task StartAsync(TimeSpan threshold, Func<WorkDoneProgressBegin, Task<IWorkDoneObserver>> start)
        {
            try
            {
                await Task.Delay(threshold, _waiting.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // done before it was worth reporting.
                return;
            }

            WorkDoneProgressBegin begin;
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                begin = new WorkDoneProgressBegin { Title = _title, Message = _message, Percentage = _percentage };
            }

            IWorkDoneObserver observer;
            try
            {
                observer = await start(begin).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // a client that cannot be told is a client that is told nothing: the work goes on.
                return;
            }

            lock (_gate)
            {
                if (!_completed)
                {
                    _observer = observer;
                    return;
                }
            }

            // the work was completed while the client was being told it began: it is told it ended.
            End(observer, _ending);
        }

        public void Report(WorkProgressStep value)
        {
            IWorkDoneObserver? observer;
            string? message;
            int? percentage;
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                // the percentage of work only goes up: a step that says less of it is done says nothing of it.
                _message = value.Message ?? _message;
                _percentage = value.Percentage is { } done ? Math.Clamp(Math.Max(done, _percentage ?? 0), 0, 100) : _percentage;
                (observer, message, percentage) = (_observer, _message, _percentage);
            }

            observer?.OnNext(message!, percentage, false);
        }

        public void Complete(string? message = null)
        {
            IWorkDoneObserver? observer;
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                (_completed, _ending, observer) = (true, message, _observer);
            }

            _waiting.Cancel();
            if (observer is not null)
            {
                End(observer, message);
            }
        }

        public void Dispose() => Complete();

        private static void End(IWorkDoneObserver observer, string? message)
        {
            if (message is not null)
            {
                observer.OnNext(message, 100, false);
            }

            observer.OnCompleted();
            observer.Dispose();
        }
    }
}
