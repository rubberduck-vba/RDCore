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
    // an absolute path the MockFileSystem accepts on both Windows and the Linux CI runner.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-ws");
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
        => new(NullLogger<ProjectFileService>.Instance, fs.Path, fs.File, new ProjectFileLoader(fs), () => SupportedLanguages.RDVBA);

    private static WorkspaceService Service(MockFileSystem fs, IServerStateProvider state, WorkspaceDocumentService documents, DocumentStateProvider documentStates)
    {
        var projectFiles = ProjectService(fs);
        return new WorkspaceService(
            new Version(99, 0, 0), state, NullLogger<WorkspaceService>.Instance,
            fs.Path, fs.File, fs.Directory, projectFiles, documents, documentStates,
            () => ProtocolSupportedLanguage.VBA, new WorkspaceProjects(projectFiles), new ProjectFileLoader(fs));
    }

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

        return Service(fs, state ?? InitializingState(), documents, documentStates);
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

    // a project file that is not JSON says so, and where: it used to surface later as a didOpen error about an empty path (HexIDE's findings desk #26).
    [TestMethod]
    public async Task ProjectFileService_LoadAsync_ProjectFileThatIsNotJson_SaysWhereItIsNot()
    {
        var sut = ProjectService(new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectFilePath] = new MockFileData("{\r\n  \"Version\": \"0.1.0\",\r\n  \"ProjectInfo\": { \"Name\": \"Check\" ]\r\n}"),
        }));

        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => sut.LoadAsync(Root));

        StringAssert.Contains(error.Message, ProjectFilePath);
        StringAssert.Contains(error.Message, "3");
        Assert.IsInstanceOfType<JsonException>(error.InnerException);
    }

    [TestMethod]
    public async Task ProjectFileService_LoadAsync_NoProjectFile_IsTheProjectTheFolderHolds_AndWritesNothing()
    {
        var fs = new MockFileSystem();
        fs.AddFile(Path.Combine(Root, "Module1.bas"), new MockFileData("Attribute VB_Name = \"Module1\"\r\n"));
        var sut = ProjectService(fs);

        await sut.LoadAsync(Root);

        Assert.IsTrue(sut.Project.IsSynthesized);
        Assert.AreEqual("Module1.bas", sut.Project.ProjectInfo.Modules.Single().RelativeUri);
        Assert.IsFalse(fs.FileExists(ProjectFilePath));
    }

    [TestMethod]
    public async Task WorkspaceService_LoadAsync_LoadsModuleContent_AndMarksItLoaded()
    {
        var fs = FileSystemWith(("src/Mod1.bas", "Public Sub Foo()\r\nEnd Sub"));
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(
            documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);

        var sut = Service(fs, InitializingState(), documents, documentStates);

        await sut.LoadAsync(Root);

        var loaded = documents.GetAllDocuments().ToArray();
        Assert.ContainsSingle(loaded);
        Assert.AreEqual("Public Sub Foo()\r\nEnd Sub", loaded[0].Text);
        Assert.IsInstanceOfType<LoadedDocumentState>(documentStates.GetCurrentState(loaded[0].Id));
    }

    // a resource file or a README the project lists is a document of the workspace, and none of its code: it is never parsed (HexIDE, desk 2026-10-27).
    [TestMethod]
    public async Task WorkspaceService_LoadAsync_LoadsTheOtherFiles_AndGivesThemToNoneThatReadsCode()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectFilePath] = new MockFileData(JsonSerializer.Serialize(new ProjectFile(Root, new RDCoreProject
            {
                Modules = [new RDCoreModule { RelativeUri = "src/Mod1.bas" }],
                OtherFiles = [new RDCoreFile { RelativeUri = "res/App.res" }, new RDCoreFile { RelativeUri = "README.md" }],
            }))),
            [Path.Combine(Root, "src", "Mod1.bas")] = new MockFileData("Public Sub Foo()\r\nEnd Sub"),
            [Path.Combine(Root, "res", "App.res")] = new MockFileData([0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0xFF, 0xFF]),
            [Path.Combine(Root, "README.md")] = new MockFileData("# Battleship"),
        });
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);
        var sut = Service(fs, InitializingState(), documents, documentStates);

        await sut.LoadAsync(Root);

        CollectionAssert.AreEqual(new[] { "Mod1.bas" }, documents.GetAllDocuments().Select(document => Path.GetFileName(document.RelativePath)).ToArray());
        Assert.IsTrue(documents.TryGetDocument(new Uri(Path.Combine(Root, "README.md")), out var readme), "a document of the workspace all the same");
        Assert.IsInstanceOfType<LoadedDocumentState>(documentStates.GetCurrentState(readme.Id));
        Assert.IsTrue(documents.TryGetDocument(new Uri(Path.Combine(Root, "res", "App.res")), out _));
    }

    // a project file keeps the version that wrote it as it is read: a later version's is refused, not taken for the running platform's (copy constructor bug).
    [TestMethod]
    [DataRow("100.0.0", true, DisplayName = "written by a later version")]
    [DataRow("99.0.0", false, DisplayName = "written by this version")]
    [DataRow("1.2.3", false, DisplayName = "written by an earlier version")]
    public async Task WorkspaceService_LoadAsync_RefusesAProjectALaterVersionWrote(string written, bool refused)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectFilePath] = new MockFileData($$"""{ "Version": "{{written}}", "Configuration": [], "ProjectInfo": { "Name": "Later" } }"""),
        });
        var sut = WorkspaceService(fs);

        if (refused)
        {
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => sut.LoadAsync(Root));
        }
        else
        {
            await sut.LoadAsync(Root);
        }
    }

    [TestMethod]
    public async Task WorkspaceDocumentService_TryGetDocument_FindsALoadedDocumentByUri_AndMissesAnUnknownOne()
    {
        var fs = FileSystemWith(("src/Mod1.bas", "Public Sub Foo()\r\nEnd Sub"));
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(
            documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);

        var sut = Service(fs, InitializingState(), documents, documentStates);
        await sut.LoadAsync(Root);

        var loaded = documents.GetAllDocuments().Single();

        Assert.IsTrue(documents.TryGetDocument(loaded.Id.Uri.ToUri(), out var found));
        Assert.AreEqual(loaded.Text, found.Text);
        Assert.IsFalse(documents.TryGetDocument(new Uri("file:///c:/nowhere/Ghost.bas"), out _));
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
