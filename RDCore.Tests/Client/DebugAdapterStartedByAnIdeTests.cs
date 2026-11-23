using OmniSharp.Extensions.DebugAdapter.Client;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using RDCore.SDK.Client;
using RDCore.SDK.Platform;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace RDCore.Tests.Client;

/// <summary>
/// The debug adapter, started as an IDE starts it (<see cref="RDCoreDebugAdapterLaunch"/>): it brings the platform up for the workspace, the language server's
/// way, and runs the program the launch names in the environment host.
/// </summary>
[TestCategory(TestCategories.Wide)]
[TestClass]
[TestCategory("Platform")]
public sealed class DebugAdapterStartedByAnIdeTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Bound = TimeSpan.FromMinutes(5);

    // a Visual Basic 6.0 project of a form and a module, as its project file names them.
    private const string Project = "Type=Exe\r\nForm=Form1.frm\r\nModule=Module1; Module1.bas\r\nStartup=\"Sub Main\"\r\nName=\"Check\"\r\n";

    private const string Form = "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Check\"\r\n" +
        "   ClientHeight    =   3000\r\n" +
        "   ClientWidth     =   4000\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n" +
        "Attribute VB_GlobalNameSpace = False\r\n" +
        "Attribute VB_Creatable = False\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Attribute VB_Exposed = False\r\n" +
        "Option Explicit\r\n";

    // Main prints a line; ShowForm, which nothing calls, names the members the form's class has of the VB library's Form. The module is loaded whole before
    // Main runs, so Main runs only if those members are the form's.
    private const string Module = "Attribute VB_Name = \"Module1\"\r\n" +
        "Sub Main()\r\n" +
        "    Debug.Print \"loaded\"\r\n" +
        "End Sub\r\n" +
        "Sub ShowForm()\r\n" +
        "    Form1.Show\r\n" +
        "    Debug.Print Form1.Caption\r\n" +
        "End Sub\r\n";

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AVB6Program_LaunchedInTheDebugAdapter_NamesTheMembersItsFormHasOfTheVBForm()
    {
        // the test's own workspace, user settings and logs, beside a platform root as a deployment has it.
        var root = Path.Combine(Path.GetTempPath(), "RDCore", "dap-launch", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "Workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "Check.vbp"), Project);
        File.WriteAllText(Path.Combine(workspace, "Form1.frm"), Form);
        File.WriteAllText(Path.Combine(workspace, "Module1.bas"), Module);

        var launch = RDCoreDebugAdapterLaunch.Prepare(new RDCoreDebugAdapterLaunchOptions
        {
            PlatformRoot = TestPlatformRoot.Directory,
            Workspace = workspace,
            Module = "Module1",
            EntryPoint = "Main",
        });
        var start = launch.ToProcessStartInfo();
        start.Environment[PlatformEnvironment.UserDataEnvironmentVariable] = Path.Combine(root, "User");
        start.Environment[PlatformEnvironment.LogsEnvironmentVariable] = Path.Combine(root, "Logs");
        start.RedirectStandardError = true;

        using var process = Process.Start(start)!;
        var errors = new ConcurrentQueue<string>();
        process.ErrorDataReceived += (_, line) => errors.Enqueue(line.Data ?? string.Empty);
        process.BeginErrorReadLine();
        try
        {
            using var timeout = new CancellationTokenSource(Bound);
            var output = new ConcurrentQueue<string>();
            var exited = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var client = await DebugAdapterClient.From(options =>
            {
                options
                    .WithInput(process.StandardOutput.BaseStream)
                    .WithOutput(new RDCore.SDK.Server.ConnectionOutput(System.IO.Pipelines.PipeWriter.Create(process.StandardInput.BaseStream)));
                options.OnOutput(said => output.Enqueue(said.Output));
                options.OnExited(ended => exited.TrySetResult(ended.ExitCode));
            }, timeout.Token);

            var prepared = launch.LaunchArguments.ToDictionary(argument => argument.Key, argument => (object)argument.Value);
            _ = await client.SendRequest(new LaunchRequestArguments { ExtensionData = prepared }, timeout.Token);
            _ = await client.SendRequest(new ConfigurationDoneArguments(), timeout.Token);
            var exitCode = await exited.Task.WaitAsync(timeout.Token);

            var told = $"output: {string.Concat(output)}\nstandard error: {string.Join("\n", errors)}";
            Assert.AreEqual(0L, exitCode, told);
            CollectionAssert.Contains(string.Concat(output).Split('\n').Select(line => line.Trim()).ToArray(), "loaded", told);
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
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // a log may still be held as the processes go: the folder is a temporary one.
            }
        }
    }
}
