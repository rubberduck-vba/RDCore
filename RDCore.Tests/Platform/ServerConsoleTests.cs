using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDCore.Tests.Platform;

/// <summary>
/// A server of the platform writes nothing to its console. Its client is on a pipe, its log is in its file, and the process that starts it may redirect its
/// standard output and never read it: a console logger then filled that pipe, and every thread that logged blocked.
/// </summary>
[TestClass]
[TestCategory("Platform")]
public sealed class ServerConsoleTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [TestMethod]
    public async Task AServer_WhoseStandardOutputNobodyReads_AnswersRequestAfterRequest_AndWritesNothingToIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "RDCore", "server-console", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "Workspace");
        Directory.CreateDirectory(workspace);
        var pipe = $"rdcore-console-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var server = Path.Combine(AppContext.BaseDirectory, "RDCore.ParseServer.dll");

        // started as a client starts it: its standard output redirected, and never read.
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            ArgumentList = { server, "--pipe-name", pipe, "--workspace", new Uri(workspace).AbsoluteUri, "--client-process-id", Environment.ProcessId.ToString() },
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment = { ["RDCORE_PLATFORM_ROOT"] = root },
        };

        using var process = Process.Start(start)!;
        try
        {
            using var connection = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await connection.ConnectAsync((int)Bound.TotalMilliseconds);
            var rpc = new JsonRpc(connection);

            var watch = Stopwatch.StartNew();
            _ = await rpc.RequestAsync("initialize", new JsonObject { ["processId"] = Environment.ProcessId, ["rootUri"] = new Uri(workspace).AbsoluteUri, ["capabilities"] = new JsonObject() });
            await rpc.NotifyAsync("initialized", new JsonObject());

            // each parse is logged as it is asked for and as it is answered: 400 of them are many times what a pipe holds before its writer blocks.
            var module = new Uri(Path.Combine(workspace, "Module1.bas")).AbsoluteUri;
            for (var i = 0; i < 400; i++)
            {
                var answer = await rpc.RequestAsync("rdcore/parser/document", new JsonObject
                {
                    ["documentUri"] = module,
                    ["fragment"] = $"Option Explicit\r\nPublic Sub Main()\r\n    Debug.Print {i}\r\nEnd Sub\r\n",
                });
                Assert.IsNull(answer["error"], $"parse {i}: {answer["error"]}");
            }

            watch.Stop();
            Assert.IsLessThan(Bound, watch.Elapsed, "400 parses, each answered in milliseconds");

            // and nothing of it reached the console: not the host's narration as it started, not what it logged, not its footer as it ended.
            _ = await rpc.RequestAsync("shutdown", new JsonObject());
            await rpc.NotifyAsync("exit", new JsonObject());
            await process.WaitForExitAsync().WaitAsync(Bound);
            Assert.AreEqual(string.Empty, await process.StandardOutput.ReadToEndAsync());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // the server's log may still be held as it goes: the folder is a temporary one.
            }
        }
    }

    // JSON-RPC 2.0 over the pipe, as LSP frames it: what the test needs of a client, and no more.
    private sealed class JsonRpc(Stream stream)
    {
        private int _id;

        public Task NotifyAsync(string method, JsonObject parameters) => WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

        public async Task<JsonObject> RequestAsync(string method, JsonObject parameters)
        {
            var id = ++_id;
            await WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
            while (true)
            {
                // a request the server stops answering is the hang: the read is bounded, and fails the test rather than wait for ever.
                var message = await ReadAsync().WaitAsync(Bound);
                if (message["id"] is { } answered && message["method"] is null && answered.GetValue<int>() == id)
                {
                    return message;
                }

                // a request from the server is answered with nothing.
                if (message["id"] is { } asked && message["method"] is not null)
                {
                    await WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = asked.DeepClone(), ["result"] = null });
                }
            }
        }

        private async Task WriteAsync(JsonObject message)
        {
            var body = Encoding.UTF8.GetBytes(message.ToJsonString());
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
            await stream.FlushAsync();
        }

        private async Task<JsonObject> ReadAsync()
        {
            var header = new StringBuilder();
            var one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(one) == 0)
                {
                    throw new EndOfStreamException("the server closed the pipe");
                }

                header.Append((char)one[0]);
            }

            var length = int.Parse(header.ToString().Split("Content-Length:")[1].Split("\r\n")[0].Trim());
            var body = new byte[length];
            await stream.ReadExactlyAsync(body);
            return JsonNode.Parse(body)!.AsObject();
        }
    }
}
