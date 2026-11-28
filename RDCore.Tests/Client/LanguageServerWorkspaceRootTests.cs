using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;

namespace RDCore.Tests.Client;

/// <summary>
/// The workspace the language server loads, of the folder its client opened (the <c>initialize</c> request's <c>rootUri</c>) and the one it was started on
/// (<c>--workspace</c>): the folder the client opened, and the one the server was started on when the client opened none. When the two are different folders,
/// the user is warned (<c>window/showMessage</c>). The server is started as an IDE starts it (<see cref="RDCoreLanguageServerLaunch"/>).
/// </summary>
/// <remarks>
/// Each folder's project file is not JSON, so the server tells the client which folder it could not load: that is the folder it loaded.
/// </remarks>
[TestCategory(TestCategories.Wide)]
[TestClass]
[TestCategory("Platform")]
public sealed class LanguageServerWorkspaceRootTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Bound = TimeSpan.FromMinutes(5);

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AClientThatOpensAFolderAboveTheProject_HasTheFolderItOpenedLoaded_AndIsWarned()
    {
        var (parent, project, shown) = await InitializeAsync(opens: Opened.Parent);

        Assert.IsTrue(shown.Any(message => message.Type == MessageType.Error && Names(message, parent) && !Names(message, project)),
            "the folder the client opened is loaded: " + Said(shown));
        Assert.IsTrue(shown.Any(message => message.Type == MessageType.Warning && Names(message, parent) && Names(message, project)),
            "the user is warned of the two folders: " + Said(shown));
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AClientThatOpensNoFolder_HasTheOneTheServerWasStartedOnLoaded()
    {
        var (_, project, shown) = await InitializeAsync(opens: Opened.None);

        Assert.IsTrue(shown.Any(message => message.Type == MessageType.Error && Names(message, project)), "the project is loaded: " + Said(shown));
        Assert.IsFalse(shown.Any(message => message.Type == MessageType.Warning), "nothing to warn of: " + Said(shown));
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AClientThatOpensTheFolderTheServerWasStartedOn_IsNotWarned()
    {
        var (_, project, shown) = await InitializeAsync(opens: Opened.Project);

        Assert.IsTrue(shown.Any(message => message.Type == MessageType.Error && Names(message, project)), "the project is loaded: " + Said(shown));
        Assert.IsFalse(shown.Any(message => message.Type == MessageType.Warning), "one folder, nothing to warn of: " + Said(shown));
    }

    private enum Opened { None, Parent, Project }

    // a folder, and a project folder below it, each with a project file that is not JSON; the server started on the project, and its client opening the folder
    // given. What the server showed its user up to the error of the load, which it shows before initialize is answered, the warning first.
    private static async Task<(string Parent, string Project, IReadOnlyList<ShowMessageParams> Shown)> InitializeAsync(Opened opens)
    {
        var root = Path.Combine(Path.GetTempPath(), "RDCore", "ws-root", Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(root, "Opened");
        var project = Path.Combine(parent, "Project");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(parent, ".rdproj"), "not a project");
        File.WriteAllText(Path.Combine(project, ".rdproj"), "not a project");

        var launch = RDCoreLanguageServerLaunch.Prepare(new RDCoreLanguageServerLaunchOptions
        {
            PlatformRoot = TestPlatformRoot.Directory,
            Workspace = new Uri(project).AbsoluteUri,
        });
        var start = launch.ToProcessStartInfo();
        start.Environment[PlatformEnvironment.UserDataEnvironmentVariable] = Path.Combine(root, "User");
        start.Environment[PlatformEnvironment.LogsEnvironmentVariable] = Path.Combine(root, "Logs");

        var shown = new ConcurrentQueue<ShowMessageParams>();
        var loadFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var process = Process.Start(start)!;
        try
        {
            using var timeout = new CancellationTokenSource(Bound);
            using var pipe = new NamedPipeClientStream(".", launch.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);

            using var client = await LanguageClient.From(options =>
            {
                options
                    .WithInput(pipe)
                    .WithOutput(new RDCore.SDK.Server.ConnectionOutput(System.IO.Pipelines.PipeWriter.Create(pipe)))
                    .OnShowMessage(message =>
                    {
                        shown.Enqueue(message);
                        if (message.Type == MessageType.Error)
                        {
                            loadFailed.TrySetResult();
                        }
                    });
                if (opens != Opened.None)
                {
                    options.WithRootUri(new Uri(opens == Opened.Parent ? parent : project));
                }
            }, timeout.Token);

            await loadFailed.Task.WaitAsync(timeout.Token);
            await client.Shutdown();
            await process.WaitForExitAsync(timeout.Token);
            return (parent, project, [.. shown]);
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

    // a message names a folder as a path, whatever the case of its drive; one that names a folder below it does not name it by that alone.
    private static bool Names(ShowMessageParams message, string folder)
        => message.Message.Split('\'').Any(part => string.Equals(Path.TrimEndingDirectorySeparator(part), folder, StringComparison.OrdinalIgnoreCase));

    private static string Said(IEnumerable<ShowMessageParams> shown) => string.Join(" | ", shown.Select(message => $"{message.Type}: {message.Message}"));
}
