using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RDCore.SDK.Platform.Channels;

/// <summary>
/// A duplex channel of calls between two processes of the platform: each end calls methods of the other by name, and waits for the answer.
/// </summary>
/// <remarks>
/// <para>
/// The language server protocol carries what an editor and the platform say to each other, and is built for it: a document changes, a request about it is ordered
/// after the change. A call that a program makes to something another process runs is not that, and cannot afford it - a program makes them by the thousand, one
/// after the other, each waiting for the last. This is the bare minimum such a call needs: a frame that says which call it is and which answer is its own, a
/// thread that reads them, and nothing else between a caller and the code it calls.
/// </para>
/// <para>
/// Each frame is its length, its kind, the number of the call it is or answers, the name of the method it calls, and its payload, which is JSON.
/// </para>
/// <para>
/// What a call costs is mostly the threads it wakes: one at the other end to read it, one at this end to read the answer, as a pipe that is pinged and ponged
/// costs. Nothing else is put in the way. A frame is read in one read of the pipe, not one for its length and one for the rest - each read of a pipe opened for
/// overlapped I/O is a wait of its own, which doubled what a call cost. The thread that reads a call answers it itself, and hands the reading over to another,
/// so that a handler that waits on the other end - whose answer has to be read meanwhile - never keeps the channel from being read. A caller that waits for its
/// answer (<see cref="Call"/>) looks for it for a moment before it sleeps, so that an answer that comes back at once does not have to wake it.
/// </para>
/// <para>
/// ⚠️ A handler runs on whatever thread read its call, and two can run at once: what a handler touches, it guards. The order of the calls is the callers' to
/// keep, by waiting for each answer before making the next call.
/// </para>
/// </remarks>
public sealed class CallChannel : IDisposable
{
    private const byte CallFrame = 1;
    private const byte AnswerFrame = 2;
    private const byte FailureFrame = 3;

    // how long a caller looks for its answer before it sleeps: about what a call costs that the other end answers at once. A thread that sleeps has to be woken,
    // which costs as much again.
    private static readonly TimeSpan Spin = TimeSpan.FromMicroseconds(250);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Stream _stream;
    private readonly BufferedStream _reading;
    private readonly object _writing = new();
    private readonly ConcurrentDictionary<long, Waiter> _pending = new();
    private readonly Dictionary<string, Func<byte[], byte[]>> _handlers = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _followers = new(0);
    private int _idle;
    private bool _started;
    private long _lastCall;
    private int _disposed;

    /// <summary>
    /// Creates a channel over a connected stream. Nothing is read until <see cref="Start"/>.
    /// </summary>
    /// <param name="stream">A duplex stream that is connected to the other end - a named pipe, by and large.</param>
    public CallChannel(Stream stream)
    {
        _stream = stream;

        // a frame is read as one read of what has arrived, rather than one for its length and another for the rest: each read of a pipe costs a wait of its own.
        _reading = new BufferedStream(stream, 64 * 1024);
    }

    /// <summary>
    /// Completes when the channel is closed: the other end closed it, or went away, or this end was disposed.
    /// </summary>
    public Task Closed => _closed.Task;

    /// <summary>
    /// Says how the calls of a method are answered. Every method is registered before the channel is <see cref="Start">started</see>.
    /// </summary>
    /// <typeparam name="TCall">What the method is called with.</typeparam>
    /// <typeparam name="TAnswer">What it answers.</typeparam>
    /// <param name="method">The name of the method.</param>
    /// <param name="handler">Answers a call; what it throws is the caller's failure.</param>
    /// <exception cref="InvalidOperationException">The channel has started.</exception>
    public void Handle<TCall, TAnswer>(string method, Func<TCall, TAnswer> handler)
    {
        if (_started)
        {
            throw new InvalidOperationException("The methods of a channel are registered before it starts.");
        }

        _handlers[method] = payload => JsonSerializer.SerializeToUtf8Bytes(handler(JsonSerializer.Deserialize<TCall>(payload, Json)!), Json);
    }

    /// <summary>
    /// Starts reading what the other end sends.
    /// </summary>
    public void Start()
    {
        _started = true;
        Read();
    }

