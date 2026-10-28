using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Server.Handlers.Workspace;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Commands;
using System.IO.Pipelines;
using System.Text;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Server;

/// <summary>
/// A request whose handler throws is answered <c>InternalError</c> on one line, and the whole exception is the server's log's: OmniSharp's own answer is
/// the exception's whole text, stack and source paths included, which a client shows its user.
/// </summary>
[TestClass]
public sealed class ServerErrorAnswersTests
{
    private sealed class Throw : IServerCommand
    {
        public string Name => "test.throw";

        public Task<JToken?> ExecuteAsync(JArray? arguments, IServerCommandContext context, CancellationToken token)
            => throw new UnauthorizedAccessException("Access to the path 'ws/.rdproj' is denied.", new IOException("the inner cause"));
    }

    private sealed class Refuse : IServerCommand
    {
        public string Name => "test.refuse";

        public Task<JToken?> ExecuteAsync(JArray? arguments, IServerCommandContext context, CancellationToken token)
            => throw new ServerCommandRefusedException("Nothing to do.");
    }

    // what the server's log was told.
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception)> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Records)
            {
                Records.Add((logLevel, exception));
            }
        }
    }

    // the error of the answer to a command, as it is written on the wire: the client turns an InternalError into its own "Internal error.", and hides it.
    private static async Task<JObject> ErrorOfAsync(string command, ILogger log)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var fromServer = new Pipe();
        var toClient = new Pipe();
        var wire = new StringBuilder();
        var copying = Task.Run(async () =>
        {
            while (true)
            {
                var read = await fromServer.Reader.ReadAsync();
                foreach (var segment in read.Buffer)
                {
                    lock (wire)
                    {
                        wire.Append(Encoding.UTF8.GetString(segment.Span));
                    }

                    await toClient.Writer.WriteAsync(segment);
                }

                fromServer.Reader.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted)
                {
                    break;
                }
            }
        });

        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(fromServer.Writer)
            .WithServices(services => services.AddSingleton<IServerCommand, Throw>().AddSingleton<IServerCommand, Refuse>())
            .WithOneLineErrors(log)
            .WithHandler<ExecuteCommandHandler>(), timeout.Token);
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer), timeout.Token);
        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;

        await Assert.ThrowsAsync<Exception>(
            () => languageClient.SendRequest("workspace/executeCommand", new ExecuteCommandParams { Command = command }).Returning<JToken>(timeout.Token));

        string written;
        lock (wire)
        {
            written = wire.ToString();
        }

        var body = written[written.LastIndexOf('{', written.LastIndexOf("\"error\"", StringComparison.Ordinal))..];
        return (JObject)JObject.Parse(body[..(body.LastIndexOf('}') + 1)])["error"]!;
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AHandlerThatThrows_IsAnsweredInternalError_OnOneLine_AndTheServersLogHasTheWholeOfIt()
    {
        var log = new RecordingLogger();

        var error = await ErrorOfAsync("test.throw", log);

        Assert.AreEqual(OmniSharp.Extensions.JsonRpc.Server.ErrorCodes.InternalError, error.Value<int>("code"));
        Assert.AreEqual("Access to the path 'ws/.rdproj' is denied.", error.Value<string>("message"), "no 'Internal Error - ', no stack, no source path");
        var logged = log.Records.Single(record => record.Level == LogLevel.Error).Exception;
        Assert.IsInstanceOfType<UnauthorizedAccessException>(logged, "the exception itself: its stack and its inner exception");
        Assert.IsInstanceOfType<IOException>(logged.InnerException);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ARefusal_IsAnsweredAsTheHandlerThrewIt_AndIsNoError()
    {
        var log = new RecordingLogger();

        var error = await ErrorOfAsync("test.refuse", log);

        Assert.AreEqual(-32803, error.Value<int>("code"));
        Assert.AreEqual("Nothing to do.", error.Value<string>("message"));
        Assert.IsFalse(log.Records.Any(record => record.Level == LogLevel.Error));
    }

    [TestMethod]
    [DataRow("first line\r\n   at Somewhere()", "first line", DisplayName = "the first line")]
    [DataRow("  padded  ", "padded", DisplayName = "trimmed")]
    [DataRow("", "InvalidOperationException", DisplayName = "no message: its type")]
    public void TheOneLine_IsTheMessagesFirstLine(string message, string expected)
        => Assert.AreEqual(expected, ServerErrorAnswers.OneLineOf(new InvalidOperationException(message)));
}
