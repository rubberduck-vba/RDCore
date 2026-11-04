using RDCore.SDK.Client;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDCore.Tests.Client;

/// <summary>
/// The language server, started as an IDE starts it, with a client that is not OmniSharp's: one that writes and reads JSON-RPC itself, as HexIDE's does. The requests the
/// language server sends it are answered, and the answers reach what is waiting for them.
/// </summary>
[TestClass]
[TestCategory("Platform")]
public sealed class ClientWithoutOmniSharpTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(90);

    private static string AppHost(string name) => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? $"{name}.exe" : name);

    // the module a program is run as, as rdc.exe's shell sends one: its procedures, the module named apart.
    private const string Program = "Public Sub Main()\r\n    Debug.Print MsgBox(\"Continue?\", vbYesNo)\r\nEnd Sub\r\n";

    private const string ModuleText = $"Attribute VB_Name = \"Program\"\r\n{Program}";

    // what HexIDE's LSP client declares (HexIDE.Lsp/VBLspClient.InitializeAsync), as it writes it.
    private const string AsHexIdeDeclares = """
        {
            "textDocument": {
                "publishDiagnostics": { "relatedInformation": false },
                "hover": { "contentFormat": ["plaintext"] },
                "synchronization": { "didSave": true },
                "codeLens": {},
                "diagnostic": {},
                "documentSymbol": { "hierarchicalDocumentSymbolSupport": true },
                "semanticTokens": { "requests": {}, "tokenTypes": ["keyword", "variable"], "tokenModifiers": ["declaration"], "formats": ["relative"] }
            },
            "workspace": {
                "executeCommand": {},
                "workspaceFolders": true,
                "diagnostics": { "refreshSupport": true },
                "fileOperations": { "didCreate": true, "didRename": true, "didDelete": true }
            },
            "window": { "workDoneProgress": true }
        }
        """;

    private const string ClaimsDialogs = """
        { "expectedComponent": 1, "expected": { "languageServer": { "sessionExecute": { "isSupported": true }, "sessionInteraction": { "isSupported": true } } } }
        """;

    [Ignore("MG20261031: Flaky timing tests passed locally, failed twice on CI (Linux)")]
    [TestMethod]
    [DataRow("{}", DisplayName = "a client that declares nothing")]
    [DataRow(AsHexIdeDeclares, DisplayName = "a client that declares what HexIDE does")]
    [Timeout(180_000, CooperativeCancellation = true)]
    public async Task AClientThatShowsDialogs_AnswersTheMsgBoxOfAProgram_AndTheProgramCompletes(string capabilities)
    {
        await using var platform = await Platform.StartAsync();
        var rpc = platform.Rpc;

        var initialize = await rpc.RequestAsync(1, "initialize", platform.InitializeParams(capabilities));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());

        var handshake = await rpc.RequestAsync(2, RDCorePlatformHandshake.Method, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        Assert.IsNull(handshake["error"], handshake.ToJsonString());

        var executed = await rpc.RequestAsync(3, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = Program, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        platform.AssertTheProgramPrintedTheAnswer(executed);
        await platform.ShutdownAsync(4);
    }

    // the order HexIDE's add-in sent its messages in (C:\Dev\Claude\hexide-msgbox-wire-2026-10-30.txt): the document opened and its diagnostics asked for before the
    // platform handshake, which is sent without waiting for them; its requests numbered from 2, so that the handshake is 4, as the language server's msgBox is.
    [Ignore("MG20261031: Flaky timing tests passed locally, failed twice on CI (Linux)")]
    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    public async Task AClientInHexIdesOrder_AnswersTheMsgBoxOfAProgram_AndTheProgramCompletes()
    {
        await using var platform = await Platform.StartAsync();
        var rpc = platform.Rpc;

        var initialize = await rpc.RequestAsync(2, "initialize", platform.InitializeParams(AsHexIdeDeclares));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        await rpc.NotifyAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri, ["languageId"] = "vba", ["version"] = 1, ["text"] = ModuleText },
        });

        var diagnostics = rpc.RequestAsync(3, "textDocument/diagnostic", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri }, ["identifier"] = "rdcore" });
        var handshake = await rpc.RequestAsync(4, RDCorePlatformHandshake.Method, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        Assert.IsNull(handshake["error"], handshake.ToJsonString());

        var executed = await rpc.RequestAsync(5, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = ModuleText, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        platform.AssertTheProgramPrintedTheAnswer(executed);
        Assert.IsNull((await diagnostics)["error"]);
        StringAssert.Contains(WholeLogOf(platform.Root), "The client expects LanguageServer to provide \"LanguageServer: SessionExecute, SessionInteraction\"",
            "what the client claimed is in the language server's file");
        await platform.ShutdownAsync(6);
    }

    // a module of the workspace with enough code to take the language server a while to parse, bind and define.
    private static string OtherModule(int index)
        => $"Attribute VB_Name = \"Other{index}\"\r\nOption Explicit\r\n"
            + string.Concat(Enumerable.Range(0, 20).Select(procedure =>
                $"Public Function F{procedure}(ByVal a As Long, ByVal b As String) As Long\r\n    Dim i As Long, s As String\r\n    For i = 1 To a\r\n        s = s & b & CStr(i)\r\n        If Len(s) > 10 Then s = Left$(s, 5)\r\n    Next\r\n    F{procedure} = Len(s) + i\r\nEnd Function\r\n"));

    // the order HexIDE's add-in sent its messages in when the reply was lost (C:\Dev\Claude\hexide-msgbox-tap-2026-10-30.log): the program run as soon as the client
    // is connected, while the language server is still parsing the workspace and composing the session - which replaces the module just before it runs it.
    [Ignore("MG20261031: Flaky timing tests passed locally, failed twice on CI (Linux)")]
    [TestMethod]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task AProgramRunWhileTheWorkspaceIsComposed_AsksTheClient_AndCompletesWithItsAnswer()
    {
        await using var platform = await Platform.StartAsync(others: 60);
        var rpc = platform.Rpc;

        var initialize = await rpc.RequestAsync(2, "initialize", platform.InitializeParams(AsHexIdeDeclares));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        await rpc.NotifyAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri, ["languageId"] = "vba", ["version"] = 1, ["text"] = ModuleText },
        });

        var handshake = rpc.RequestAsync(3, RDCorePlatformHandshake.Method, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        var diagnostics = rpc.RequestAsync(4, "textDocument/diagnostic", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri }, ["identifier"] = "rdcore" });
        Assert.IsNull((await handshake)["error"]);

        var executed = await rpc.RequestAsync(5, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = ModuleText, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        platform.AssertTheProgramPrintedTheAnswer(executed);
        Assert.IsNull((await diagnostics)["error"]);
        StringAssert.Contains(WholeLogOf(platform.Root), "OmniSharp.Extensions.", "what OmniSharp's own container logs is in the language server's file as well");
        await platform.ShutdownAsync(6);
    }

    // a clean start and a clean shutdown log no fault: not the messages held until the client was initialized, nor the input read as it was completed. What
    // OmniSharp's own container logs reaches each server's file. A log is read once its server has exited, and its file log is flushed: the language server
    // exits once the parse server it started has.
    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    public async Task AServerStartedThenShutDown_LogsNoFault_AndWhatItsConnectionLogsReachesItsFile()
    {
        await using var platform = await Platform.StartAsync();
        var rpc = platform.Rpc;

        var initialize = await rpc.RequestAsync(1, "initialize", platform.InitializeParams(AsHexIdeDeclares));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        await platform.ShutdownAsync(2);

        foreach (var component in new[] { "RDCore.LanguageServer", "RDCore.ParseServer" })
        {
            var log = WholeLogOf(platform.Root, component);
            StringAssert.Contains(log, "OmniSharp.Extensions.", $"what OmniSharp's own container logs is in the file of {component}");
            Assert.DoesNotContain("Tried to send", log, component);
            Assert.DoesNotContain("Reading is not allowed", log, component);
        }
    }

    // the second byte tap (C:\Dev\Claude\hexide-msgbox-tap2-2026-10-30.log): the language server asks the client to refresh its diagnostics while the program waits to
    // run, and the client, as HexIDE does, answers and pulls the document's diagnostics again at once.
    [Ignore("MG20261031: Flaky timing tests passed locally, failed twice on CI (Linux)")]
    [TestMethod]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task AProgramRunWhileTheClientPullsItsDiagnosticsAgain_AsksTheClient_AndCompletesWithItsAnswer()
    {
        await using var platform = await Platform.StartAsync(others: 60);
        var rpc = platform.Rpc;
        Task<JsonObject>? pulledAgain = null;
        rpc.Answered = method =>
        {
            if (method == "workspace/diagnostic/refresh")
            {
                pulledAgain ??= rpc.RequestAsync(6, "textDocument/diagnostic", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri }, ["identifier"] = "rdcore" });
            }
        };

        var initialize = await rpc.RequestAsync(2, "initialize", platform.InitializeParams(AsHexIdeDeclares));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        var handshake = rpc.RequestAsync(3, RDCorePlatformHandshake.Method, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        await rpc.NotifyAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri, ["languageId"] = "vba", ["version"] = 1, ["text"] = ModuleText },
        });
        var diagnostics = rpc.RequestAsync(4, "textDocument/diagnostic", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri }, ["identifier"] = "rdcore" });
        Assert.IsNull((await handshake)["error"]);

        var executed = await rpc.RequestAsync(5, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = ModuleText, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        platform.AssertTheProgramPrintedTheAnswer(executed);
        Assert.IsNull((await diagnostics)["error"]);
        Assert.IsNotNull(pulledAgain, "the server asked for a refresh: " + string.Join(", ", platform.Asked));
        Assert.IsNull((await pulledAgain)["error"], "and the second pull is answered");
        await platform.ShutdownAsync(7);
    }

    // the third byte tap (C:\Dev\Claude\hexide-msgbox-tap3-2026-10-30.log), in its order to the byte: HexIDE pulls the document's diagnostics again before it answers
    // the refresh, and writes both at once; the program's msgBox is then the language server's request 5, which is the id of the client's execute, still running.
    [TestMethod]
    [Ignore("MG20261031: Flaky timing tests passed locally, failed twice on CI (Linux)")]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task AProgramRunWhileTheClientPullsAgainBeforeItAnswersTheRefresh_AsksTheClient_AndCompletesWithItsAnswer()
    {
        await using var platform = await Platform.StartAsync(others: 60);
        var rpc = platform.Rpc;
        Task<JsonObject>? pulledAgain = null;
        rpc.Before = method =>
        {
            if (method != "workspace/diagnostic/refresh" || pulledAgain is not null)
            {
                return null;
            }

            pulledAgain = rpc.Expect(6);
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 6,
                ["method"] = "textDocument/diagnostic",
                ["params"] = new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri }, ["identifier"] = "rdcore" },
            };
        };

        var initialize = await rpc.RequestAsync(2, "initialize", platform.InitializeParams(AsHexIdeDeclares));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        await rpc.NotifyAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri, ["languageId"] = "vba", ["version"] = 1, ["text"] = ModuleText },
        });
        var handshake = rpc.RequestAsync(3, RDCorePlatformHandshake.Method, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        var diagnostics = rpc.RequestAsync(4, "textDocument/diagnostic", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri }, ["identifier"] = "rdcore" });
        Assert.IsNull((await handshake)["error"]);

        var executed = await rpc.RequestAsync(5, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = ModuleText, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        platform.AssertTheProgramPrintedTheAnswer(executed);
        Assert.IsNull((await diagnostics)["error"]);
        Assert.IsNotNull(pulledAgain, "the server asked for a refresh: " + string.Join(", ", platform.Asked));
        Assert.IsNull((await pulledAgain.WaitAsync(Bound))["error"], "and the second pull is answered");
        await platform.ShutdownAsync(7);
    }

    // overnight #39/#75, what hung HexIDE: its probe answered a vbOKOnly box with vbYes, and the host showed a native dialog nobody saw. A client that shows dialogs
    // is the one that answers, and an answer the dialog cannot have fails the run, untrappably, instead; one it can have is the program's.
    private const string OkOnly = "Public Sub Main()\r\n    On Error Resume Next\r\n    Debug.Print MsgBox(\"from the add-in probe\")\r\n    Debug.Print \"after\"\r\nEnd Sub\r\n";

    [TestMethod]
    [Ignore("MG20261031: Flaky timing tests passed locally, failed twice on CI (Linux)")]
    [DataRow(6, true, ExecutionOutcome.RuntimeError, "VBR50002", DisplayName = "answered with vbYes, which a vbOKOnly box has no button for")]
    [DataRow(1, true, ExecutionOutcome.Completed, "", DisplayName = "answered with vbOK")]
    [DataRow(0, false, ExecutionOutcome.RuntimeError, "VBR50001", DisplayName = "declined: not handled")]
    [Timeout(180_000, CooperativeCancellation = true)]
    public async Task AClientThatShowsDialogs_IsTheOneThatAnswers_AndNoDialogOfTheHostWaits(int result, bool handled, ExecutionOutcome outcome, string code)
    {
        await using var platform = await Platform.StartAsync();
        platform.MessageBoxAnswer = () => new JsonObject { ["handled"] = handled, ["result"] = result };
        var rpc = platform.Rpc;

        var initialize = await rpc.RequestAsync(1, "initialize", platform.InitializeParams(AsHexIdeDeclares));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        Assert.IsNull((await rpc.RequestAsync(2, RDCorePlatformHandshake.Method, JsonNode.Parse(ClaimsDialogs)!.AsObject()))["error"]);

        var executed = await rpc.RequestAsync(3, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = OkOnly, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        var told = $"{executed.ToJsonString()}\n{LogOf(platform.Root)}";
        Assert.IsNull(executed["error"], told);
        Assert.AreEqual((int)outcome, executed["result"]!["outcome"]!.GetValue<int>(), told);
        var output = executed["result"]!["output"]!.AsArray().Select(line => line!.GetValue<string>().Trim()).ToArray();
        if (outcome is ExecutionOutcome.Completed)
        {
            CollectionAssert.AreEqual(new[] { "1", "after" }, output, told);
        }
        else
        {
            Assert.AreEqual(code, executed["result"]!["errorCode"]!.GetValue<string>(), told);
            Assert.DoesNotContain("after", output, "untrappable: On Error Resume Next does not go on past it");
        }

        await platform.ShutdownAsync(4);
    }

    // the platform as a deployment has it, with a workspace of one module, and the language server started from it as an IDE starts it.
    private sealed class Platform : IAsyncDisposable
    {
        private readonly string _root;
        private readonly Process _process;
        private readonly NamedPipeClientStream _pipe;

        private Platform(string root, string workspace, Process process, NamedPipeClientStream pipe)
        {
            _root = root;
            Workspace = new Uri(workspace).AbsoluteUri;
            ModuleUri = new Uri(Path.Combine(workspace, "Program.bas")).AbsoluteUri;
            _process = process;
            _pipe = pipe;
            Rpc = new JsonRpc(pipe, (method, _) =>
            {
                Asked.Enqueue(method);
                return method == RDCorePlatformProtocol.SessionMessageBox ? MessageBoxAnswer() : null;
            });
        }

        public JsonRpc Rpc { get; }

        /// <summary>What the client answers a MsgBox with: vbYes, by default.</summary>
        public Func<JsonObject> MessageBoxAnswer { get; set; } = () => new JsonObject { ["handled"] = true, ["result"] = 6 };

        public string Root => _root;

        public string Workspace { get; }

        public string ModuleUri { get; }

        public ConcurrentQueue<string> Asked { get; } = [];

        // the workspace has `others` modules besides Program, which the language server parses and composes the session of before it runs anything.
        public static async Task<Platform> StartAsync(int others = 0)
        {
            var root = Path.Combine(Path.GetTempPath(), "RDCore", "raw-client", Guid.NewGuid().ToString("N"));
            var workspace = Path.Combine(root, "Workspace");
            Directory.CreateDirectory(workspace);
            File.WriteAllText(Path.Combine(workspace, "Program.bas"), ModuleText);
            for (var index = 0; index < others; index++)
            {
                File.WriteAllText(Path.Combine(workspace, $"Other{index}.bas"), OtherModule(index));
            }

            File.WriteAllText(Path.Combine(root, PlatformManifest.FileName), JsonSerializer.Serialize(new PlatformManifest
            {
                PlatformVersion = "0.1",
                ExtensionsDirectory = "Extensions",
                LangService = AppHost("RDCore.LanguageServer"),
                ParseServer = AppHost("RDCore.ParseServer"),
                HostService = AppHost("rdc"),
            }));

            var launch = RDCoreLanguageServerLaunch.Prepare(new RDCoreLanguageServerLaunchOptions
            {
                PlatformRoot = root,
                Workspace = new Uri(workspace).AbsoluteUri,
                PipeName = $"rdcore-raw-client-{Environment.ProcessId}-{Guid.NewGuid():N}",
            });
            var start = launch.ToProcessStartInfo();
            start.Environment[PlatformEnvironment.UserDataEnvironmentVariable] = Path.Combine(root, "User");

            var process = Process.Start(start)!;
            var pipe = new NamedPipeClientStream(".", launch.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync((int)Bound.TotalMilliseconds);
            return new Platform(root, workspace, process, pipe);
        }

        public JsonObject InitializeParams(string capabilities) => new()
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = Workspace,
            ["workspaceFolders"] = new JsonArray(new JsonObject { ["uri"] = Workspace, ["name"] = "Workspace" }),
            ["trace"] = "off",
            ["locale"] = "en-US",
            ["capabilities"] = JsonNode.Parse(capabilities),
        };

        public void AssertTheProgramPrintedTheAnswer(JsonObject executed)
        {
            var told = $"{executed.ToJsonString()}\nasked: {string.Join(", ", Asked)}\nlogged by the server:\n{string.Join("\n", Rpc.Logged)}\n{LogOf(_root)}";
            Assert.IsNull(executed["error"], told);
            Assert.Contains(RDCorePlatformProtocol.SessionMessageBox, Asked, "the client was asked: " + told);
            Assert.AreEqual((int)ExecutionOutcome.Completed, executed["result"]!["outcome"]!.GetValue<int>(), told);
            CollectionAssert.Contains(executed["result"]!["output"]!.AsArray().Select(line => line!.GetValue<string>().Trim()).ToArray(), "6",
                "the program printed what the client answered: " + told);
        }

        public async Task ShutdownAsync(int id)
        {
            _ = await Rpc.RequestAsync(id, "shutdown", new JsonObject());
            await Rpc.NotifyAsync("exit", new JsonObject());
            await _process.WaitForExitAsync().WaitAsync(Bound);
        }

        public async ValueTask DisposeAsync()
        {
            await Rpc.DisposeAsync();
            await _pipe.DisposeAsync();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            _process.Dispose();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // a log may still be held as the processes go: the folder is a temporary one.
            }
        }
    }

    // the end of the language server's log, which says what it did with what it was sent.
    private static string LogOf(string root) => string.Join('\n', WholeLogOf(root).Split('\n').TakeLast(60));

    private static string WholeLogOf(string root, string component = "RDCore.LanguageServer")
    {
        var logs = Directory.Exists(root) ? Directory.EnumerateFiles(root, $"{component}*.log", SearchOption.AllDirectories).ToArray() : [];
        if (logs.Length == 0)
        {
            return $"(no {component} log)";
        }

        using var stream = new FileStream(logs[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream).ReadToEnd();
    }

    // JSON-RPC 2.0 over the pipe, as LSP frames it, written by hand as a client that is not OmniSharp's writes it: both ways at once, a request answered whenever its
    // answer comes, and the server's requests answered as they arrive.
    private sealed class JsonRpc : IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly Func<string, JsonNode?, JsonNode?> _answer;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
        private readonly SemaphoreSlim _writing = new(1, 1);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _reading;

        public JsonRpc(Stream stream, Func<string, JsonNode?, JsonNode?> answer)
        {
            _stream = stream;
            _answer = answer;
            _reading = Task.Run(ReadAllAsync);
        }

        /// <summary>What the server logged to the client (<c>window/logMessage</c>).</summary>
        public ConcurrentQueue<string> Logged { get; } = [];

        /// <summary>What the client does once it has answered a request of the server, given its method.</summary>
        public Action<string>? Answered { get; set; }

        /// <summary>A message the client writes before it answers a request of the server, given its method, in the same write as the answer; or none.</summary>
        public Func<string, JsonObject?>? Before { get; set; }

        /// <summary>The answer to a request with this id, which the client writes itself.</summary>
        public Task<JsonObject> Expect(int id)
        {
            var answered = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = answered;
            return answered.Task;
        }

        public Task NotifyAsync(string method, JsonObject parameters) => WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

        public async Task<JsonObject> RequestAsync(int id, string method, JsonObject parameters)
        {
            var answered = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = answered;
            await WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
            return await answered.Task.WaitAsync(Bound);
        }

        private async Task ReadAllAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var message = await ReadAsync();
                    if (message["method"] is { } called)
                    {
                        var method = called.GetValue<string>();
                        if (message["id"] is { } asked)
                        {
                            // a request from the server is answered: what the client does with it, or nothing.
                            var answer = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = asked.DeepClone(), ["result"] = _answer(method, message["params"])?.DeepClone() };
                            await WriteAsync(Before?.Invoke(method) is { } first ? [first, answer] : [answer]);
                            Answered?.Invoke(method);
                        }
                        else if (method == "window/logMessage")
                        {
                            Logged.Enqueue(message["params"]?["message"]?.ToString() ?? string.Empty);
                        }
                    }
                    else if (message["id"] is { } id && id.GetValueKind() == JsonValueKind.Number && _pending.TryRemove(id.GetValue<int>(), out var answered))
                    {
                        answered.TrySetResult(message);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or EndOfStreamException or ObjectDisposedException or OperationCanceledException)
            {
                foreach (var pending in _pending.Values)
                {
                    pending.TrySetException(exception);
                }
            }
        }

        private Task WriteAsync(JsonObject message) => WriteAsync([message]);

        // the messages framed one after the other, and written at once.
        private async Task WriteAsync(JsonObject[] messages)
        {
            var frames = new MemoryStream();
            foreach (var message in messages)
            {
                var body = Encoding.UTF8.GetBytes(message.ToJsonString());
                frames.Write(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
                frames.Write(body);
            }

            await _writing.WaitAsync();
            try
            {
                await _stream.WriteAsync(frames.ToArray());
                await _stream.FlushAsync();
            }
            finally
            {
                _writing.Release();
            }
        }

        private async Task<JsonObject> ReadAsync()
        {
            var header = new StringBuilder();
            var one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await _stream.ReadAsync(one, _stop.Token) == 0)
                {
                    throw new EndOfStreamException("the server closed the pipe");
                }

                header.Append((char)one[0]);
            }

            var length = int.Parse(header.ToString().Split("Content-Length:")[1].Split("\r\n")[0].Trim());
            var body = new byte[length];
            await _stream.ReadExactlyAsync(body, _stop.Token);
            return JsonNode.Parse(body)!.AsObject();
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _reading.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                // the pipe is disposed after, which ends the read.
            }
        }
    }
}
