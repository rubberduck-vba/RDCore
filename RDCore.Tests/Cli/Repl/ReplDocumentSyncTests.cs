using System.IO.Abstractions.TestingHelpers;
using NSubstitute;
using RDCore.CLI.App.Repl;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The language server is told of the program as it is, one notification at a time and in order, whatever thread tells it: each change it is sent is a version
/// and the text that is that version.
/// </summary>
[TestClass]
public sealed class ReplDocumentSyncTests
{
    private static readonly Uri Document = new(Path.Combine(Path.GetTempPath(), "rdcore-sync", "Program.rdc"));

    private readonly ReplProgram _program = new();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();
    private readonly List<(int Version, string Text)> _sent = [];

    // each change is held until the test lets it go, the first one at least.
    private readonly TaskCompletionSource _firstSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ReplDocumentSyncTests()
    {
        _platform.ChangeDocumentAsync(Arg.Any<Uri>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            lock (_sent)
            {
                _sent.Add((call.ArgAt<int>(1), call.ArgAt<string>(2)));
                if (_sent.Count == 1)
                {
                    _firstSent.TrySetResult();
                    return _release.Task;
                }
            }

            return Task.CompletedTask;
        });
    }

    private async Task<ReplDocument> OpenedAsync()
    {
        var document = new ReplDocument(_program, _platform);
        _program.Store(10, "X = 1");
        await document.OpenAsync(Document.LocalPath, CancellationToken.None);
        return document;
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ALineStoredWhileTheServerIsTold_IsTheNextVersion_AndTheNextChangeSendsIt()
    {
        var document = await OpenedAsync();
        _program.Store(20, "Y = 2");

        var telling = document.SynchronizeAsync(CancellationToken.None);
        await _firstSent.Task.WaitAsync(TimeSpan.FromSeconds(20));
        _program.Store(30, "Z = 3");
        _release.SetResult();
        await telling;

        await document.SynchronizeAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 2, 3 }, _sent.Select(sent => sent.Version).ToArray(), "the version sent is the one of the text sent");
        Assert.AreEqual("10 X = 1\r\n20 Y = 2\r\n", _sent[0].Text);
        Assert.AreEqual("10 X = 1\r\n20 Y = 2\r\n30 Z = 3\r\n", _sent[1].Text, "the line stored meanwhile is not lost");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TwoChangesAtOnce_AreSentOneAfterTheOther_EachVersionOnce()
    {
        var document = await OpenedAsync();
        _program.Store(20, "Y = 2");

        var first = document.SynchronizeAsync(CancellationToken.None);
        await _firstSent.Task.WaitAsync(TimeSpan.FromSeconds(20));
        _program.Store(30, "Z = 3");
        var second = Task.Run(() => document.SynchronizeAsync(CancellationToken.None));
        var third = Task.Run(() => document.SynchronizeAsync(CancellationToken.None));

        // the others wait for the one that is being sent.
        Assert.IsFalse(second.Wait(TimeSpan.FromMilliseconds(200)), "a change waits for the one being sent");
        lock (_sent)
        {
            Assert.HasCount(1, _sent);
        }

        _release.SetResult();
        await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(20));

        CollectionAssert.AreEqual(new[] { 2, 3 }, _sent.Select(sent => sent.Version).ToArray(), "in order, and no version twice");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TheScratchDocument_IsOpenedOnce_WhenTwoAskForItAtOnce()
    {
        var opening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = 0;
        _platform.OpenDocumentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref opened);
            return opening.Task;
        });
        var workspace = await ReplWorkspace.CreateAsync(new MockFileSystem(), Substitute.For<IProjectFileWriter>());
        var document = new ReplDocument(_program, _platform, workspace);
        _program.Store(10, "X = 1");

        var one = Task.Run(() => document.EnsureOpenAsync(CancellationToken.None));
        var two = Task.Run(() => document.EnsureOpenAsync(CancellationToken.None));
        Assert.IsFalse(Task.WhenAll(one, two).Wait(TimeSpan.FromMilliseconds(200)));
        opening.SetResult();

        CollectionAssert.AreEqual(new[] { true, true }, await Task.WhenAll(one, two).WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.AreEqual(1, opened);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ASave_WaitsForTheChangeBeingSent_AndSavesTheTextTheServerWasToldOf()
    {
        var document = await OpenedAsync();
        _program.Store(20, "Y = 2");

        var telling = document.SynchronizeAsync(CancellationToken.None);
        await _firstSent.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var saving = Task.Run(() => document.WillSaveAsync(CancellationToken.None));
        Assert.IsFalse(saving.Wait(TimeSpan.FromMilliseconds(200)), "the save waits for the change being sent");
        await _platform.DidNotReceiveWithAnyArgs().WillSaveDocumentAsync(default!, default);

        _program.Store(30, "Z = 3");
        _release.SetResult();
        await telling;
        var saved = await saving.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.AreEqual(_sent[^1].Text, saved, "what is saved is what the server was told of last");
        Assert.AreEqual("10 X = 1\r\n20 Y = 2\r\n30 Z = 3\r\n", saved);
    }
}
