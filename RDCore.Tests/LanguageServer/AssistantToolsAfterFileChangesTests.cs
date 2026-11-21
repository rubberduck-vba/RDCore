using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.LanguageServer.Assistant;
using RDCore.LanguageServer.Server.Handlers.Workspace;
using RDCore.SDK.Assistant;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Assistant;
using RDCore.SDK.Server.Services;
using System.IO.Pipelines;
using System.Text.Json.Nodes;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A tool the client calls after it said a file changed (<c>workspace/didChangeWatchedFiles</c>), as <c>rdc mcp</c> does once it wrote an edit, is called once
/// the server has read the file: the notification is handled as it arrives, and the call waits for the changes that arrived before it.
/// </summary>
[TestClass]
public sealed class AssistantToolsAfterFileChangesTests
{
    // a cap on a hang, not a budget: each wait is on the answer itself.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-assistant-reread-ws");

    private static DidChangeWatchedFilesParams Changed(string fileName) => new()
    {
        Changes = new Container<FileEvent>(new FileEvent { Uri = DocumentUri.FromFileSystemPath(Path.Combine(Root, fileName)), Type = FileChangeType.Changed }),
    };

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AToolCalledAfterTheClientSaysAFileChanged_IsCalledOnceTheFileIsRead()
    {
        using var timeout = new CancellationTokenSource(SafetyCap);
        var incoming = new IncomingDocumentChanges();
        incoming.ServeFileChanges();

        // the reading of the files goes on until the test lets it go; once the second notification's reading begins, the call sent before it has
        // arrived too.
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondBegun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var begun = 0;
        var read = false;
        var changes = Substitute.For<IWatchedFileChanges>();
        changes.ChangedAsync(Arg.Any<IReadOnlyList<FileEvent>>(), Arg.Any<ILanguageServerFacade>()).Returns(async _ =>
        {
            if (Interlocked.Increment(ref begun) == 2)
            {
                secondBegun.TrySetResult();
            }

            await reading.Task;
            read = true;
        });

        // the tool says whether the file was read when it was called.
        var readWhenCalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = Substitute.For<IAssistantTool>();
        probe.Name.Returns("probe");
        probe.CallAsync(Arg.Any<JsonObject>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            readWhenCalled.TrySetResult(read);
            return Task.FromResult(ToolCallResult.Of("called"));
        });

        var capabilities = new PlatformClientCapabilitiesService();
        capabilities.Record(new PlatformInitializeParams
        {
            ExpectedComponent = CoreServerComponent.LanguageServer,
            Expected = new CorePlatformClientCapabilities { LanguageServer = new LanguageServerCapabilities { AssistantTools = new AssistantTools(true) } },
        });

        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithLifecycleReceiver()
            .WithInput(PipeReader.Create(incoming.Tap(toServer.Reader.AsStream())))
            .WithOutput(toClient.Writer)
            .WithEveryRequestAnswered()
            .AddHandler(new ProjectFileWatchHandler(changes, Substitute.For<ILanguageServerFacade>(), incoming))
            .AddHandler(new ToolsCallHandler(new AssistantToolCatalog([probe]), capabilities, incoming)), timeout.Token);
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer), timeout.Token);
        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var client = await connecting;

        client.SendNotification(Changed("Module1.bas"));
        MediatR.IRequest<ToolCallReply> request = new ToolCallRequest(new ToolCallParams { Name = "probe" });
        var call = client.SendRequest(request, timeout.Token);
        client.SendNotification(Changed("Module2.bas"));
        await secondBegun.Task.WaitAsync(timeout.Token);
        reading.SetResult();

        Assert.AreEqual("called", (await call).Content.Single().Text);
        Assert.IsTrue(await readWhenCalled.Task.WaitAsync(timeout.Token), "the tool was called before the file the client said changed was read");
    }
}
