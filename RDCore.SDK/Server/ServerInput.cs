using System.Buffers;
using System.IO.Pipelines;

namespace RDCore.SDK.Server;

/// <summary>
/// The reader a language server reads its client's messages with, which can be completed while it reads: a reader that is the one it wraps, read for read,
/// until it is completed.
/// </summary>
/// <remarks>
/// <para>
/// OmniSharp's input loop waits on a read for as long as the connection is open, and OmniSharp completes its reader under that read, as it handles
/// <c>exit</c> and as the server is disposed. Over a named pipe, completing a stream's reader disposes the pipe, the read comes back, and the reader that
/// was completed fails it: <c>"Reading is not allowed after reader was completed"</c>, which the loop reports as an unhandled exception at every clean
/// shutdown.
/// </para>
/// <para>
/// Completed while a read is waiting, this reader cancels the read and answers it with the end of the input, and completes the reader it wraps once the read
/// has come back: the loop stops as it stops when the client closes the connection.
/// </para>
/// </remarks>
/// <param name="inner">The reader of the stream the client's messages arrive on.</param>
public sealed class ServerInput(PipeReader inner) : PipeReader
{
    private static readonly ReadResult End = new(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true);

    private readonly object _gate = new();
    private readonly CancellationTokenSource _ending = new();
    private Task _reading = Task.CompletedTask;
    private TaskCompletionSource? _completion;

    // the read that came back is the end of the input this reader answered, not one of the reader it wraps: there is nothing of it to advance past.
    private volatile bool _ended;

    /// <inheritdoc/>
    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_ending.IsCancellationRequested)
            {
                _ended = true;
                return End;
            }

            _reading = returned.Task;
        }

        try
        {
            using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _ending.Token);
            return await inner.ReadAsync(reading.Token);
        }
        catch (OperationCanceledException) when (_ending.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _ended = true;
            return End;
        }
        finally
        {
            returned.SetResult();
        }
    }

    /// <inheritdoc/>
    public override bool TryRead(out ReadResult result)
    {
        if (_ending.IsCancellationRequested)
        {
            _ended = true;
            result = End;
            return true;
        }

        return inner.TryRead(out result);
    }

    /// <inheritdoc/>
    public override void AdvanceTo(SequencePosition consumed)
    {
        if (!_ended)
        {
            inner.AdvanceTo(consumed);
        }
    }

    /// <inheritdoc/>
    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        if (!_ended)
        {
            inner.AdvanceTo(consumed, examined);
        }
    }

    /// <inheritdoc/>
    public override void CancelPendingRead() => inner.CancelPendingRead();

    /// <inheritdoc/>
    public override void Complete(Exception? exception = null) => _ = CompleteOnce(exception);

    /// <inheritdoc/>
    public override ValueTask CompleteAsync(Exception? exception = null) => new(CompleteOnce(exception));

    // completed once, however many times it is told to: by the loop as it stops, by the exit, and by the server as it is disposed.
    private Task CompleteOnce(Exception? exception)
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_completion is { } started)
            {
                return started.Task;
            }

            completion = _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = CompleteAfterReadingAsync(exception, completion);
        return completion.Task;
    }

    private async Task CompleteAfterReadingAsync(Exception? exception, TaskCompletionSource completion)
    {
        try
        {
            _ending.Cancel();

            Task reading;
            lock (_gate)
            {
                reading = _reading;
            }

            // the read that waits comes back first, answered with the end of the input; the reader it wraps is completed with nothing reading it.
            await reading;
            await inner.CompleteAsync(exception);
            completion.TrySetResult();
        }
        catch (Exception failure)
        {
            completion.TrySetException(failure);
        }
    }
}
