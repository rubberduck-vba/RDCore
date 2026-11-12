using RDCore.External.Interaction;
using System.Collections.Concurrent;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Pipes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.CLI.App.Repl;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Server.Configuration;
using static RDCore.Tests.Cli.Repl.ScriptedKeys;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// A question the language server asks the shell (<c>window/showMessageRequest</c>) over the shell's own language client: it is answered whatever else the
/// server sends while the user is asked.
/// </summary>
[TestClass]
public sealed class ReplQuestionWireTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [Method("test/ping", Direction.ClientToServer)]
    public sealed record class PingParams : IRequest<int>;

    // keys that come when the test sends them; reading says that the question is being asked.
    private sealed class WaitingKeys : IConsoleKeys
    {
        private readonly BlockingCollection<ConsoleKeyInfo> _keys = [];

        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsInteractive => true;

        public void Send(ConsoleKeyInfo key) => _keys.Add(key);

        public ConsoleKeyInfo ReadKey(CancellationToken token)
        {
            Reading.TrySetResult();
            return _keys.Take(token);
        }

        public string? ReadLine(CancellationToken token) => null;
    }

    // the language server the shell starts and connects to: an OmniSharp server on the pipe, in this process, which answers the platform handshake and serves
    // test/ping once it is let to.
    private sealed class InProcessLanguageServer(Task pong) : IRDCoreServerProcess, IChildConnectionFactory
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<IDisposable> _held = [];

        public TaskCompletionSource<ILanguageServer> Served { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ProcessId => Environment.ProcessId;

        public bool HasExited => _exit.Task.IsCompleted;

        public int ExitCode => 0;

        public ChildConnection Create()
            => Hold(new ChildConnection(
                this, new RDCore.SDK.Server.RDCorePlatformDefaultTransportLayer(Microsoft.Extensions.Options.Options.Create(new SdkAppOptions())),
                NullLogger<ChildConnection>.Instance));

        public Task StartAsync(string relativePath, string pipeName, CancellationTokenSource tokenSource, string? mode = null)
        {
            var pipe = Hold(new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous));
            _ = ServeAsync(pipe);
            return Task.CompletedTask;
        }

        private async Task ServeAsync(NamedPipeServerStream pipe)
        {
            await pipe.WaitForConnectionAsync();
            var server = Hold(await OmniSharpLanguageServer.From(server => server
                .WithInput(System.IO.Pipelines.PipeReader.Create(pipe))
                .WithOutput(System.IO.Pipelines.PipeWriter.Create(pipe))
                .WithSerializer(new RDCore.SDK.Server.Serialization.PullDiagnosticsLspSerializer())
                .OnJsonRequest(RDCorePlatformProtocol.PlatformInitialize, (JToken _, CancellationToken _) => Task.FromResult<JToken>(
                    JObject.FromObject(new { component = (int)CoreServerComponent.LanguageServer, provided = Array.Empty<string>() })))
                .OnRequest<PingParams, int>("test/ping", async (_, _) =>
                {
                    await pong;
                    return 1;
                })));
            Served.TrySetResult(server);
        }

        private T Hold<T>(T disposable) where T : IDisposable
        {
            lock (_held)
            {
                _held.Add(disposable);
            }

            return disposable;
        }

        public Task WaitForExitAsync() => _exit.Task;

        public void Shutdown() => _exit.TrySetResult();

        // the connection first, then the server, then the pipe it served on.
        public void Dispose()
        {
            IDisposable[] held;
            lock (_held)
            {
                held = [.. _held];
                _held.Clear();
            }

            _exit.TrySetResult();
            foreach (var disposable in held.OfType<ChildConnection>().Concat<IDisposable>(held.Where(each => each is not ChildConnection).Reverse()))
            {
                disposable.Dispose();
            }
        }
    }

    // OmniSharp's language client abandoned a request that runs - the question, which waits for the user - when a message it runs in order arrived: $/progress,
    // as the server reports the work it does while the shell comes up. The question was taken off the console as the user was reading it, and the server was
    // answered nothing. The progress is sent before the answer to a request the shell made, which the shell has read once it has its answer.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AQuestionTheShellIsAsking_IsAnswered_WhenTheServersProgressArrivesMeanwhile()
    {
        var progressSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var languageServer = new InProcessLanguageServer(progressSent.Task);
        var keys = new WaitingKeys();
        var questions = new ReplServerQuestions(keys);
        var shell = new RDCore.CLI.RDCoreConsoleClientApp(
            Microsoft.Extensions.Options.Options.Create(new SdkAppOptions { Server = { MaxRestartAttempts = 0 } }),
            languageServer,
            new ConsoleUserInteraction(keys, new StringWriter()),
            Substitute.For<IReplConsole>(),
            new RDCore.SDK.Formatting.RubberduckIndenterSettingsReader(
                new MockFileSystem(), "C:\\", Substitute.For<RDCore.SDK.Formatting.ILegacyIndenterRegistry>(), isWindows: false),
            Substitute.For<RDCore.CLI.App.Configuration.IPlatformSettings>(),
            Substitute.For<ILiveSettings>(),
            Microsoft.Extensions.Options.Options.Create(new RDCore.CLI.AppOptions()),
            questions,
            NullLogger<RDCore.CLI.RDCoreConsoleClientApp>.Instance);
        var platform = Substitute.For<RDCore.SDK.Platform.IPlatformCompositionService>();
        platform.GetManifest().Returns(new RDCore.SDK.Platform.PlatformManifest { LangService = "RDCore.LanguageServer/RDCore.LanguageServer.exe" });
        using var services = new ServiceCollection().AddSingleton(platform).BuildServiceProvider();
        await shell.RunAsync(services, []).WaitAsync(Token);
        var server = await languageServer.Served.Task.WaitAsync(Token);

        // the server asks; the shell has said READY, and asks it at its console.
        var answering = server.Window.ShowMessageRequest(new ShowMessageRequestParams
        {
            Type = MessageType.Info,
            Message = "Import?",
            Actions = new Container<MessageActionItem>(new MessageActionItem { Title = "Import" }, new MessageActionItem { Title = "Not now" }),
        }, Token);
        await questions.Waiting.WaitAsync(Token);
        var asking = Task.Run(() => questions.AskWaiting(keys), Token);
        await keys.Reading.Task.WaitAsync(Token);

        // the server reports its work while the user reads the question, and answers a request of the shell's after it.
        var pinged = shell.SendRequestAsync<PingParams, int>(new PingParams(), Token);
        server.SendNotification("$/progress", new ProgressParams { Token = "work", Value = JToken.FromObject(new { kind = "begin", title = "Analyzing the workspace" }) });
        progressSent.SetResult();
        Assert.AreEqual(1, await pinged.WaitAsync(Token));

        // Enter chooses the last button.
        keys.Send(Key(ConsoleKey.Enter));
        await asking.WaitAsync(Token);
        Assert.AreEqual("Not now", (await answering.WaitAsync(Token))?.Title, "the question was answered, and its answer reached the server");
    }
}
