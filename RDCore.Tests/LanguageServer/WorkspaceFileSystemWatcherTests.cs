using System.IO.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.LanguageServer.Server.Handlers.Workspace;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The server's own watch of the workspace's files (#255): only for a client that does not watch them, never when the setting says so; the workspace's project
/// file and the source files under its root, not what a dot folder holds; and the events of a moment said together, once.
/// </summary>
[TestClass]
public sealed class WorkspaceFileSystemWatcherTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-watch-ws");

    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly IFileSystemWatcher _watcher = Substitute.For<IFileSystemWatcher>();
    private readonly IWatchedFileChanges _changes = Substitute.For<IWatchedFileChanges>();
    private readonly ILanguageServerFacade _server = Substitute.For<ILanguageServerFacade>();
    private readonly List<IReadOnlyList<FileEvent>> _said = [];

    public WorkspaceFileSystemWatcherTests()
    {
        _fileSystem.Path.Returns(new FileSystem().Path);
        _fileSystem.FileSystemWatcher.New(Arg.Any<string>()).Returns(_watcher);
        _changes.ChangedAsync(Arg.Any<IReadOnlyList<FileEvent>>(), Arg.Any<ILanguageServerFacade>())
            .Returns(call =>
            {
                lock (_said)
                {
                    _said.Add((IReadOnlyList<FileEvent>)call[0]);
                }

                return Task.CompletedTask;
            });
    }

    private WorkspaceFileSystemWatcher Watcher(bool enabled = true)
        => new(_fileSystem, Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { EnableFileSystemWatchers = enabled } }), _changes,
            NullLogger<WorkspaceFileSystemWatcher>.Instance)
        { Settle = TimeSpan.FromMilliseconds(50) };

    private static string PathOf(string relativePath) => Path.Combine(Root, relativePath);

    [TestMethod]
    public void AClientThatWatches_IsNotWatchedFor()
    {
        using var sut = Watcher();

        Assert.IsFalse(sut.Start(Root, clientWatches: true, _server));
        _fileSystem.FileSystemWatcher.DidNotReceiveWithAnyArgs().New(default!);
    }

    [TestMethod]
    public void TheSetting_TurnsTheWatchOff()
    {
        using var sut = Watcher(enabled: false);

        Assert.IsFalse(sut.Start(Root, clientWatches: false, _server));
        _fileSystem.FileSystemWatcher.DidNotReceiveWithAnyArgs().New(default!);
    }

    [TestMethod]
    public void AClientThatDoesNotWatch_IsWatchedFor_UnderTheWorkspaceRoot()
    {
        using var sut = Watcher();

        Assert.IsTrue(sut.Start(Root, clientWatches: false, _server));
        _fileSystem.FileSystemWatcher.Received(1).New(Path.GetFullPath(Root));
        Assert.IsTrue(_watcher.IncludeSubdirectories);
        Assert.IsTrue(_watcher.EnableRaisingEvents);
    }

    [TestMethod]
    public async Task TheEventsOfAMoment_AreSaidTogether_Once_EachFileAsItIsNow()
    {
        using var sut = Watcher();
        sut.Start(Root, clientWatches: false, _server);

        sut.OnFileSystemEvent(PathOf("A.bas"), FileChangeType.Changed);
        sut.OnFileSystemEvent(PathOf("A.bas"), FileChangeType.Changed);
        sut.OnFileSystemEvent(PathOf("src/B.cls"), FileChangeType.Created);
        sut.OnFileSystemEvent(PathOf("src/B.cls"), FileChangeType.Changed);
        sut.OnFileSystemEvent(PathOf(ProjectFile.FileName), FileChangeType.Changed);
        await sut.Said;

        var said = _said.Single();
        Assert.HasCount(3, said);
        Assert.AreEqual(FileChangeType.Changed, said.Single(change => change.Uri == DocumentUri.FromFileSystemPath(PathOf("A.bas"))).Type);
        Assert.AreEqual(FileChangeType.Created, said.Single(change => change.Uri == DocumentUri.FromFileSystemPath(PathOf("src/B.cls"))).Type);
    }

    [TestMethod]
    [DataRow(".git/hooks/Hook.bas", DisplayName = "what a dot folder holds")]
    [DataRow("README.md", DisplayName = "a file that is not a source file")]
    [DataRow("sub/.rdproj", DisplayName = "another folder's project file")]
    [DataRow("../elsewhere/Far.bas", DisplayName = "a file outside the workspace")]
    public async Task WhatIsNotTheWorkspaces_IsNotSaid(string relativePath)
    {
        using var sut = Watcher();
        sut.Start(Root, clientWatches: false, _server);

        sut.OnFileSystemEvent(Path.GetFullPath(PathOf(relativePath)), FileChangeType.Changed);
        await sut.Said;
        await Task.Delay(100);

        Assert.IsEmpty(_said);
    }
}