    /// <summary>
    /// Calls a method of the other end, and waits for the answer.
    /// </summary>
    /// <typeparam name="TCall">What the method is called with.</typeparam>
    /// <typeparam name="TAnswer">What it answers.</typeparam>
    /// <param name="method">The name of the method.</param>
    /// <param name="call">What it is called with.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="CallChannelException">The other end failed to answer, or the channel closed first.</exception>
    public TAnswer Call<TCall, TAnswer>(string method, TCall call)
    {
        var waiter = new SynchronousWaiter();
        Send(method, call, waiter);
        return JsonSerializer.Deserialize<TAnswer>(waiter.Wait(), Json)!;
    }

    /// <summary>
    /// Calls a method of the other end.
    /// </summary>
    /// <typeparam name="TCall">What the method is called with.</typeparam>
    /// <typeparam name="TAnswer">What it answers.</typeparam>
    /// <param name="method">The name of the method.</param>
    /// <param name="call">What it is called with.</param>
    /// <returns>Completes with the answer; faults with a <see cref="CallChannelException"/> when the other end failed to answer, or the channel closed first.</returns>
    public Task<TAnswer> CallAsync<TCall, TAnswer>(string method, TCall call)
    {
        var waiter = new AsynchronousWaiter();
        try
        {
            Send(method, call, waiter);
        }
        catch (CallChannelException failure)
        {
            return Task.FromException<TAnswer>(failure);
        }

        return waiter.Answer.ContinueWith(
            static answered => JsonSerializer.Deserialize<TAnswer>(answered.GetAwaiter().GetResult(), Json)!,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Send<TCall>(string method, TCall call, Waiter waiter)
    {
        var number = Interlocked.Increment(ref _lastCall);
        _pending[number] = waiter;
        if (_closed.Task.IsCompleted)
        {
            _ = _pending.TryRemove(number, out _);
            throw new CallChannelException(Exceptions.CallChannel_Closed);
        }

        try
        {
            Write(CallFrame, number, method, JsonSerializer.SerializeToUtf8Bytes(call, Json));
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            _ = _pending.TryRemove(number, out _);
            Close();
            throw new CallChannelException(Exceptions.CallChannel_Closed, exception);
        }
    }

    // [length][kind][number][method length][method][payload]: the length counts everything after itself.
    private void Write(byte kind, long number, string method, ReadOnlySpan<byte> payload)
    {
        var name = Encoding.UTF8.GetByteCount(method);
        var length = 1 + 8 + 2 + name + payload.Length;
        var frame = new byte[4 + length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, length);
        frame[4] = kind;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(5), number);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(13), checked((ushort)name));
        _ = Encoding.UTF8.GetBytes(method, frame.AsSpan(15));
        payload.CopyTo(frame.AsSpan(15 + name));

        lock (_writing)
        {
            _stream.Write(frame);
            _stream.Flush();
        }
    }

    // One thread reads at a time: the one that reads a call hands the reading to another and answers the call, then waits to be handed the reading back. The
    // threads are the channel's own, not the pool's - a handler blocks its thread for as long as it waits, and a reader that the pool has no thread for would
    // keep every answer from being read: one that is idle takes over, or a new one when none is.
    private void Read()
    {
        int idle;
        do
        {
            idle = Volatile.Read(ref _idle);
            if (idle == 0)
            {
                new Thread(Follow) { IsBackground = true, Name = "RDCore call channel" }.Start();
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _idle, idle - 1, idle) != idle);

        _ = _followers.Release();
    }

    private void Follow()
    {
        while (ReadUntilACall() is { } call)
        {
            Answer(call.Number, call.Method, call.Payload);

            // the next call comes as soon as the last one is answered: the thread looks for its turn for a moment before it sleeps.
            _ = Interlocked.Increment(ref _idle);
            var started = Stopwatch.GetTimestamp();
            var promoted = false;
            while (!(promoted = _followers.Wait(0)) && Stopwatch.GetElapsedTime(started) < Spin)
            {
                Thread.SpinWait(20);
            }

            if (!promoted)
            {
                _followers.Wait();
            }

            if (_closed.Task.IsCompleted)
            {
                return;
            }
        }
    }

