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

        // the file operations of a client, with the reload service doing what it does for the primary project: the project file is what the change makes of it, and the
        // workspace becomes what it lists.
        public WorkspaceFileOperationService FileOperations(MockFileSystem fileSystem, IProjectFileWriter writer)
        {
            var reload = Substitute.For<IWorkspaceReloadService>();
            reload.ChangeAsync(Arg.Any<Func<ProjectFile, ProjectFile>>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var before = ProjectFiles.Project;
                var after = ((Func<ProjectFile, ProjectFile>)call[0])(before);
                ProjectFiles.Update(after);

                TextDocumentIdentifier IdOf(string relative) => new(new Uri(fileSystem.Path.GetFullPath(fileSystem.Path.Combine(before.Uri, relative))));
                var listedBefore = before.ProjectInfo.Modules.Select(module => module.RelativeUri).ToHashSet();
                var listedAfter = after.ProjectInfo.Modules.Select(module => module.RelativeUri).ToHashSet();
                foreach (var removed in listedBefore.Except(listedAfter))
                {
                    Documents.Remove(IdOf(removed));
                }

                foreach (var added in listedAfter.Except(listedBefore))
                {
                    States.Track(IdOf(added));
                    await Documents.TryLoadAsync(IdOf(added));
                }

                return new WorkspaceReloadResult(listedAfter.Except(listedBefore).Count(), listedBefore.Except(listedAfter).Count(), false, false);
            });

            return new WorkspaceFileOperationService(
                reload, fileSystem.Path, fileSystem.File, fileSystem.Directory, () => SupportedLanguages.RDVBA, Projects, writer, Documents, States,
                NullLogger<WorkspaceFileOperationService>.Instance);
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

    // the client renames a file on disk and says so (workspace/didRenameFiles).
    private static async Task<(Loaded Loaded, IProjectFileWriter Writer)> RenamedAsync(string from, string to)
    {
        var files = Files();
        var loaded = await new Loaded(files).LoadAsync(App, App, Shared, Lib);
        var writer = Substitute.For<IProjectFileWriter>();
        files.File.Move(from, to);

        await loaded.FileOperations(files, writer).RenamedAsync([(new Uri(from), new Uri(to))], CancellationToken.None);

        return (loaded, writer);
    }

    private static string[] ListedBy(ProjectFile project) => [.. project.ProjectInfo.Modules.Select(module => module.RelativeUri.Replace('\\', '/')).Order()];

    [TestMethod]
    public async Task ARenamedModule_OfAnotherProject_IsListedUnderItsNewPath_ByThatProject()
    {
        var (loaded, writer) = await RenamedAsync(Path.Combine(Lib, "Lib.bas"), Path.Combine(Lib, "Library.bas"));

        var lib = loaded.Projects.Projects.Single(project => project.Root == Lib).Project;
        CollectionAssert.AreEqual(new[] { "../Shared/Util.bas", "Library.bas" }, ListedBy(lib));
        await writer.Received(1).SaveAsync(Arg.Is<ProjectFile>(project => project.Uri == Lib && ListedBy(project).Contains("Library.bas")), Arg.Any<CancellationToken>());
        Assert.IsTrue(loaded.Documents.TryGetDocument(new Uri(Path.Combine(Lib, "Library.bas")), out var document), "the document is the one of the new path");
        Assert.AreEqual(Lib, document.WorkspaceRoot, "of the project that lists it, not the primary one");
        CollectionAssert.AreEqual(new[] { Lib }, document.Projects.ToArray());
        Assert.IsFalse(loaded.Documents.TryGetDocument(new Uri(Path.Combine(Lib, "Lib.bas")), out _));
        CollectionAssert.AreEqual(new[] { "Main", "Util" }, loaded.Names(App), "the primary project is as it was");
    }

    [TestMethod]
    public async Task ARenamedSharedFile_IsListedUnderItsNewPath_ByEveryProjectThatListsIt()
    {
        var (loaded, writer) = await RenamedAsync(Util, Path.Combine(Shared, "Tools.bas"));

        CollectionAssert.AreEqual(new[] { "../Shared/Tools.bas", "Main.bas" }, ListedBy(loaded.ProjectFiles.Project), "the primary project");
        CollectionAssert.AreEqual(new[] { "../Shared/Tools.bas", "Lib.bas" }, ListedBy(loaded.Projects.Projects.Single(project => project.Root == Lib).Project), "and the other");
        await writer.Received(1).SaveAsync(Arg.Is<ProjectFile>(project => project.Uri == Lib), Arg.Any<CancellationToken>());
        Assert.IsTrue(loaded.Documents.TryGetDocument(new Uri(Path.Combine(Shared, "Tools.bas")), out var document));
        CollectionAssert.AreEqual(new[] { App, Lib }, document.Projects.ToArray(), "one document, a module of each");
        Assert.IsFalse(loaded.Documents.TryGetDocument(new Uri(Util), out _));
        Assert.IsEmpty(loaded.ProjectFiles.Project.ProjectInfo.Folders, "a file of another folder is in no folder of the project");
    }

    [TestMethod]
    public async Task AFolderThatHoldsAnImportedVbp_IsAProject_AndEveryOtherLaterFolderIsASourceFolder()
    {
        var other = Path.Combine(Base, "Other");
        var files = Files();
        files.AddFile(Path.Combine(Lib, "Lib.vbp"), new MockFileData("Type=Exe\r\n"));
        files.AddDirectory(other);

        var loaded = await new Loaded(files).LoadAsync(App, App, Lib, other);

        CollectionAssert.AreEqual(new[] { App, Lib }, loaded.Projects.Projects.Select(project => project.Root).ToArray(), "Other holds no .vbp, which an import would have left");
        CollectionAssert.AreEqual(
            new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Project, WorkspaceFolderRole.Source }, loaded.Projects.Roles().Select(entry => entry.Role).ToArray());
    }

    [TestMethod]
    public async Task TheFirstFolder_IsAlwaysAProject()
    {
        var loaded = await new Loaded(Files()).LoadAsync(App, App, Shared, Lib);

        var roles = loaded.Projects.Roles().Select(entry => entry.Role).ToArray();

        CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source, WorkspaceFolderRole.Project }, roles);
    }
}
