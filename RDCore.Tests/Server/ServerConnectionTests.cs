using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Logging;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Server;

/// <summary>
/// What a language server of the platform logs as it starts and as it shuts down cleanly: nothing that is not a fault.
/// </summary>
[TestClass]
public sealed class ServerConnectionTests
{
    private static readonly TimeSpan Hung = TimeSpan.FromMinutes(5);

    [TestMethod]
    public async Task AReaderCompletedWhileAReadWaits_AnswersTheReadWithTheEndOfTheInput_ThenCompletesTheReaderItWraps()
    {
        var pipe = new Pipe();
        var input = new ServerInput(pipe.Reader);

        var waiting = input.ReadAsync().AsTask();
        Assert.IsFalse(waiting.IsCompleted, "nothing was written: the read waits");
        var completing = input.CompleteAsync().AsTask();

        var read = await waiting.WaitAsync(Hung);
        Assert.IsTrue(read.IsCompleted, "the end of the input");
        Assert.IsTrue(read.Buffer.IsEmpty);
        input.AdvanceTo(read.Buffer.Start, read.Buffer.End);

        await completing.WaitAsync(Hung);
        Assert.IsTrue((await pipe.Writer.FlushAsync()).IsCompleted, "the reader it wraps is completed once the read has come back");
        await input.CompleteAsync();
    }

    // OmniSharp stops its loop as the server is disposed by cancelling the read it waits on; the loop's catch then copies out the buffer of its last read, which
    // it has advanced past and the reader has taken back.
    [TestMethod]
    public async Task AReadCancelledByTheLoop_IsAnsweredWithTheEndOfTheInput()
    {
        var pipe = new Pipe();
        var input = new ServerInput(pipe.Reader);
        using var stopping = new CancellationTokenSource();

        var waiting = input.ReadAsync(stopping.Token).AsTask();
        await stopping.CancelAsync();

        var read = await waiting.WaitAsync(Hung);
        Assert.IsTrue(read.IsCompleted, "the end of the input");
        Assert.IsTrue(read.Buffer.IsEmpty);
        input.AdvanceTo(read.Buffer.Start, read.Buffer.End);
        await input.CompleteAsync().AsTask().WaitAsync(Hung);
    }

    // the exit completes the reader while the loop parses what it has read.
    [TestMethod]
    public async Task AReaderCompletedWhileItsReadIsParsed_LeavesTheBufferToTheLoop_ThenAnswersWithTheEndOfTheInput()
    {
        var pipe = new Pipe();
        var input = new ServerInput(pipe.Reader);
        var message = "Content-Length: 2\r\n\r\n{}"u8.ToArray();
        await pipe.Writer.WriteAsync(message);

        var read = await input.ReadAsync().AsTask().WaitAsync(Hung);
        await input.CompleteAsync().AsTask().WaitAsync(Hung);

        CollectionAssert.AreEqual(message, read.Buffer.ToArray(), "the buffer the loop holds is still what it read");
        input.AdvanceTo(read.Buffer.End);
        var end = await input.ReadAsync().AsTask().WaitAsync(Hung);
        Assert.IsTrue(end.IsCompleted, "the end of the input");
        Assert.IsTrue(end.Buffer.IsEmpty);
        input.AdvanceTo(end.Buffer.Start, end.Buffer.End);
        await input.CompleteAsync();
    }

    [TestMethod]
    public async Task AServerThatExits_ReportsNoUnhandledException()
    {
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(Hung);

        // a named pipe, opened overlapped on both ends, as a component and its parent open theirs (RDCorePlatformDefaultTransportLayer).
        var name = "rdcore-server-input-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        using var clientPipe = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        await Task.WhenAll(serverPipe.WaitForConnectionAsync(timeout.Token), clientPipe.ConnectAsync(timeout.Token));

        var input = new ServerInput(PipeReader.Create(serverPipe));
        var loop = new ProbedReader(input);
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(loop)
            .WithOutput(ServerOutput.Over(serverPipe))
            .WithUnhandledExceptionHandler(exception => reported.TrySetResult(exception)), timeout.Token);
        var connecting = LanguageClient.From(client => client.WithInput(clientPipe).WithOutput(clientPipe), timeout.Token);
        await Task.WhenAll(serving, connecting);
        using var languageClient = await connecting;
        using var server = await serving;

