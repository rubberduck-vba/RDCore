using System.Buffers;
using System.IO.Pipelines;

namespace RDCore.SDK.Server;

/// <summary>
/// The reader a language server reads its client's messages with, which can be completed while it reads: the messages of the reader it wraps, until it is
/// completed, and then the end of the input.
/// </summary>
/// <remarks>
/// <para>
/// OmniSharp's input loop reads for as long as the connection is open, and OmniSharp completes its reader under that loop, as it handles <c>exit</c> and as
/// the server is disposed. Over a named pipe, completing a stream's reader disposes the pipe, the read that waits comes back, and the reader that was
/// completed fails it: <c>"Reading is not allowed after reader was completed"</c>, which the loop reports as an unhandled exception at every clean shutdown.
/// Completed while the loop parses what it has read, the reader takes back the buffer the loop is parsing.
/// </para>
/// <para>
/// This reader copies what the reader it wraps reads into a pipe of its own, which the loop reads. Completed, it stops reading the reader it wraps and
/// completes it, and ends its own pipe: the loop parses what it holds, reads the end of the input, and stops as it stops when the client closes the
/// connection. Only the loop's own reads complete the pipe it reads.
/// </para>
/// <para>
/// A read that the loop cancels, as OmniSharp stops the loop when the server is disposed, is answered with the end of the input as well.
/// </para>
/// </remarks>
public sealed class ServerInput : PipeReader
{
    private static readonly ReadResult End = new(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true);

    private readonly PipeReader _inner;

    // the loop reads a message whole before it consumes any of it: the pipe never waits for it to consume, however long the message.
    private readonly Pipe _received = new(new PipeOptions(pauseWriterThreshold: 0, resumeWriterThreshold: 0, useSynchronizationContext: false));
    private readonly CancellationTokenSource _ending = new();
    private readonly Task _receiving;

    // the loop has read the end of the input, and the pipe it read is completed: there is nothing left to advance past or to read.
    private bool _finished;

    /// <summary>
    /// Creates the reader of a language server's input, and starts reading the reader it wraps.
    /// </summary>
    /// <param name="inner">The reader of the stream the client's messages arrive on.</param>
    public ServerInput(PipeReader inner)
    {
        _inner = inner;
        _receiving = Task.Run(ReceiveAsync);
    }

    /// <inheritdoc/>
    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_finished)
        {
            return End;
        }

        try
        {
            return Finish(await _received.Reader.ReadAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // the loop is stopped as the server is disposed, by cancelling its read: a read that fails sends the loop to a catch that copies out the buffer of
            // its last read, which it has advanced past. The loop holds no buffer of the pipe while it waits.
            _received.Reader.Complete();
            _finished = true;
            return End;
        }
    }

    /// <inheritdoc/>
    public override bool TryRead(out ReadResult result)
    {
        if (_finished)
        {
            result = End;
            return true;
        }

        if (_received.Reader.TryRead(out var read))
        {
            result = Finish(read);
            return true;
        }

        result = default;
        return false;
    }

    /// <inheritdoc/>
    public override void AdvanceTo(SequencePosition consumed)
    {
        if (!_finished)
        {
            _received.Reader.AdvanceTo(consumed);
        }
    }

    /// <inheritdoc/>
    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        if (!_finished)
        {
            _received.Reader.AdvanceTo(consumed, examined);
        }
    }

    /// <inheritdoc/>
    public override void CancelPendingRead() => _received.Reader.CancelPendingRead();

    /// <inheritdoc/>
    public override void Complete(Exception? exception = null) => _ = CompleteAfterReceiving();

    /// <inheritdoc/>
    public override ValueTask CompleteAsync(Exception? exception = null) => new(CompleteAfterReceiving());

    // the end of the input, read by the loop with nothing left to parse, completes the pipe it reads: no buffer of it is held.
    private ReadResult Finish(ReadResult read)
    {
        if (!read.IsCompleted || !read.Buffer.IsEmpty)
        {
            return read;
        }

        _received.Reader.AdvanceTo(read.Buffer.End);
        _received.Reader.Complete();
        _finished = true;
        return End;
    }

    private async Task ReceiveAsync()
    {
        Exception? failure = null;
        try
        {
            while (true)
            {
                var read = await _inner.ReadAsync(_ending.Token);
                foreach (var segment in read.Buffer)
                {
                    _received.Writer.Write(segment.Span);
                }

                _inner.AdvanceTo(read.Buffer.End);
                await _received.Writer.FlushAsync(_ending.Token);
                if (read.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (_ending.IsCancellationRequested)
        {
            // completed: the read of the reader it wraps came back, and nothing reads it now.
        }
        catch (Exception exception)
        {
            // the loop reads the failure of the stream as it would have read it from the stream's own reader.
            failure = exception;
        }

        await _inner.CompleteAsync(failure);
        await _received.Writer.CompleteAsync(failure);
    }

    // completed once, however many times it is told to: by the exit, by the loop as it stops, and by the server as it is disposed.
    // receiving ends by completing the reader it wraps and the pipe it fills.
    private Task CompleteAfterReceiving()
    {
        _ending.Cancel();
        return _receiving;
    }
}