    private (long Number, string Method, byte[] Payload)? ReadUntilACall()
    {
        var header = new byte[4];
        try
        {
            while (true)
            {
                _reading.ReadExactly(header);
                var frame = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
                _reading.ReadExactly(frame);

                var kind = frame[0];
                var number = BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(1));
                var name = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9));
                var payload = frame[(11 + name)..];

                switch (kind)
                {
                    case CallFrame:
                        Read();
                        return (number, Encoding.UTF8.GetString(frame, 11, name), payload);
                    case AnswerFrame when _pending.TryRemove(number, out var answered):
                        answered.Complete(payload);
                        break;
                    case FailureFrame when _pending.TryRemove(number, out var failed):
                        failed.Fail(new CallChannelException(Encoding.UTF8.GetString(payload)));
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or ObjectDisposedException or OperationCanceledException)
        {
            // the other end is gone, or this one was disposed: either way, nothing more is read.
            Close();
            return null;
        }
    }

    private void Answer(long number, string method, byte[] payload)
    {
        byte kind;
        byte[] answer;
        try
        {
            if (!_handlers.TryGetValue(method, out var handler))
            {
                throw new CallChannelException(string.Format(System.Globalization.CultureInfo.CurrentCulture, Exceptions.CallChannel_NoSuchMethod, method));
            }

            answer = handler(payload);
            kind = AnswerFrame;
        }
        catch (Exception exception)
        {
            answer = Encoding.UTF8.GetBytes(exception.Message);
            kind = FailureFrame;
        }

        try
        {
            Write(kind, number, string.Empty, answer);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // a caller that is gone has nobody to answer.
            Close();
        }
    }

    // the calls that wait for an answer get none: they fail, and say why.
    private void Close()
    {
        if (!_closed.TrySetResult())
        {
            return;
        }

        foreach (var number in _pending.Keys)
        {
            if (_pending.TryRemove(number, out var waiting))
            {
                waiting.Fail(new CallChannelException(Exceptions.CallChannel_Closed));
            }
        }

        // the threads that wait for their turn to read have nothing left to read, and go.
        _ = _followers.Release(Math.Max(1, Volatile.Read(ref _idle)));
    }

    /// <summary>
    /// Closes the channel, and the stream it reads.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Close();
        _stream.Dispose();
    }

    // what waits for the answer to a call.
    private abstract class Waiter
    {
        public abstract void Complete(byte[] payload);

        public abstract void Fail(Exception failure);
    }

    // a thread that waits: it looks for its answer for a moment before it sleeps, so that an answer that comes back at once does not have to wake it.
    private sealed class SynchronousWaiter : Waiter
    {
        private readonly ManualResetEventSlim _done = new(false, spinCount: 1);
        private byte[]? _payload;
        private Exception? _failure;

        public override void Complete(byte[] payload)
        {
            _payload = payload;
            _done.Set();
        }

        public override void Fail(Exception failure)
        {
            _failure = failure;
            _done.Set();
        }

        public byte[] Wait()
        {
            var started = Stopwatch.GetTimestamp();
            while (!_done.IsSet && Stopwatch.GetElapsedTime(started) < Spin)
            {
                Thread.SpinWait(20);
            }

            _done.Wait();
            _done.Dispose();
            return _failure is null ? _payload! : throw _failure;
        }
    }

    private sealed class AsynchronousWaiter : Waiter
    {
        private readonly TaskCompletionSource<byte[]> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<byte[]> Answer => _answer.Task;

        public override void Complete(byte[] payload) => _answer.TrySetResult(payload);

        public override void Fail(Exception failure) => _answer.TrySetException(failure);
    }
}

/// <summary>
/// A call over a <see cref="CallChannel"/> got no answer: the other end failed to answer it, or the channel closed before it did.
/// </summary>
public sealed class CallChannelException : Exception
{
    /// <summary>
    /// Creates the failure.
    /// </summary>
    /// <param name="message">What the other end said, or why there was no answer.</param>
    /// <param name="inner">What made it fail, when it failed at this end.</param>
    public CallChannelException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
