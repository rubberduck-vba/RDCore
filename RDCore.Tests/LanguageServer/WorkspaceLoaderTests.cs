using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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

    private static WorkspaceService Service(MockFileSystem fs, IServerStateProvider state, WorkspaceDocumentService documents, DocumentStateProvider documentStates,
        Microsoft.Extensions.Logging.ILogger<WorkspaceService>? logger = null)
    {
        var projectFiles = ProjectService(fs);
        return new WorkspaceService(
            state, logger ?? NullLogger<WorkspaceService>.Instance,
            fs.Path, fs.File, fs.Directory, projectFiles, documents, documentStates,
            () => ProtocolSupportedLanguage.VBA, new WorkspaceProjects(projectFiles), new ProjectFileLoader(fs));
    }

    private static IServerStateProvider InitializingState()
    {
        var state = Substitute.For<IServerStateProvider>();
        state.State.Returns(ServerState.Initializing);
        return state;
    }

    private static WorkspaceService WorkspaceService(MockFileSystem fs, IServerStateProvider? state = null, Microsoft.Extensions.Logging.ILogger<WorkspaceService>? logger = null)
    {
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(
            documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);

        return Service(fs, state ?? InitializingState(), documents, documentStates, logger);
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
    [DataRow("{ \"ProjectInfo\": { \"Name\": \"ByHand\", \"Modules\": [ \"Module1.bas\" ] } }", DisplayName = "no Version")]
    [DataRow("{ \"Version\": \"\", \"ProjectInfo\": { \"Name\": \"ByHand\", \"Modules\": [ \"Module1.bas\" ] } }", DisplayName = "an empty Version")]
    public async Task WorkspaceService_LoadAsync_AProjectFileNoPlatformWrote_Loads(string json)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectFilePath] = new MockFileData(json),
            [Path.Combine(Root, "Module1.bas")] = new MockFileData("Attribute VB_Name = \"Module1\"\r\n"),
        });
        var sut = WorkspaceService(fs);

        await sut.LoadAsync(Root);
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

    // a project file's version is the version of its format, and a project file loads whatever it says: the first builds stamped theirs 1.0.0, which a
    // 0.1 platform refused as a later platform's (desk card version-010-refuses-100). What is not this platform's format is said in the log, and only there.
    [TestMethod]
    [DataRow("\"1.0.0\"", null, DisplayName = "1.0.0, as the first builds stamped it: the format 1.0")]
    [DataRow("\"1.0\"", null, DisplayName = "1.0, this platform's format")]
    [DataRow("\"0.1.3\"", LogLevel.Information, DisplayName = "0.1.3, as the next builds stamped it: an older format")]
    [DataRow("\"1.7\"", LogLevel.Warning, DisplayName = "1.7, a newer minor")]
    [DataRow("\"100.0.0\"", LogLevel.Warning, DisplayName = "100.0.0, a newer major that reads all the same")]
    [DataRow("2", LogLevel.Warning, DisplayName = "the number 2, a newer major")]
    [DataRow("\"v1\"", LogLevel.Warning, DisplayName = "v1, no format's version")]
    [DataRow("{ \"Major\": 1 }", LogLevel.Warning, DisplayName = "an object, no format's version")]
    public async Task WorkspaceService_LoadAsync_LoadsAProjectFileWhateverFormatItSays_AndLogsOneThatIsNotThisOne(string version, LogLevel? logged)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ProjectFilePath] = new MockFileData($$"""{ "Version": {{version}}, "Configuration": [], "ProjectInfo": { "Name": "Written", "Modules": [ "Module1.bas" ] } }"""),
            [Path.Combine(Root, "Module1.bas")] = new MockFileData("Attribute VB_Name = \"Module1\"\r\n"),
        });
        var logger = new CapturingLogger();
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);
        var sut = Service(fs, InitializingState(), documents, documentStates, logger);

        await sut.LoadAsync(Root);

        Assert.ContainsSingle(documents.GetAllDocuments(), "the project's module is loaded");
        var said = logger.Entries.Where(entry => entry.Message.Contains("format", StringComparison.Ordinal)).Select(entry => entry.Level).ToArray();
        CollectionAssert.AreEqual(logged is { } level ? new[] { level } : Array.Empty<LogLevel>(), said);
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<WorkspaceService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
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
