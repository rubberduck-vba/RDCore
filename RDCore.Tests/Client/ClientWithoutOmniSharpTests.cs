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
    // what ends a wait is what it waits for, or the language server exiting or closing its pipe: never how long it took, which is the machine's. A wait that ends
    // on neither has hung, and fails as hung - a safety net, never the way a test is expected to end.
    private static readonly TimeSpan Hung = TimeSpan.FromMinutes(10);

    private static async Task<T> Unhung<T>(Task<T> waited, string what)
    {
        try
        {
            return await waited.WaitAsync(Hung);
        }
        catch (TimeoutException)
        {
            throw new AssertFailedException($"hung: {what} - nothing came in {Hung.TotalMinutes} minutes, and the language server neither exited nor closed its pipe.");
        }
    }

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

    [TestMethod]
    [DataRow("{}", DisplayName = "a client that declares nothing")]
    [DataRow(AsHexIdeDeclares, DisplayName = "a client that declares what HexIDE does")]
    public async Task AClientThatShowsDialogs_AnswersTheMsgBoxOfAProgram_AndTheProgramCompletes(string capabilities)
    {
        await using var platform = await Platform.StartAsync();
        var rpc = platform.Rpc;

        var initialize = await rpc.RequestAsync(1, "initialize", platform.InitializeParams(capabilities));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());

        var handshake = await rpc.RequestAsync(2, RDCorePlatformProtocol.PlatformInitialize, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        Assert.IsNull(handshake["error"], handshake.ToJsonString());

        var executed = await rpc.RequestAsync(3, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = Program, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        platform.AssertTheProgramPrintedTheAnswer(executed);
        await platform.ShutdownAsync(4);
    }

    // the client's own module - rdc's shell's program, which runs `? 1 + 1` - is run in a workspace whose project does not list it, as a module of the session: in a
    // workspace whose project file can't be read, which loads no project (the client is told why), and in one whose project has other modules. In the first, the
    // run's module is addressed under the workspace's root although no project was read, and the host composes a session with no modules to run it in.
    [TestMethod]
    [DataRow(UnreadableProjectFile, DisplayName = "a project file that can't be read")]
    [DataRow(AProjectOfOtherModules, DisplayName = "a project that does not list it")]
    public async Task TheClientsOwnModule_IsRun_InAWorkspaceWhoseProjectDoesNotHaveIt(string projectFile)
    {
        await using var platform = await Platform.StartAsync(others: 1, projectFile: projectFile);
        var executed = await RunAsync(platform, "Program", sessionModule: true);

        var told = $"{executed.ToJsonString()}\nlogged by the server:\n{string.Join("\n", platform.Rpc.Logged)}\n{LogOf(platform.Root)}";
        Assert.IsNull(executed["error"], told);
        Assert.AreEqual((int)ExecutionOutcome.Completed, executed["result"]!["outcome"]!.GetValue<int>(), told);
        CollectionAssert.AreEqual(new[] { "2" }, executed["result"]!["output"]!.AsArray().Select(line => line!.GetValue<string>().Trim()).ToArray(), told);
        await platform.ShutdownAsync(4);
    }

    // a module the project does not have, and that the client does not say is its own, is not found: a name mistyped or a module that is no longer the project's
    // is not made a module of the session.
    [TestMethod]
    [DataRow(UnreadableProjectFile, DisplayName = "a project file that can't be read")]
    [DataRow(AProjectOfOtherModules, DisplayName = "a project that does not list it")]
    public async Task AModuleTheProjectDoesNotHave_IsNotFound_UnlessItIsTheClientsOwn(string projectFile)
    {
        await using var platform = await Platform.StartAsync(others: 1, projectFile: projectFile);
        var executed = await RunAsync(platform, "Progam", sessionModule: false);

        var told = $"{executed.ToJsonString()}\nlogged by the server:\n{string.Join("\n", platform.Rpc.Logged)}\n{LogOf(platform.Root)}";
        Assert.IsNull(executed["error"], told);
        Assert.AreEqual((int)ExecutionOutcome.NotFound, executed["result"]!["outcome"]!.GetValue<int>(), told);
        StringAssert.Contains(executed["result"]!["errorMessage"]!.GetValue<string>(), "'Progam'", told);
        await platform.ShutdownAsync(4);
    }

    // a project file of a newer format loads all the same, in the language server and in the environment host the language server composes the session of: a
    // module it lists is run under the constant it defines. Had the format been refused, the folder would have been a project of its source files, with no
    // constant, and 0 printed.
    [TestMethod]
    public async Task AModuleOfAProjectFileOfANewerFormat_IsRun_UnderTheConstantsItDefines()
    {
        await using var platform = await Platform.StartAsync(others: 1, projectFile: AProjectFileOfANewerFormat);
        var executed = await RunAsync(platform, "Other0", sessionModule: false,
            source: "Public Sub Main()\r\n#If NEWER Then\r\n    Debug.Print 1 + 1\r\n#Else\r\n    Debug.Print 0\r\n#End If\r\nEnd Sub\r\n");

        var told = $"{executed.ToJsonString()}\nlogged by the server:\n{string.Join("\n", platform.Rpc.Logged)}\n{LogOf(platform.Root)}";
        Assert.IsNull(executed["error"], told);
        Assert.AreEqual((int)ExecutionOutcome.Completed, executed["result"]!["outcome"]!.GetValue<int>(), told);
        CollectionAssert.AreEqual(new[] { "2" }, executed["result"]!["output"]!.AsArray().Select(line => line!.GetValue<string>().Trim()).ToArray(), told);
        await platform.ShutdownAsync(4);
    }

    // the environment host is started in its own folder, and the session it composes for the workspace starts in the workspace root: a relative path is a file
    // of the workspace.
    [TestMethod]
    public async Task AProgram_StartsInTheWorkspaceRoot_WhereARelativePathFindsItsFiles()
    {
        await using var platform = await Platform.StartAsync();
        var executed = await RunAsync(platform, "Program", sessionModule: true,
            source: "Public Sub Main()\r\n    Debug.Print CurDir\r\n    Debug.Print Dir(\"Program.bas\")\r\nEnd Sub\r\n");

        var told = $"{executed.ToJsonString()}\nlogged by the server:\n{string.Join("\n", platform.Rpc.Logged)}\n{LogOf(platform.Root)}";
        Assert.IsNull(executed["error"], told);
        Assert.AreEqual((int)ExecutionOutcome.Completed, executed["result"]!["outcome"]!.GetValue<int>(), told);
        // CurDir is the workspace's real path: on macOS the temporary folder is under /var, a link to /private/var.
        var root = RDCore.SDK.Platform.PlatformRootConfinement.RealPath(new Uri(platform.Workspace).LocalPath);
        CollectionAssert.AreEqual(new[] { RDCore.SDK.Workspace.WorkspacePaths.Normalized(root), "Program.bas" },
            executed["result"]!["output"]!.AsArray().Select(line => line!.GetValue<string>().Trim()).ToArray(), told);
        await platform.ShutdownAsync(4);
    }

    // a client that opens no folder has no workspace to run anything in: what it runs is not found, and is no internal error of the language server.
    [TestMethod]
    public async Task AClientThatOpensNoFolder_IsToldThereIsNoWorkspaceToRunIn()
    {
        await using var platform = await Platform.StartAsync();
        var rpc = platform.Rpc;
        var initialize = await rpc.RequestAsync(1, "initialize", platform.InitializeParams("{}", opensAFolder: false));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        var handshake = await rpc.RequestAsync(2, RDCorePlatformProtocol.PlatformInitialize, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        Assert.IsNull(handshake["error"], handshake.ToJsonString());

        var executed = await rpc.RequestAsync(3, RDCorePlatformProtocol.SessionExecute, new JsonObject
        {
            ["source"] = "Public Sub Main()\r\n    Debug.Print 1 + 1\r\nEnd Sub\r\n",
            ["moduleName"] = "Program",
            ["entryPoint"] = "Main",
            ["sessionModule"] = true,
        });

        var told = $"{executed.ToJsonString()}\nlogged by the server:\n{string.Join("\n", rpc.Logged)}\n{LogOf(platform.Root)}";
        Assert.IsNull(executed["error"], told);
        Assert.AreEqual((int)ExecutionOutcome.NotFound, executed["result"]!["outcome"]!.GetValue<int>(), told);
        StringAssert.Contains(executed["result"]!["errorMessage"]!.GetValue<string>(), "no workspace", told);
        await platform.ShutdownAsync(4);
    }

    private const string UnreadableProjectFile = "{ \"Version\": \"1.0\", \"ProjectInfo\": { \"Name\": \"Broken\" ";
    private const string AProjectOfOtherModules = "{ \"ProjectInfo\": { \"Name\": \"Other\", \"Modules\": [ { \"RelativeUri\": \"Other0.bas\" } ] } }";
    private const string AProjectFileOfANewerFormat =
        "{ \"Version\": \"100.0.0\", \"ProjectInfo\": { \"Name\": \"Newer\", \"Modules\": [ { \"RelativeUri\": \"Other0.bas\" } ], \"PrecompilerConstants\": { \"NEWER\": \"1\" } } }";

    // the workspace is initialized and handshaken, and `Debug.Print 1 + 1`, or the source given, is run as Main of the module.
    private static async Task<JsonObject> RunAsync(Platform platform, string moduleName, bool sessionModule, string? source = null)
    {
        var rpc = platform.Rpc;
        var initialize = await rpc.RequestAsync(1, "initialize", platform.InitializeParams("{}"));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());

        var handshake = await rpc.RequestAsync(2, RDCorePlatformProtocol.PlatformInitialize, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        Assert.IsNull(handshake["error"], handshake.ToJsonString());

        return await rpc.RequestAsync(3, RDCorePlatformProtocol.SessionExecute, new JsonObject
        {
            ["source"] = source ?? "Public Sub Main()\r\n    Debug.Print 1 + 1\r\nEnd Sub\r\n",
            ["moduleName"] = moduleName,
            ["entryPoint"] = "Main",
            ["sessionModule"] = sessionModule,
        });
    }

    // the order HexIDE's add-in sent its messages in (C:\Dev\Claude\hexide-msgbox-wire-2026-10-30.txt): the document opened and its diagnostics asked for before the
    // platform handshake, which is sent without waiting for them; its requests numbered from 2, so that the handshake is 4, as the language server's msgBox is.
    [TestMethod]
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
        var handshake = await rpc.RequestAsync(4, RDCorePlatformProtocol.PlatformInitialize, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        Assert.IsNull(handshake["error"], handshake.ToJsonString());

        var executed = await rpc.RequestAsync(5, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = ModuleText, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        platform.AssertTheProgramPrintedTheAnswer(executed);
        Assert.IsNull((await diagnostics)["error"], (await diagnostics).ToJsonString());
        await platform.ShutdownAsync(6);

        // the log is read once the language server has exited, from every file it wrote: a server that runs past midnight writes the line to the next day's.
        StringAssert.Contains(WholeLogOf(platform.Root), "The client expects LanguageServer to provide \"LanguageServer: SessionExecute, SessionInteraction\"",
            "what the client claimed is in the language server's file");
    }

    // a module of the workspace with enough code to take the language server a while to parse, bind and define.
    private static string OtherModule(int index)
        => $"Attribute VB_Name = \"Other{index}\"\r\nOption Explicit\r\n"
            + string.Concat(Enumerable.Range(0, 20).Select(procedure =>
                $"Public Function F{procedure}(ByVal a As Long, ByVal b As String) As Long\r\n    Dim i As Long, s As String\r\n    For i = 1 To a\r\n        s = s & b & CStr(i)\r\n        If Len(s) > 10 Then s = Left$(s, 5)\r\n    Next\r\n    F{procedure} = Len(s) + i\r\nEnd Function\r\n"));

    // the order HexIDE's add-in sent its messages in when the reply was lost (C:\Dev\Claude\hexide-msgbox-tap-2026-10-30.log): the program run as soon as the client
    // is connected, while the language server is still parsing the workspace and composing the session - which replaces the module just before it runs it.
    [TestMethod]
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

        var handshake = rpc.RequestAsync(3, RDCorePlatformProtocol.PlatformInitialize, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        var diagnostics = rpc.RequestAsync(4, "textDocument/diagnostic", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri }, ["identifier"] = "rdcore" });
        Assert.IsNull((await handshake)["error"], (await handshake).ToJsonString());

        var executed = await rpc.RequestAsync(5, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = ModuleText, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        platform.AssertTheProgramPrintedTheAnswer(executed);
        Assert.IsNull((await diagnostics)["error"], (await diagnostics).ToJsonString());
        await platform.ShutdownAsync(6);
    }

    // a clean start and a clean shutdown log no fault: not the messages held until the client was initialized, nor the input read as it was completed. What
    // OmniSharp's own container logs reaches each server's file: the platform handshake, which each server's container logs as it answers it - the language
    // server's, of this client, and the parse server's, of the language server. The container logs what the client's trace asks for, and warnings whatever it
    // asks for: a client that traces messages is told of the handshake. A program run has both servers up; a log is read once its server has exited, and its
    // file log is flushed: the language server exits once the parse server it started has.
    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task AServerStartedThenShutDown_LogsNoFault_AndWhatItsConnectionLogsReachesItsFile()
    {
        await using var platform = await Platform.StartAsync();
        var rpc = platform.Rpc;
        var parameters = platform.InitializeParams(AsHexIdeDeclares);
        parameters["trace"] = "messages";

        var initialize = await rpc.RequestAsync(1, "initialize", parameters);
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        var handshake = await rpc.RequestAsync(2, RDCorePlatformProtocol.PlatformInitialize, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        Assert.IsNull(handshake["error"], handshake.ToJsonString());
        var executed = await rpc.RequestAsync(3, RDCorePlatformProtocol.SessionExecute,
            new JsonObject { ["source"] = "Public Sub Main()\r\n    Debug.Print 6\r\nEnd Sub\r\n", ["moduleName"] = "Program", ["entryPoint"] = "Main" });
        Assert.AreEqual((int)ExecutionOutcome.Completed, executed["result"]?["outcome"]?.GetValue<int>(), executed.ToJsonString());
        await platform.ShutdownAsync(4);

        foreach (var component in new[] { "RDCore.LanguageServer", "RDCore.ParseServer" })
        {
            var log = WholeLogOf(platform.Root, component);
            StringAssert.Contains(log, "\"OmniSharp.Extensions.JsonRpc.IJsonRpcHandler\": \"Platform handshake: ", $"what OmniSharp's own container logs is in the file of {component}");
            Assert.DoesNotContain("Tried to send", log, component);
            Assert.DoesNotContain("Reading is not allowed", log, component);
        }
    }

    // the second byte tap (C:\Dev\Claude\hexide-msgbox-tap2-2026-10-30.log): the language server asks the client to refresh its diagnostics while the program waits to
    // run, and the client, as HexIDE does, answers and pulls the document's diagnostics again at once. The refresh comes of the document's open and the
    // workspace's bring-up, not of the run, so the client holds it and the program's MsgBox for each other: the document is pulled again while the program
    // waits.
    [TestMethod]
    public async Task AProgramRunWhileTheClientPullsItsDiagnosticsAgain_AsksTheClient_AndCompletesWithItsAnswer()
    {
        await using var platform = await Platform.StartAsync(others: 60);
        var rpc = platform.Rpc;
        var refreshed = new TaskCompletionSource<Task<JsonObject>>(TaskCreationOptions.RunContinuationsAsynchronously);
        PullsAgainWhileTheProgramWaits(rpc, refreshed.Task);
        rpc.Answered = (method, held) =>
        {
            if (method == DiagnosticRefresh && held)
            {
                _ = PullAgainAsync();
            }
        };

        async Task PullAgainAsync()
        {
            try
            {
                refreshed.TrySetResult(await rpc.WriteRequestAsync(6, "textDocument/diagnostic",
                    new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri }, ["identifier"] = "rdcore" }));
            }
            catch (Exception exception)
            {
                refreshed.TrySetException(exception);
            }
        }

        var initialize = await rpc.RequestAsync(2, "initialize", platform.InitializeParams(AsHexIdeDeclares));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        var handshake = rpc.RequestAsync(3, RDCorePlatformProtocol.PlatformInitialize, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        await rpc.NotifyAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri, ["languageId"] = "vba", ["version"] = 1, ["text"] = ModuleText },
        });
        var diagnostics = rpc.RequestAsync(4, "textDocument/diagnostic", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri }, ["identifier"] = "rdcore" });
        Assert.IsNull((await handshake)["error"], (await handshake).ToJsonString());

        var executed = await rpc.RequestAsync(5, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = ModuleText, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        platform.AssertTheProgramPrintedTheAnswer(executed);
        AssertAnsweredOrDeclinedForTheDialog(await diagnostics);
        var pulledAgain = await Unhung(refreshed.Task, "the server's refresh: asked " + string.Join(", ", platform.Asked));
        AssertAnsweredOrDeclinedForTheDialog(await pulledAgain, "the second pull");
        await platform.ShutdownAsync(7);
    }

    // a pull that comes while the program's MsgBox is open is declined, ServerCancelled and not to be retriggered, and the client is asked to pull again once
    // the box is answered; one that comes before or after it is answered. Which it is, is the order the pull and the box come in, which these tests don't force.
    private static void AssertAnsweredOrDeclinedForTheDialog(JsonObject reply, string pull = "the pull")
    {
        if (reply["error"] is not { } error)
        {
            Assert.IsNotNull(reply["result"], $"{pull} is answered: {reply.ToJsonString()}");
            return;
        }

        Assert.AreEqual(-32802, error["code"]!.GetValue<int>(), $"{pull} is declined for the dialog, ServerCancelled, or answered: {reply.ToJsonString()}");
        Assert.IsFalse(error["data"]!["retriggerRequest"]!.GetValue<bool>(), $"and not to be retriggered: {reply.ToJsonString()}");
    }

    // the third byte tap (C:\Dev\Claude\hexide-msgbox-tap3-2026-10-30.log), in its order to the byte: HexIDE pulls the document's diagnostics again before it answers
    // the refresh, and writes both at once; the program's msgBox is then the language server's request 5, which is the id of the client's execute, still running.
    // The client holds the refresh and the MsgBox for each other, as above: the pull and the refresh's answer are written while the program waits.
    [TestMethod]
    public async Task AProgramRunWhileTheClientPullsAgainBeforeItAnswersTheRefresh_AsksTheClient_AndCompletesWithItsAnswer()
    {
        await using var platform = await Platform.StartAsync(others: 60);
        var rpc = platform.Rpc;
        var refreshed = new TaskCompletionSource<Task<JsonObject>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PullsAgainWhileTheProgramWaits(rpc, written.Task);
        rpc.Answered = (method, held) =>
        {
            if (method == DiagnosticRefresh && held)
            {
                written.TrySetResult();
            }
        };
        rpc.Before = (method, held) =>
        {
            if (method != DiagnosticRefresh || !held)
            {
                return null;
            }

            refreshed.TrySetResult(rpc.Expect(6));
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
        var handshake = rpc.RequestAsync(3, RDCorePlatformProtocol.PlatformInitialize, JsonNode.Parse(ClaimsDialogs)!.AsObject());
        var diagnostics = rpc.RequestAsync(4, "textDocument/diagnostic", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri }, ["identifier"] = "rdcore" });
        Assert.IsNull((await handshake)["error"], (await handshake).ToJsonString());

        var executed = await rpc.RequestAsync(5, RDCorePlatformProtocol.SessionExecute, new JsonObject { ["source"] = ModuleText, ["moduleName"] = "Program", ["entryPoint"] = "Main" });

        platform.AssertTheProgramPrintedTheAnswer(executed);
        AssertAnsweredOrDeclinedForTheDialog(await diagnostics);
        var pulledAgain = await Unhung(refreshed.Task, "the server's refresh: asked " + string.Join(", ", platform.Asked));
        AssertAnsweredOrDeclinedForTheDialog(await pulledAgain, "the second pull");
        await platform.ShutdownAsync(7);
    }

    private const string DiagnosticRefresh = "workspace/diagnostic/refresh";

    // the client answers the server's first diagnostics refresh once the program has asked its MsgBox, and the MsgBox once what the refresh makes it write
    // is written: the document is pulled again while the program waits, whichever the server asks first. The pull is declined for the dialog, or answered
    // when it comes after the box's answer is read.
    private static void PullsAgainWhileTheProgramWaits(JsonRpc rpc, Task written)
    {
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshHeld = false;
        rpc.Hold = method =>
        {
            if (method == RDCorePlatformProtocol.SessionMessageBox)
            {
                asked.TrySetResult();
                return written;
            }

            if (method != DiagnosticRefresh || refreshHeld)
            {
                // no task is what says the request is not held (Hold): the client answers it at once.
#pragma warning disable VSTHRD114
                return null;
#pragma warning restore VSTHRD114
            }

            refreshHeld = true;
            return asked.Task;
        };
    }

    // overnight #39/#75, what hung HexIDE: its probe answered a vbOKOnly box with vbYes, and the host showed a native dialog nobody saw. A client that shows dialogs
    // is the one that answers, and an answer the dialog cannot have fails the run, untrappably, instead; one it can have is the program's.
    private const string OkOnly = "Public Sub Main()\r\n    On Error Resume Next\r\n    Debug.Print MsgBox(\"from the add-in probe\")\r\n    Debug.Print \"after\"\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow(6, true, ExecutionOutcome.RuntimeError, "VBR50002", DisplayName = "answered with vbYes, which a vbOKOnly box has no button for")]
    [DataRow(1, true, ExecutionOutcome.Completed, "", DisplayName = "answered with vbOK")]
    [DataRow(0, false, ExecutionOutcome.RuntimeError, "VBR50001", DisplayName = "declined: not handled")]
    public async Task AClientThatShowsDialogs_IsTheOneThatAnswers_AndNoDialogOfTheHostWaits(int result, bool handled, ExecutionOutcome outcome, string code)
    {
        await using var platform = await Platform.StartAsync();
        platform.MessageBoxAnswer = () => new JsonObject { ["handled"] = handled, ["result"] = result };
        var rpc = platform.Rpc;

        var initialize = await rpc.RequestAsync(1, "initialize", platform.InitializeParams(AsHexIdeDeclares));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        Assert.IsNull((await rpc.RequestAsync(2, RDCorePlatformProtocol.PlatformInitialize, JsonNode.Parse(ClaimsDialogs)!.AsObject()))["error"]);

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

    // a pause, as the shell's Ctrl+Break and the debug adapter send it, is handled while the program it pauses runs - in the language server and in the host - and
    // stops a program that never ends.
    private const string Looping = "Public Sub Main()\r\n    Dim Total As Long\r\n    Do While True\r\n        Total = Total + 1\r\n    Loop\r\nEnd Sub\r\n";

    private const string ClaimsDebugging = """
        { "expectedComponent": 1, "expected": { "languageServer": { "sessionExecute": { "isSupported": true }, "programDebugging": { "isSupported": true } } } }
        """;

    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task AProgramRunUnderADebugger_IsPausedWhileItRuns_ThenEnded()
    {
        await using var platform = await Platform.StartAsync();
        var rpc = platform.Rpc;

        var initialize = await rpc.RequestAsync(1, "initialize", platform.InitializeParams(AsHexIdeDeclares));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        Assert.IsNull((await rpc.RequestAsync(2, RDCorePlatformProtocol.PlatformInitialize, JsonNode.Parse(ClaimsDebugging)!.AsObject()))["error"]);

        var running = rpc.RequestAsync(3, RDCorePlatformProtocol.SessionExecute,
            new JsonObject { ["source"] = Looping, ["moduleName"] = "Program", ["entryPoint"] = "Main", ["debug"] = true });

        // a pause that arrives before the program runs acts on nothing, and is answered so: it is sent again until it stops the program. Until one acts, the
        // program runs; the pause that acts and the run it suspends are answered at once, in either order.
        var id = 4;
        JsonNode paused;
        bool acted;
        do
        {
            paused = await rpc.RequestAsync(id++, RDCorePlatformProtocol.SessionDebugPause, new JsonObject());
            Assert.IsNull(paused["error"], $"{paused.ToJsonString()}\n{LogOf(platform.Root)}");
            acted = paused["result"]!["acted"]!.GetValue<bool>();
            if (!acted)
            {
                Assert.IsFalse(running.IsCompleted, $"{(running.IsCompleted ? (await running).ToJsonString() : null)}\n{LogOf(platform.Root)}");
            }
        }
        while (!acted);

        var suspended = await running;
        Assert.AreEqual((int)ExecutionOutcome.Suspended, suspended["result"]!["outcome"]!.GetValue<int>(), $"{suspended.ToJsonString()}\n{LogOf(platform.Root)}");

        var ended = await rpc.RequestAsync(id++, RDCorePlatformProtocol.SessionDebugTerminate, new JsonObject());
        Assert.IsTrue(ended["result"]!["acted"]!.GetValue<bool>(), ended.ToJsonString());
        await platform.ShutdownAsync(id);
    }

    // a program of the shell, `10 ? "HELLO"` and `20 GOTO 10` - `?` is Debug.Print - as the shell's RUN sends it, run under the debugger and stopped as a break at
    // the keyboard stops it: a GoTo that goes back for ever is stopped too, and the run says the line it stopped at, which is one of the program's. The workspace
    // is a VBA one, whose module Program the program is run as.
    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task TheShellsEndlessGoTo_IsStoppedByABreak_AndSaysALineOfTheProgram()
    {
        var program = new RDCore.CLI.App.Repl.ReplProgram();
        program.Store(10, "Debug.Print \"HELLO\"");
        program.Store(20, "GoTo 10");
        await using var platform = await Platform.StartAsync();
        var rpc = platform.Rpc;

        var initialize = await rpc.RequestAsync(1, "initialize", platform.InitializeParams(AsHexIdeDeclares));
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        Assert.IsNull((await rpc.RequestAsync(2, RDCorePlatformProtocol.PlatformInitialize, JsonNode.Parse(ClaimsDebugging)!.AsObject()))["error"]);

        var running = rpc.RequestAsync(3, RDCorePlatformProtocol.SessionExecute, new JsonObject
        {
            ["source"] = program.ToModuleSource(),
            ["moduleName"] = RDCore.CLI.App.Repl.ReplProgram.ModuleName,
            ["entryPoint"] = RDCore.CLI.App.Repl.ReplProgram.EntryPointName,
            ["debug"] = true,
        });

        // the shell's break is a pause of a program under the debugger. One that arrives before the program runs acts on nothing, and is sent again until one acts.
        var id = 4;
        bool acted;
        do
        {
            var paused = await rpc.RequestAsync(id++, RDCorePlatformProtocol.SessionDebugPause, new JsonObject());
            Assert.IsNull(paused["error"], $"{paused.ToJsonString()}\n{LogOf(platform.Root)}");
            acted = paused["result"]!["acted"]!.GetValue<bool>();
            if (!acted && running.IsCompleted)
            {
                Assert.Fail($"{(await running).ToJsonString()}\n{LogOf(platform.Root)}");
            }
        }
        while (!acted);

        var suspended = await running;
        var told = $"{suspended.ToJsonString()[..Math.Min(2000, suspended.ToJsonString().Length)]}\n{LogOf(platform.Root)}";
        Assert.AreEqual((int)ExecutionOutcome.Suspended, suspended["result"]!["outcome"]!.GetValue<int>(), told);
        CollectionAssert.Contains(new int?[] { 10, 20 }, program.LineNumberAt(suspended["result"]!["errorLine"]!.GetValue<int>()), told);

        var ended = await rpc.RequestAsync(id++, RDCorePlatformProtocol.SessionDebugTerminate, new JsonObject());
        Assert.IsTrue(ended["result"]!["acted"]!.GetValue<bool>(), ended.ToJsonString());
        await platform.ShutdownAsync(id);
    }

    // LSP's own locale, which a client of the protocol says: the language server speaks the client's language, and tells the components it starts the same.
    // OmniSharp 0.19.9 gives its handlers an initialize request without it. A document's diagnostics are pulled so that the parse server is known to have been
    // initialized. The logs are read once the platform is shut down: a file log is written behind, and only an ended process has written all of it.
    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task TheLocaleOfTheInitializeRequest_IsTheLanguageOfTheLanguageServer_AndOfTheComponentsItStarts()
    {
        await using var platform = await Platform.StartAsync();
        var rpc = platform.Rpc;
        var parameters = platform.InitializeParams("{}");
        parameters["locale"] = "fr-CA";

        var initialize = await rpc.RequestAsync(1, "initialize", parameters);
        Assert.IsNull(initialize["error"], initialize.ToJsonString());
        await rpc.NotifyAsync("initialized", new JsonObject());
        await rpc.NotifyAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri, ["languageId"] = "vba", ["version"] = 1, ["text"] = ModuleText },
        });
        var diagnostics = await rpc.RequestAsync(2, "textDocument/diagnostic", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = platform.ModuleUri }, ["identifier"] = "rdcore" });
        Assert.IsNull(diagnostics["error"], diagnostics.ToJsonString());

        await platform.ShutdownAsync(3);

        const string Applied = "The client's user interface is in 'fr-CA'.";
        StringAssert.Contains(WholeLogOf(platform.Root), Applied, "the language server: " + LogOf(platform.Root));
        StringAssert.Contains(WholeLogOf(platform.Root, "RDCore.ParseServer"), Applied, "the parse server it started");
    }

    // the language the user sets (Environment.Regional.Language) is the language of every component, over the locale of a client in English (an IDE's): the
    // environment host answers in it, and the language server, the parse server and the environment host say they started in it. How the program writes and
    // reads numbers and dates is untouched. The logs are read once the platform is shut down.
    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task TheLanguageTheUserSets_IsTheLanguageOfEveryComponent_WhateverTheClientsLocale()
    {
        await using var platform = await Platform.StartAsync(others: 1, projectFile: AProjectOfOtherModules, language: "fr");
        var notFound = await RunAsync(platform, "Progam", sessionModule: false);
        var told = $"{notFound.ToJsonString()}\n{LogOf(platform.Root)}";
        Assert.AreEqual((int)ExecutionOutcome.NotFound, notFound["result"]!["outcome"]!.GetValue<int>(), told);
        StringAssert.Contains(notFound["result"]!["errorMessage"]!.GetValue<string>(), "le module 'Progam' n'est pas défini dans la session", "the environment host: " + told);

        var failed = await platform.Rpc.RequestAsync(4, RDCorePlatformProtocol.SessionExecute, new JsonObject
        {
            ["source"] = "Public Sub Main()\r\n    Debug.Print CStr(2.5)\r\n    Debug.Print CStr(CDate(\"1/2/2026 13:05\"))\r\n    Debug.Print 1 / 0\r\nEnd Sub\r\n",
            ["moduleName"] = "Program",
            ["entryPoint"] = "Main",
            ["sessionModule"] = true,
        });
        told = $"{failed.ToJsonString()}\n{LogOf(platform.Root)}";
        Assert.AreEqual((int)ExecutionOutcome.RuntimeError, failed["result"]!["outcome"]!.GetValue<int>(), told);
        CollectionAssert.AreEqual(new[] { "2.5", "1/2/2026 1:05:00 PM" }, failed["result"]!["output"]!.AsArray().Select(line => line!.GetValue<string>().Trim()).ToArray(), told);
        Assert.AreEqual(11, failed["result"]!["errorNumber"]!.GetValue<int>(), told);

        await platform.ShutdownAsync(5);

        const string Started = "L'interface est en 'fr' (Environment.Regional.Language).";
        StringAssert.Contains(WholeLogOf(platform.Root), Started, "the language server, as it started: " + LogOf(platform.Root));
        StringAssert.Contains(WholeLogOf(platform.Root), "OnLanguageServerInitializedAsync handler complété.", "the language server's own messages: " + LogOf(platform.Root));
        StringAssert.Contains(WholeLogOf(platform.Root), "The user interface is in 'fr' (Environment.Regional.Language), whatever the client's locale ('en-US').",
            "the language server, as it was initialized: " + LogOf(platform.Root));
        StringAssert.Contains(WholeLogOf(platform.Root, "RDCore.ParseServer"), Started, "the parse server it started");
        StringAssert.Contains(WholeLogOf(platform.Root, "RDCore.EnvironmentHost"), Started, "the environment host it started");
    }

    // the platform as a deployment has it, with a workspace of one module, and the language server started from it as an IDE starts it.
    private sealed class Platform : IAsyncDisposable
    {
        private readonly string _root;
        private readonly Process _process;
        private readonly NamedPipeClientStream _pipe;

        private Platform(string root, string workspace, Process process, Task<int> exited, NamedPipeClientStream pipe)
        {
            _root = root;
            Workspace = new Uri(workspace).AbsoluteUri;
            ModuleUri = new Uri(Path.Combine(workspace, "Program.bas")).AbsoluteUri;
            _process = process;
            _pipe = pipe;
            Exited = exited;
            Rpc = new JsonRpc(pipe, (method, _) =>
            {
                Asked.Enqueue(method);
                return method == RDCorePlatformProtocol.SessionMessageBox ? MessageBoxAnswer() : null;
            });

            // a language server that exits answers nothing more: what the client waits for fails, with its log, at once.
            Rpc.Told = () => $"logged by the server:\n{string.Join("\n", Rpc.Logged)}\n{LogOf(root)}";
            _ = exited.ContinueWith(code => Rpc.Fail(new InvalidOperationException($"the language server exited with code {code.Result}.\n{Rpc.Told()}")),
                TaskScheduler.Default);
        }

        /// <summary>The language server's exit code, once it has exited.</summary>
        public Task<int> Exited { get; }

        public JsonRpc Rpc { get; }

        /// <summary>What the client answers a MsgBox with: vbYes, by default.</summary>
        public Func<JsonObject> MessageBoxAnswer { get; set; } = () => new JsonObject { ["handled"] = true, ["result"] = 6 };

        public string Root => _root;

        public string Workspace { get; }

        public string ModuleUri { get; }

        public ConcurrentQueue<string> Asked { get; } = [];

        // the workspace has `others` modules besides Program, which the language server parses and composes the session of before it runs anything, and the
        // project file given, if one is; and the language of the user interface the user set, if one is.
        public static async Task<Platform> StartAsync(int others = 0, string? projectFile = null, string? language = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "RDCore", "raw-client", Guid.NewGuid().ToString("N"));
            var workspace = Path.Combine(root, "Workspace");
            Directory.CreateDirectory(workspace);
            File.WriteAllText(Path.Combine(workspace, "Program.bas"), ModuleText);
            if (projectFile is not null)
            {
                File.WriteAllText(Path.Combine(workspace, RDCore.SDK.Workspace.ProjectFile.FileName), projectFile);
            }
            for (var index = 0; index < others; index++)
            {
                File.WriteAllText(Path.Combine(workspace, $"Other{index}.bas"), OtherModule(index));
            }

            var launch = RDCoreLanguageServerLaunch.Prepare(new RDCoreLanguageServerLaunchOptions
            {
                PlatformRoot = TestPlatformRoot.Directory,
                Workspace = new Uri(workspace).AbsoluteUri,
            });
            var start = launch.ToProcessStartInfo();
            start.Environment[PlatformEnvironment.UserDataEnvironmentVariable] = Path.Combine(root, "User");
            start.Environment[PlatformEnvironment.LogsEnvironmentVariable] = Path.Combine(root, "Logs");
            if (language is not null)
            {
                var user = Directory.CreateDirectory(Path.Combine(root, "User")).FullName;
                _ = new RDCore.SDK.Server.Configuration.UserSettingsStore(new System.IO.Abstractions.FileSystem(),
                    Path.Combine(user, RDCore.SDK.Server.Configuration.UserSettingsStore.FileName), RDCore.SDK.Server.Configuration.SettingsSchema.Platform)
                    .Set("Environment.Regional.Language", language);
            }

            var process = Process.Start(start)!;
            var exited = process.WaitForExitAsync().ContinueWith(_ => process.ExitCode, TaskScheduler.Default);
            var pipe = new NamedPipeClientStream(".", launch.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            var connected = pipe.ConnectAsync();
            if (await Unhung(Task.WhenAny(connected, exited), "the language server opening its pipe") != connected)
            {
                await pipe.DisposeAsync();
                throw new AssertFailedException($"the language server exited with code {await exited} before it opened its pipe.\n{LogOf(root)}");
            }

            await connected;
            return new Platform(root, workspace, process, exited, pipe);
        }

        // a client that opens no folder gives no root and no folders (LSP 3.17: rootUri null, workspaceFolders null).
        public JsonObject InitializeParams(string capabilities, bool opensAFolder = true) => new()
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = opensAFolder ? Workspace : null,
            ["workspaceFolders"] = opensAFolder ? new JsonArray(new JsonObject { ["uri"] = Workspace, ["name"] = "Workspace" }) : null,
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
            _ = await Unhung(Exited, "the language server exiting once it was told to exit");
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

    // every file of the component's log, in the order they were written: the file logger names each day's file after its date (RDCore.LanguageServer-20261105.log),
    // so a server that runs past midnight writes two, and the directory lists them in no particular order.
    private static string WholeLogOf(string root, string component = "RDCore.LanguageServer")
    {
        var logs = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, $"{component}*.log", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray()
            : [];
        if (logs.Length == 0)
        {
            return $"(no {component} log)";
        }

        return string.Concat(logs.Select(log =>
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new StreamReader(stream).ReadToEnd();
        }));
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
        private Exception? _failure;

        public JsonRpc(Stream stream, Func<string, JsonNode?, JsonNode?> answer)
        {
            _stream = stream;
            _answer = answer;
            _reading = Task.Run(ReadAllAsync);
        }

        /// <summary>What the server logged to the client (<c>window/logMessage</c>).</summary>
        public ConcurrentQueue<string> Logged { get; } = [];

        /// <summary>What a failure says besides itself: what the server logged.</summary>
        public Func<string> Told { get; set; } = () => string.Empty;

        /// <summary>What the client does once it has answered a request of the server, given its method and whether it held it.</summary>
        public Action<string, bool>? Answered { get; set; }

        /// <summary>
        /// A message the client writes before it answers a request of the server, given its method and whether it held it, in the same write as the answer; or none.
        /// </summary>
        public Func<string, bool, JsonObject?>? Before { get; set; }

        /// <summary>What the client waits for before it answers a request of the server, given its method, reading on meanwhile; or nothing, to answer at once.</summary>
        public Func<string, Task?>? Hold { get; set; }

        /// <summary>The answer to a request with this id, which the client writes itself.</summary>
        public Task<JsonObject> Expect(int id) => Unhung(Pending(id), $"the answer to request {id}");

        public Task NotifyAsync(string method, JsonObject parameters) => WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

        public async Task<JsonObject> RequestAsync(int id, string method, JsonObject parameters) => await await WriteRequestAsync(id, method, parameters);

        /// <summary>A request, written: what is given once it is written is its answer, which comes when it comes.</summary>
        public async Task<Task<JsonObject>> WriteRequestAsync(int id, string method, JsonObject parameters)
        {
            var answered = Pending(id);
            await WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
            return Unhung(answered, $"the answer to {method} ({id})");
        }

        /// <summary>
        /// Fails what the client waits for, and what it waits for from now on: the server is gone, and answers nothing more. The first failure is the one kept.
        /// </summary>
        public void Fail(Exception failure)
        {
            Interlocked.CompareExchange(ref _failure, failure, null);
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(_failure!);
            }
        }

        // what answers the request with this id; already failed when the server is gone.
        private Task<JsonObject> Pending(int id)
        {
            var answered = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = answered;
            if (Interlocked.CompareExchange(ref _failure, null, null) is { } failure)
            {
                answered.TrySetException(failure);
            }

            return answered.Task;
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
                            // a request from the server is answered: what the client does with it, or nothing. One the client holds is answered once it lets
                            // it go, and the client reads on meanwhile.
                            if (Hold?.Invoke(method) is { } held)
                            {
                                _ = AnswerWhenAsync(held, method, asked.DeepClone(), message["params"]);
                            }
                            else
                            {
                                await AnswerAsync(method, asked.DeepClone(), message["params"], held: false);
                            }
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
            catch (Exception exception)
            {
                // whatever ended the reading - the pipe closed, a frame unread, the client disposed - nothing more is answered.
                Fail(new InvalidOperationException($"the client stopped reading the server: {exception.Message}\n{Told()}", exception));
            }
        }

        private async Task AnswerAsync(string method, JsonNode id, JsonNode? parameters, bool held)
        {
            var answer = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = _answer(method, parameters)?.DeepClone() };
            await WriteAsync(Before?.Invoke(method, held) is { } first ? [first, answer] : [answer]);
            Answered?.Invoke(method, held);
        }

        private async Task AnswerWhenAsync(Task held, string method, JsonNode id, JsonNode? parameters)
        {
            try
            {
                await held;
                await AnswerAsync(method, id, parameters, held: true);
            }
            catch (Exception exception)
            {
                Fail(new InvalidOperationException($"the client could not answer {method}: {exception.Message}\n{Told()}", exception));
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
