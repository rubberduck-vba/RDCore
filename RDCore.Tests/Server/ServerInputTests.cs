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
public sealed class ServerInputTests
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
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(input)
            .WithOutput(PipeWriter.Create(serverPipe))
            .WithUnhandledExceptionHandler(exception => reported.TrySetResult(exception)), timeout.Token);
        var connecting = LanguageClient.From(client => client.WithInput(clientPipe).WithOutput(clientPipe), timeout.Token);
        await Task.WhenAll(serving, connecting);
        using var languageClient = await connecting;
        using var server = await serving;

        // the client says shutdown and exit, as a component's parent does; OmniSharp completes the reader as it handles exit, while its loop waits on a read.
        await languageClient.Shutdown();
        await server.WaitForExit.WaitAsync(Hung);

        // completed, the reader has nothing reading it: the read that waited was answered with the end of the input, and nothing is left to fail.
        await input.CompleteAsync().AsTask().WaitAsync(Hung);
        Assert.IsFalse(reported.Task.IsCompleted, reported.Task.IsCompleted ? reported.Task.Result.ToString() : null);
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
