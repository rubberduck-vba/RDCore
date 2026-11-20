using RDCore.CLI.App.Mcp;
using RDCore.SDK.Assistant;
using RDCore.SDK.Platform;
using RDCore.Tests.Client;
using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json.Nodes;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc mcp --workspace &lt;path&gt;</c> brings the platform up as a client and serves its tools to an assistant's host over standard input and output,
/// where nothing is written but the MCP messages.
/// </summary>
[TestClass]
public sealed class McpCommandTests
{
    // a cap on a hang, not a budget: each wait is on the process itself, which a loaded machine starts and stops as late as it does.
    private static readonly TimeSpan Bound = TimeSpan.FromMinutes(5);

    [TestMethod]
    [DataRow(new[] { "--workspace", "C:/Work" }, "C:/Work")]
    [DataRow(new[] { "--workspace=C:/Work" }, "C:/Work")]
    [DataRow(new string[0], null)]
    [DataRow(new[] { "--workspace" }, null)]
    [DataRow(new[] { "--workspace=" }, null)]
    [DataRow(new[] { "--workspace", "--verbose" }, null)]
    [DataRow(new[] { "C:/Work" }, null)]
    [DataRow(new[] { "--workspace", "C:/Work", "--other" }, null)]
    public void TheWorkspace_IsTheOneArgumentOfTheCommand(string[] args, string? expected)
        => Assert.AreEqual(expected, McpCommand.WorkspaceOf(args));

    [TestMethod]
    public async Task ACommandLineWithoutAWorkspace_IsRefused_WithTheUsage()
    {
        var error = new StringWriter();

        var exitCode = await new McpCommand(new MockFileSystem(), error).ExecuteAsync([], CancellationToken.None);

        Assert.AreEqual(McpCommand.Refused, exitCode);
        StringAssert.Contains(error.ToString(), "rdc mcp --workspace");
    }

    [TestMethod]
    public async Task AWorkspaceThatIsNoFolder_IsRefused()
    {
        var error = new StringWriter();
        var fileSystem = new MockFileSystem();
        var missing = fileSystem.Path.GetFullPath("missing");

        var exitCode = await new McpCommand(fileSystem, error).ExecuteAsync(["--workspace", "missing"], CancellationToken.None);

        Assert.AreEqual(McpCommand.Refused, exitCode);
        StringAssert.Contains(error.ToString(), missing);
    }

    [TestMethod]
    [TestCategory("Platform")]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AnMcpSession_IsAnsweredAtOnce_ServesTheTools_AndEndsWithStandardInput()
    {
        var root = Path.Combine(Path.GetTempPath(), "RDCore", "mcp", Guid.NewGuid().ToString("N"));
        var workspace = Directory.CreateDirectory(Path.Combine(root, "Workspace")).FullName;
        File.WriteAllText(Path.Combine(workspace, "Module1.bas"), string.Join("\r\n",
            "Attribute VB_Name = \"Module1\"",
            "Option Explicit",
            "Public Function Twice(ByVal value As Long) As Long",
            "    Twice = value * 2",
            "End Function",
            ""));

        var platform = TestPlatformRoot.Directory;
        var start = new ProcessStartInfo(Path.Combine(platform, "Platform", OperatingSystem.IsWindows() ? "rdc.exe" : "rdc"))
        {
            ArgumentList = { "mcp", "--workspace", workspace },
            WorkingDirectory = workspace,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment[PlatformEnvironment.RootEnvironmentVariable] = platform;
        start.Environment[PlatformEnvironment.UserDataEnvironmentVariable] = Path.Combine(root, "User");
        start.Environment[PlatformEnvironment.LogsEnvironmentVariable] = Path.Combine(root, "Logs");

        using var rdc = Process.Start(start)!;
        var errors = rdc.StandardError.ReadToEndAsync();
        try
        {
            var lines = new List<string>();
            async Task<JsonObject> AskAsync(string request)
            {
                await rdc.StandardInput.WriteAsync(request + "\n");
                await rdc.StandardInput.FlushAsync();
                var line = await rdc.StandardOutput.ReadLineAsync().WaitAsync(Bound) ?? throw new AssertFailedException($"rdc ended: {await errors}");
                lines.Add(line);
                return JsonNode.Parse(line)!.AsObject();
            }

            var initialized = await AskAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""");
            Assert.AreEqual(McpToolServer.ProtocolVersion, (string)initialized["result"]!["protocolVersion"]!);
            await rdc.StandardInput.WriteAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""" + "\n");

            var listed = await AskAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var names = listed["result"]!["tools"]!.AsArray().Select(tool => (string)tool!["name"]!).ToList();
            CollectionAssert.IsSubsetOf(new[] { CoreAssistantTools.GetDiagnostics, CoreAssistantTools.FindSymbol, CoreAssistantTools.References, CoreAssistantTools.Definition }, names);

            var found = await AskAsync("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"find_symbol","arguments":{"query":"Twice"}}}""");
            // asked right after start, and answered from the workspace once it is loaded.
            Assert.AreEqual(3, (int)found["id"]!);
            Assert.IsFalse((bool)found["result"]!["isError"]!, found.ToJsonString());
            var symbol = found["result"]!["structuredContent"]!["symbols"]!.AsArray().Single()!;
            Assert.AreEqual("Twice", (string?)symbol["name"], found.ToJsonString());
            Assert.AreEqual("Module1.bas", (string?)symbol["location"]!["path"], found.ToJsonString());

            // the host ends the session by closing its end.
            rdc.StandardInput.Close();
            var rest = await rdc.StandardOutput.ReadToEndAsync().WaitAsync(Bound);
            await rdc.WaitForExitAsync().WaitAsync(Bound);

            lines.AddRange(rest.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.AreEqual(0, rdc.ExitCode, await errors.WaitAsync(Bound));
            foreach (var line in lines)
            {
                Assert.AreEqual("2.0", (string?)JsonNode.Parse(line)?["jsonrpc"], $"standard output has only MCP messages: {line}");
            }
        }
        finally
        {
            if (!rdc.HasExited)
            {
                rdc.Kill(entireProcessTree: true);
            }

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // the folder is a temporary one.
            }
        }
    }
}
