using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Server;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.SDK.Server.Services.States;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A client that opens several folders (<strong>LSP 3.17</strong> <c>workspaceFolders</c>): each folder that is a project's is loaded as one, a folder that only
/// holds the sources a project lists is not, and a file no folder holds is refused.
/// </summary>
[TestClass]
public sealed class WorkspaceFoldersLoadTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "rdcore-folders-load");
    private static readonly string App = Path.Combine(Base, "App");
    private static readonly string Shared = Path.Combine(Base, "Shared");
    private static readonly string Lib = Path.Combine(Base, "Lib");

    private static readonly string Util = Path.Combine(Shared, "Util.bas");

    private static MockFileData ProjectOf(string root, string name, params string[] modules)
        => new(JsonSerializer.Serialize(new ProjectFile(root, new RDCoreProject
        {
            Name = name,
            Modules = [.. modules.Select(module => new RDCoreModule { RelativeUri = module })],
        })));

    // App lists its own Main.bas and ..\Shared\Util.bas, as a .vbp that lists ..\Shared\Util.bas does; Lib lists the same Util.bas and a Lib.bas of its own.
    private static MockFileSystem Files() => new(new Dictionary<string, MockFileData>
    {
        [Path.Combine(App, ProjectFile.FileName)] = ProjectOf(App, "App", "Main.bas", Path.Combine("..", "Shared", "Util.bas")),
        [Path.Combine(App, "Main.bas")] = new("Attribute VB_Name = \"Main\"\r\n"),
        [Util] = new("Attribute VB_Name = \"Util\"\r\n"),
        [Path.Combine(Lib, ProjectFile.FileName)] = ProjectOf(Lib, "Lib", "Lib.bas", Path.Combine("..", "Shared", "Util.bas")),
        [Path.Combine(Lib, "Lib.bas")] = new("Attribute VB_Name = \"Lib\"\r\n"),
    });

    private sealed class Loaded(MockFileSystem files)
    {
        public readonly ProjectFileService ProjectFiles = new(NullLogger<ProjectFileService>.Instance, files.Path, files.File, new ProjectFileLoader(files), () => SupportedLanguages.RDVBA);
        public readonly DocumentStateProvider States = new(NullLogger<DocumentStateProvider>.Instance);
        public WorkspaceDocumentService Documents = null!;
        public WorkspaceProjects Projects = null!;
        public WorkspaceService Workspace = null!;

        public async Task<Loaded> LoadAsync(string root, params string[] folders)
        {
            var state = Substitute.For<IServerStateProvider>();
            state.State.Returns(ServerState.Initializing);
            Documents = new WorkspaceDocumentService(States, NullLogger<WorkspaceDocumentService>.Instance, files.Path, files.File);
            Projects = new WorkspaceProjects(ProjectFiles);
            Workspace = new WorkspaceService(
                new Version(99, 0, 0), state, NullLogger<WorkspaceService>.Instance, files.Path, files.File, files.Directory, ProjectFiles, Documents, States,
                () => ProtocolSupportedLanguage.VBA, Projects, new ProjectFileLoader(files));
            await Workspace.LoadAsync(root, folders);
            return this;
        }

        public string[] Names(string project) => [.. Documents.GetProjectDocuments(project).Select(document => document.Name).Order()];
    }

    [TestMethod]
    public async Task AFolderThatOnlyHoldsSourcesAProjectLists_IsNoProject_AndTheFileIsTheProjects()
    {
        var loaded = await new Loaded(Files()).LoadAsync(App, App, Shared);

        Assert.HasCount(1, loaded.Projects.Projects);
        CollectionAssert.AreEqual(new[] { "Main", "Util" }, loaded.Names(App), "..\\Shared\\Util.bas is a module of the project that lists it");
    }

    [TestMethod]
    public async Task AFolderNoProjectListsFilesIn_IsAProjectOfItsOwn()
    {
        var loaded = await new Loaded(Files()).LoadAsync(App, App, Shared, Lib);

        CollectionAssert.AreEqual(new[] { App, Lib }, loaded.Projects.Projects.Select(project => project.Root).ToArray());
        Assert.AreEqual("Lib", loaded.Projects.Projects[1].Project.ProjectInfo.Name);
    }

    [TestMethod]
    public async Task AFileTwoProjectsList_IsOneDocument_AndAModuleOfEach()
    {
        var loaded = await new Loaded(Files()).LoadAsync(App, App, Shared, Lib);

        CollectionAssert.AreEqual(new[] { "Lib", "Util" }, loaded.Names(Lib));
        CollectionAssert.AreEqual(new[] { "Main", "Util" }, loaded.Names(App));
        Assert.IsTrue(loaded.Documents.TryGetDocument(new Uri(Util), out var util));
        Assert.AreEqual(App, util.WorkspaceRoot, "the project that loaded it first is the one its diagnostics come from");
        CollectionAssert.AreEqual(new[] { App, Lib }, util.Projects.ToArray());
        Assert.HasCount(3, loaded.Documents.GetAllDocuments(), "Main, Lib and Util: one document for the file both list");
    }

    [TestMethod]
    public async Task AFileNoFolderOfTheWorkspaceHolds_IsRefused_AndSaidSo()
    {
        var loaded = await new Loaded(Files()).LoadAsync(App, App);

        CollectionAssert.AreEqual(new[] { "Main" }, loaded.Names(App));
        Assert.AreEqual((App, Path.Combine("..", "Shared", "Util.bas")), loaded.Projects.Refused.Single());
    }

    [TestMethod]
    public async Task AWorkspaceOfOneFolder_LoadsAsItDid()
    {
        var files = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(App, ProjectFile.FileName)] = ProjectOf(App, "App", "Main.bas"),
            [Path.Combine(App, "Main.bas")] = new("Attribute VB_Name = \"Main\"\r\n"),
        });

        var loaded = await new Loaded(files).LoadAsync(App, App);

        CollectionAssert.AreEqual(new[] { "Main" }, loaded.Names(App));
        Assert.IsEmpty(loaded.Projects.Refused);
        Assert.AreEqual(App, loaded.Documents.GetAllDocuments().Single().WorkspaceRoot);
    }

    [TestMethod]
    public async Task ARenamedModule_OfAnotherProject_StaysThatProjects()
    {
        var loaded = await new Loaded(Files()).LoadAsync(App, App, Shared, Lib);
        var lib = new Uri(Path.Combine(Lib, "Lib.bas"));
        var renamed = new Uri(Path.Combine(Lib, "Library.bas"));
        loaded.States.Track(new TextDocumentIdentifier(renamed));

        loaded.Documents.Rename(new TextDocumentIdentifier(lib), "Library.bas");

        Assert.IsTrue(loaded.Documents.TryGetDocument(renamed, out var document), "it is where its project's folder is, not under the primary project's");
        Assert.AreEqual(Lib, document.WorkspaceRoot);
        CollectionAssert.AreEqual(new[] { Lib }, document.Projects.ToArray());
        Assert.IsFalse(loaded.Documents.TryGetDocument(lib, out _));
    }

    [TestMethod]
    public async Task ARenamedSharedFile_StaysInItsFolder_AndAModuleOfEveryProjectThatListsIt()
    {
        var loaded = await new Loaded(Files()).LoadAsync(App, App, Shared, Lib);

        var renamed = new Uri(Path.Combine(Shared, "Tools.bas"));
        loaded.States.Track(new TextDocumentIdentifier(renamed));

        loaded.Documents.Rename(new TextDocumentIdentifier(new Uri(Util)), "Tools.bas");

        Assert.IsTrue(loaded.Documents.TryGetDocument(renamed, out var document));
        Assert.AreEqual(App, document.WorkspaceRoot);
        CollectionAssert.AreEqual(new[] { App, Lib }, document.Projects.ToArray());
    }

    [TestMethod]
    public async Task TheFirstFolder_IsAlwaysAProject()
    {
        var loaded = await new Loaded(Files()).LoadAsync(App, App, Shared, Lib);

        var roles = loaded.Projects.Roles().Select(entry => entry.Role).ToArray();

        CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source, WorkspaceFolderRole.Project }, roles);
    }
}
