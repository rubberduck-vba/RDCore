using OmniSharp.Extensions.LanguageServer.Client;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform;
using System.Diagnostics;
using System.IO.Pipes;

namespace RDCore.Tests.Client;

/// <summary>
/// The language server, started as an IDE starts a server it registered (<c>"transport": "pipe", "pipeRole": "connect"</c>, with
/// <c>--pipe-name {pipe} --workspace {workspaceUri}</c>) from the pre-launch preparation (<see cref="RDCoreLanguageServerLaunch"/>), answers the LSP
/// <c>initialize</c> request, and then the platform handshake, over the IDE's own LSP connection.
/// </summary>
[TestClass]
[TestCategory("Platform")]
public sealed class LanguageServerStartedByAnIdeTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Bound = TimeSpan.FromMinutes(5);

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task TheLanguageServer_StartedWithAPipeAndAWorkspace_AnswersInitialize_AndThePlatformHandshake()
    {
        // the test's own workspace, user settings and logs, beside a platform root as a deployment has it.
        var root = Path.Combine(Path.GetTempPath(), "RDCore", "ide-launch", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "Workspace");
        Directory.CreateDirectory(workspace);

        var launch = RDCoreLanguageServerLaunch.Prepare(new RDCoreLanguageServerLaunchOptions
        {
            PlatformRoot = TestPlatformRoot.Directory,
            Workspace = new Uri(workspace).AbsoluteUri,
        });
        var start = launch.ToProcessStartInfo();
        start.Environment[PlatformEnvironment.UserDataEnvironmentVariable] = Path.Combine(root, "User");
        start.Environment[PlatformEnvironment.LogsEnvironmentVariable] = Path.Combine(root, "Logs");

        using var process = Process.Start(start)!;
        try
        {
            using var timeout = new CancellationTokenSource(Bound);
            using var pipe = new NamedPipeClientStream(".", launch.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);

            // LanguageClient.From sends initialize, and returns once it is answered.
            using var client = await LanguageClient.From(options => options
                .WithInput(pipe)
                .WithOutput(pipe)
                .WithRootUri(new Uri(workspace)), timeout.Token);

            Assert.IsNotNull(client.ServerSettings.Capabilities, "initialize is answered");

            var platform = new RDCorePlatformClient(new OmniSharpPlatformChannel(client));
            var handshake = await platform.InitializeAsync(CoreServerComponent.LanguageServer, new CorePlatformClientCapabilities { LanguageServer = new() }, timeout.Token);

            Assert.AreEqual(CoreServerComponent.LanguageServer, handshake.Component);
            Assert.IsNotEmpty(handshake.Provided, "the language server provides capabilities beyond LSP");

            await client.Shutdown();
            await process.WaitForExitAsync(timeout.Token);
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
