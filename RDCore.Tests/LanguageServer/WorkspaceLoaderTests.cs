using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.LanguageServer.Server;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.SDK.Server.Services.States;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class WorkspaceLoaderTests
{
    private const string Root = @"C:\ws";
    private static readonly string ProjectFilePath = Path.Combine(Root, ProjectFile.FileName);

    private static MockFileSystem FileSystemWith(params (string RelativePath, string Content)[] files)
    {
        var project = new ProjectFile(Root, new RDCoreProject
        {
            Modules = [.. files.Select(f => new RDCoreModule { RelativeUri = f.RelativePath })],
        });

        var entries = new Dictionary<string, MockFileData>
        {
            [ProjectFilePath] = new MockFileData(JsonSerializer.Serialize(project)),
        };
        foreach (var (relativePath, content) in files)
        {
            entries[Path.Combine(Root, relativePath)] = new MockFileData(content);
        }
        return new MockFileSystem(entries);
    }

    private static ProjectFileService ProjectService(MockFileSystem fs)
        => new(NullLogger<ProjectFileService>.Instance, fs.Path, fs.File);

    private static IServerStateProvider InitializingState()
    {
        var state = Substitute.For<IServerStateProvider>();
        state.State.Returns(ServerState.Initializing);
        return state;
    }

    private static WorkspaceService WorkspaceService(MockFileSystem fs, IServerStateProvider? state = null)
    {
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(
            documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);

        return new WorkspaceService(
            new Version(99, 0, 0), state ?? InitializingState(), NullLogger<WorkspaceService>.Instance,
            fs.Path, fs.File, fs.Directory, ProjectService(fs), documents, documentStates,
            [ProtocolSupportedLanguage.VBA]);
    }

    [TestMethod]
    public async Task ProjectFileService_LoadAsync_Success_SetsProjectAndReturns()
    {
        var fs = FileSystemWith(("src/Mod1.bas", "Attribute VB_Name = \"Mod1\""));
        var sut = ProjectService(fs);

        await sut.LoadAsync(Root);

        Assert.AreEqual(Root, sut.Project.Uri);
        Assert.ContainsSingle(sut.Project.ProjectInfo.Modules);
        Assert.AreEqual("src/Mod1.bas", sut.Project.ProjectInfo.Modules[0].RelativeUri);
    }

    [TestMethod]
    public async Task ProjectFileService_LoadAsync_NoProjectFile_Throws()
    {
        var sut = ProjectService(new MockFileSystem());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => sut.LoadAsync(Root));
    }

    [TestMethod]
    public async Task WorkspaceService_LoadAsync_LoadsModuleContent_AndMarksItLoaded()
    {
        var fs = FileSystemWith(("src/Mod1.bas", "Public Sub Foo()\r\nEnd Sub"));
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(
            documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);

        var sut = new WorkspaceService(
            new Version(99, 0, 0), InitializingState(), NullLogger<WorkspaceService>.Instance,
            fs.Path, fs.File, fs.Directory, ProjectService(fs), documents, documentStates,
            [ProtocolSupportedLanguage.VBA]);

        await sut.LoadAsync(Root);

        var loaded = documents.GetAllDocuments().ToArray();
        Assert.ContainsSingle(loaded);
        Assert.AreEqual("Public Sub Foo()\r\nEnd Sub", loaded[0].Text);
        Assert.IsInstanceOfType<LoadedDocumentState>(documentStates.GetCurrentState(loaded[0].Id));
    }

    [TestMethod]
    public async Task WorkspaceService_LoadAsync_NotInitializing_Throws()
    {
        var state = Substitute.For<IServerStateProvider>();
        state.State.Returns(ServerState.Running);

        var sut = WorkspaceService(FileSystemWith(("src/Mod1.bas", "")), state);

        await Assert.ThrowsExactlyAsync<InvalidServerStateException>(() => sut.LoadAsync(Root));
    }
}