        // the client says shutdown and exit, as a component's parent does; OmniSharp completes the reader as it handles exit, while its loop waits on a read.
        await languageClient.Shutdown();
        await server.WaitForExit.WaitAsync(Hung);

        // the loop reports what fails its read before it completes its reader: once it has, all it would report is reported.
        var failure = await loop.LoopEnded.WaitAsync(Hung);
        Assert.IsNull(failure, failure?.ToString());
        Assert.IsFalse(reported.Task.IsCompleted, reported.Task.IsCompleted ? reported.Task.Result.ToString() : null);
    }

    // OmniSharp completes the server's output before its input as it handles exit: a stream disposed with the output fails the read that waits on it.
    [TestMethod]
    public async Task AServerOutputOverAStream_LeavesItOpen_AsItIsCompleted()
    {
        using var stream = new MemoryStream();
        var output = ServerOutput.Over(stream);
        await output.WriteAsync("{}"u8.ToArray());

        await output.CompleteAsync().AsTask().WaitAsync(Hung);

        Assert.IsTrue(stream.CanRead, "the stream is still open");
        CollectionAssert.AreEqual("{}"u8.ToArray(), stream.ToArray());
    }

    [TestMethod]
    public async Task AServerOutputThatIsCompleted_WritesNothingMore_AndSaysTheWritingIsOver()
    {
        using var stream = new MemoryStream();
        var output = ServerOutput.Over(stream);
        await output.CompleteAsync().AsTask().WaitAsync(Hung);

        var written = await output.WriteAsync("{}"u8.ToArray());
        "{}"u8.CopyTo(output.GetSpan(2));
        output.Advance(2);
        var flushed = await output.FlushAsync();

        Assert.IsTrue(written.IsCompleted, "a write");
        Assert.IsTrue(flushed.IsCompleted, "a flush");
        Assert.AreEqual(0, stream.Length);
    }

    // the loop is stopped as the server is disposed, by cancelling its read: what arrives after it is read by nothing.
    [TestMethod]
    public async Task AReaderWhoseLoopIsStopped_StopsReceiving_AndCompletesTheReaderItWraps()
    {
        var pipe = new Pipe();
        var received = new ProbedReader(pipe.Reader);
        var input = new ServerInput(received);
        using var stopping = new CancellationTokenSource();
        var waiting = input.ReadAsync(stopping.Token).AsTask();
        stopping.Cancel();
        Assert.IsTrue((await waiting.WaitAsync(Hung)).IsCompleted, "the end of the input");

        await pipe.Writer.WriteAsync("Content-Length: 2\r\n\r\n{}"u8.ToArray());

        await received.Completed.WaitAsync(Hung);
    }

    // the stream a server reads can fail as it is closed, as the server's input is completed.
    [TestMethod]
    public async Task AReaderWhoseStreamFailsAsItIsClosed_IsCompletedWithoutFault_AndAnswersWithTheEndOfTheInput()
    {
        var pipe = new Pipe();
        var input = new ServerInput(new ProbedReader(pipe.Reader, failsAsItIsCompleted: new IOException("The pipe is broken.")));

        await input.CompleteAsync().AsTask().WaitAsync(Hung);
        input.Complete();

        var end = await input.ReadAsync().AsTask().WaitAsync(Hung);
        Assert.IsTrue(end.IsCompleted, "the end of the input");
        Assert.IsFalse(end.IsCanceled);
    }

    [TestMethod]
    public async Task ARequestHeldUntilTheClientIsInitialized_IsNotWarnedOf()
    {
        var logged = new ConcurrentQueue<(string Category, LogLevel Level, string Message)>();
        var told = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toServer = new Pipe();
        var toClient = new Pipe();
        using var timeout = new CancellationTokenSource(Hung);

        // a message sent as the server answers initialize, which OmniSharp holds until it has started.
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .OnInitialized((started, _, _, _) =>
            {
                started.SendNotification("rdcore/test/held");
                return Task.CompletedTask;
            })
            .WithServices(services => services.AddLogging(builder =>
            {
                builder.AddProvider(new RecordingLoggerProvider(logged));
                builder.WithoutDeferredOutputWarnings();
            })), timeout.Token);
        var connecting = LanguageClient.From(client => client
            .WithInput(toClient.Reader)
            .WithOutput(toServer.Writer)
            .OnNotification("rdcore/test/held", () => told.TrySetResult()), timeout.Token);
        await Task.WhenAll(serving, connecting);
        using var languageClient = await connecting;
        using var server = await serving;

        // it was held, and then sent.
        await told.Task.WaitAsync(Hung);

        var warnings = logged.Where(record => record.Category == DeferredOutputLogging.Category && record.Level == LogLevel.Warning).ToList();
        Assert.IsEmpty(warnings, string.Join("\n", warnings.Select(record => record.Message)));
    }

    // OmniSharp stops its output as it handles exit: it completes the writer, then cancels the loop that writes. A message still being written then is
    // cancelled through the writer it completed.
    [TestMethod]
    public async Task AServerStoppedWhileAMessageIsWritten_StopsWithoutFault()
    {
        var stream = new WriteWaitsUntilCancelled();
        var output = new ServerOutput(PipeWriter.Create(stream));
        var handler = new OmniSharp.Extensions.JsonRpc.OutputHandler(output, new OmniSharp.Extensions.JsonRpc.Serialization.JsonRpcSerializer(), [],
            System.Reactive.Concurrency.Scheduler.Default, Microsoft.Extensions.Logging.Abstractions.NullLogger<OmniSharp.Extensions.JsonRpc.OutputHandler>.Instance);
        handler.Initialized();

        handler.Send(new OmniSharp.Extensions.JsonRpc.Client.OutgoingNotification { Method = "rdcore/test/written" });
        await stream.Writing.WaitAsync(Hung);

        await handler.StopAsync().WaitAsync(Hung);
    }

    // a stream whose reader has stopped reading: a write waits until it is cancelled.
    private sealed class WriteWaitsUntilCancelled : Stream
    {
        private readonly TaskCompletionSource _writing = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Writing => _writing.Task;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _writing.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // a reader read as the one it wraps, which says when it is completed, and when its loop has read its last and completed it.
    private sealed class ProbedReader(PipeReader inner, Exception? failsAsItIsCompleted = null) : PipeReader
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<Exception?> _loopEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _readTheLast;
        private Exception? _readFailure;

        public Task Completed => _completed.Task;

        // the failure of its last read, or none: the first completion after the read that ended the input, or that failed.
        public Task<Exception?> LoopEnded => _loopEnded.Task;

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var read = await inner.ReadAsync(cancellationToken);
                _readTheLast |= read.IsCompleted;
                return read;
            }
            catch (Exception exception)
            {
                _readFailure = exception;
                _readTheLast = true;
                throw;
            }
        }

        public override bool TryRead(out ReadResult result)
        {
            var read = inner.TryRead(out result);
            _readTheLast |= read && result.IsCompleted;
            return read;
        }

        public override void AdvanceTo(SequencePosition consumed) => inner.AdvanceTo(consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => inner.AdvanceTo(consumed, examined);

        public override void CancelPendingRead() => inner.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
            inner.Complete(exception);
            Completing();
        }

        public override async ValueTask CompleteAsync(Exception? exception = null)
        {
            await inner.CompleteAsync(exception);
            Completing();
        }

        private void Completing()
        {
            _completed.TrySetResult();
            if (_readTheLast)
            {
                _loopEnded.TrySetResult(_readFailure);
            }

            if (failsAsItIsCompleted is not null)
            {
                throw failsAsItIsCompleted;
            }
        }
    }

    // every record of the container, as it is logged.
    private sealed class RecordingLoggerProvider(ConcurrentQueue<(string Category, LogLevel Level, string Message)> logged) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, logged);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(string category, ConcurrentQueue<(string Category, LogLevel Level, string Message)> logged) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => System.Reactive.Disposables.Disposable.Empty;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => logged.Enqueue((category, logLevel, formatter(state, exception)));
        }
    }
}
