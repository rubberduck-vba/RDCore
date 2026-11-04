using System.IO.Pipelines;

namespace RDCore.SDK.Server;

/// <summary>
/// The writer a language server writes its messages to its client with, which can be completed while a message is being written: the writer it wraps,
/// write for write, until it is completed, and then nothing more.
/// </summary>
/// <remarks>
/// <para>
/// OmniSharp stops its output as it handles <c>exit</c>: it completes its writer, then cancels the loop that writes to it. A message still being written
/// then, as when the client has stopped reading, is cancelled through the token it was written with, and a stream's writer cancels the write with a token
/// source it disposed as it was completed: the <see cref="ObjectDisposedException"/> ends the process.
/// </para>
/// <para>
/// This writer writes to the writer it wraps without the caller's token, and cancels the write itself only while the writer it wraps is not completed.
/// Completed while a message is being written, it cancels that write, and completes the writer it wraps without writing the rest: the client of a server
/// that exits reads nothing more of it.
/// </para>
/// </remarks>
/// <param name="inner">The writer of the stream the server's messages are written to.</param>
public sealed class ServerOutput(PipeWriter inner) : PipeWriter
{
    private readonly object _gate = new();
    private Task _writing = Task.CompletedTask;
    private Task? _completion;

    /// <inheritdoc/>
    public override void Advance(int bytes) => inner.Advance(bytes);

    /// <inheritdoc/>
    public override Memory<byte> GetMemory(int sizeHint = 0) => inner.GetMemory(sizeHint);

    /// <inheritdoc/>
    public override Span<byte> GetSpan(int sizeHint = 0) => inner.GetSpan(sizeHint);

    /// <inheritdoc/>
    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        => WriteAsync(() => inner.FlushAsync(CancellationToken.None), cancellationToken);

    /// <inheritdoc/>
    public override ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
        => WriteAsync(() => inner.WriteAsync(source, CancellationToken.None), cancellationToken);

    /// <inheritdoc/>
    public override void CancelPendingFlush()
    {
        lock (_gate)
        {
            if (_completion is null)
            {
                inner.CancelPendingFlush();
            }
        }
    }

    /// <inheritdoc/>
    public override void Complete(Exception? exception = null) => _ = CompleteOnce(exception);

    /// <inheritdoc/>
    public override ValueTask CompleteAsync(Exception? exception = null) => new(CompleteOnce(exception));

    private async ValueTask<FlushResult> WriteAsync(Func<ValueTask<FlushResult>> write, CancellationToken cancellationToken)
    {
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _writing = written.Task;
        }

        FlushResult flushed;
        try
        {
            using (cancellationToken.Register(CancelPendingFlush))
            {
                flushed = await write();
            }
        }
        finally
        {
            written.SetResult();
        }

        if (flushed.IsCanceled && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return flushed;
    }

    // completed once, however many times it is told to.
    private Task CompleteOnce(Exception? exception)
    {
        TaskCompletionSource completion;
        Task writing;
        lock (_gate)
        {
            if (_completion is { } started)
            {
                return started;
            }

            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = completion.Task;
            writing = _writing;
            if (!writing.IsCompleted)
            {
                inner.CancelPendingFlush();
            }
        }

        _ = CompleteAfterWritingAsync(writing, exception, completion);
        return completion.Task;
    }

    private async Task CompleteAfterWritingAsync(Task writing, Exception? exception, TaskCompletionSource completion)
    {
        try
        {
            if (writing.IsCompleted)
            {
                await inner.CompleteAsync(exception);
            }
            else
            {
                // the writer it wraps writes what it holds as it is completed, unless it is completed with a fault: what was not written is not written.
                await writing;
                await inner.CompleteAsync(exception ?? new OperationCanceledException("The server's output was completed while a message was being written."));
            }

            completion.TrySetResult();
        }
        catch (Exception failure)
        {
            completion.TrySetException(failure);
        }
    }
}
