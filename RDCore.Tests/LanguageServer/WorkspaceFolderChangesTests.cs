using System.IO.Abstractions.TestingHelpers;
using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Server;
using RDCore.LanguageServer.Server.Handlers.Workspace;
using RDCore.LanguageServer.Services.Workspace;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services.States;
using RDCore.SDK.Workspace;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <strong>LSP 3.17</strong> <c>workspace/didChangeWorkspaceFolders</c>: a folder the client adds once the server is initialized is loaded as one it gave at
/// initialize is, and the project of one it removes is dropped, with what only that project listed.
/// </summary>
[TestClass]
public sealed class WorkspaceFolderChangesTests
{
    // a cap on a hang, not a budget: each wait is on the change itself, which a loaded machine makes as late as it does.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

    private static readonly string Base = Path.Combine(Path.GetTempPath(), "rdcore-folder-changes");
    private static readonly string Client = Path.Combine(Base, "Client");
    private static readonly string Service = Path.Combine(Base, "Service");
    private static readonly string Shared = Path.Combine(Base, "Shared");

    private static readonly string Util = Path.Combine(Shared, "Util.bas");
    private static readonly string UtilAsListed = Path.Combine("..", "Shared", "Util.bas");

    private static MockFileData ProjectOf(string root, string name, params string[] modules)
        => new(JsonSerializer.Serialize(new ProjectFile(root, new RDCoreProject
        {
            Name = name,
            Modules = [.. modules.Select(module => new RDCoreModule { RelativeUri = module })],
        })));

    // Client lists its own Client.bas and ..\Shared\Util.bas; Service lists Service.bas, Broken.bas and the same Util.bas; Shared has no project of its own.
    private static MockFileSystem Files() => new(new Dictionary<string, MockFileData>
    {
        [Path.Combine(Client, ProjectFile.FileName)] = ProjectOf(Client, "Client", "Client.bas", UtilAsListed),
        [Path.Combine(Client, "Client.bas")] = new("Attribute VB_Name = \"Client\"\r\n"),
        [Path.Combine(Service, ProjectFile.FileName)] = ProjectOf(Service, "Service", "Service.bas", "Broken.bas", UtilAsListed),
        [Path.Combine(Service, "Service.bas")] = new("Attribute VB_Name = \"Service\"\r\n"),
        [Path.Combine(Service, "Broken.bas")] = new("Attribute VB_Name = \"Broken\"\r\nPublic Sub (\r\n"),
        [Util] = new("Attribute VB_Name = \"Util\"\r\n"),
    });

    // the workspace as the language server has it: loaded as the server initializes, and changed once it runs.
    private sealed class Workspace
    {
        private readonly IServerStateProvider _state = Substitute.For<IServerStateProvider>();
        private readonly TaskCompletionSource _refreshed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Workspace(MockFileSystem files)
        {
            _state.State.Returns(ServerState.Initializing);
            ProjectFiles = new ProjectFileService(NullLogger<ProjectFileService>.Instance, files.Path, files.File, new ProjectFileLoader(files), () => SupportedLanguages.RDVBA);
            Documents = new WorkspaceDocumentService(States, NullLogger<WorkspaceDocumentService>.Instance, files.Path, files.File);
            Projects = new WorkspaceProjects(ProjectFiles);
            Loader = new WorkspaceService(
                _state, NullLogger<WorkspaceService>.Instance, files.Path, files.File, files.Directory, ProjectFiles, Documents, States,
                () => ProtocolSupportedLanguage.VBA, Projects, new ProjectFileLoader(files));

            Parsing.ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(new ModuleParser().Parse((Uri)call[0], "Public Sub Foo()\r\nEnd Sub")));
            var host = Substitute.For<IRDCoreClientApp>();
            host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(ProjectReload)] });
            host.SendRequestAsync<HostProjectReloadParams, HostProjectReloadResult>(Arg.Any<HostProjectReloadParams>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new HostProjectReloadResult { Composed = true }));
            var orchestration = Substitute.For<IPlatformOrchestrationService>();
            orchestration.RuntimeEnvironment.Returns(host);

            // the change is done once the client is asked to refresh what it shows: the documents are parsed, and the symbols defined, by then.
            Refresh.When(refresh => refresh.Changed()).Do(_ => _refreshed.TrySetResult());
            Reload = new WorkspaceReloadService(
                _state, ProjectFiles, Documents, Loader, States, Parsing, Symbols, orchestration, files.Path, Refresh, NullLogger<WorkspaceReloadService>.Instance);
            Changes = new WorkspaceFolderChanges(Reload, Loader, Projects, NullLogger<WorkspaceFolderChanges>.Instance);
        }

        public ProjectFileService ProjectFiles { get; }
        public DocumentStateProvider States { get; } = new(NullLogger<DocumentStateProvider>.Instance);
        public WorkspaceDocumentService Documents { get; }
        public WorkspaceProjects Projects { get; }
        public WorkspaceService Loader { get; }
        public IParsingClientService Parsing { get; } = Substitute.For<IParsingClientService>();
        public ISymbolSyncService Symbols { get; } = Substitute.For<ISymbolSyncService>();
        public IWorkspaceRefresh Refresh { get; } = Substitute.For<IWorkspaceRefresh>();
        public WorkspaceReloadService Reload { get; }
        public WorkspaceFolderChanges Changes { get; }
        public ILanguageServerFacade Server { get; } = Substitute.For<ILanguageServerFacade>();

        // the server initializes on the folders the client gives, and runs.
        public async Task<Workspace> LoadAsync(string root, params string[] folders)
        {
            await Loader.LoadAsync(root, folders);
            _state.State.Returns(ServerState.Running);
            return this;
        }

        public Task ChangeAsync(string[] added, string[] removed) => Changes.ChangedAsync(added, removed, Server);

        public Task Refreshed => _refreshed.Task.WaitAsync(SafetyCap);

        public string[] Names(string project) => [.. Documents.GetProjectDocuments(project).Select(document => document.Name).Order()];

        public string[] Roots => [.. Projects.Projects.Select(project => project.Root)];

        public string[] ToldTheClient => [.. Server.Window.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<ShowMessageParams>().Select(message => message.Message)];
    }

    private static readonly string ServiceReadme = Path.Combine(Service, "README.md");

    // the workspace of Files(), where Service also lists a README as another file.
    private static MockFileSystem FilesWithServiceReadme()
    {
        var files = Files();
        files.AddFile(Path.Combine(Service, ProjectFile.FileName), new MockFileData(JsonSerializer.Serialize(new ProjectFile(Service, new RDCoreProject
        {
            Name = "Service",
            Modules = [new RDCoreModule { RelativeUri = "Service.bas" }, new RDCoreModule { RelativeUri = UtilAsListed }],
            OtherFiles = [new RDCoreFile { RelativeUri = "README.md" }],
        }))));
        files.AddFile(ServiceReadme, new MockFileData("# Service"));
        return files;
    }

    // the primary project's file changes on disk (it gains a module), and the workspace is reloaded from it, as a watched .rdproj is.
    private static async Task ReloadAfterThePrimaryProjectChangesAsync(Workspace workspace, MockFileSystem files)
    {
        files.AddFile(Path.Combine(Client, "Extra.bas"), new MockFileData("Attribute VB_Name = \"Extra\"\r\n"));
        files.AddFile(Path.Combine(Client, ProjectFile.FileName), ProjectOf(Client, "Client", "Client.bas", UtilAsListed, "Extra.bas"));
        Assert.IsNotNull(await workspace.Reload.ReloadAsync(CancellationToken.None), "the workspace is reloaded");
        CollectionAssert.Contains(workspace.Names(Client), "Extra", "the reload applied the primary project's file");
    }

    // a reload says which documents are other files for every project at once: the other projects' are kept, not only the primary one's.
    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    [DataRow(true, DisplayName = "a folder added once the server runs")]
    [DataRow(false, DisplayName = "a folder given at initialize")]
    public async Task AnOtherProjectsOtherFile_StaysAnOtherFile_WhenThePrimaryProjectIsReloaded(bool addedLater)
    {
        var files = FilesWithServiceReadme();
        var workspace = addedLater
            ? await new Workspace(files).LoadAsync(Client, Client, Shared)
            : await new Workspace(files).LoadAsync(Client, Client, Shared, Service);
        if (addedLater)
        {
            await workspace.ChangeAsync([Service], []);
        }

        var readme = new Uri(ServiceReadme);
        Assert.IsTrue(workspace.Documents.TryGetDocument(readme, out _), "Service's README is loaded");
        Assert.IsFalse(workspace.Documents.TryGetModule(readme, out _), "and is no module before the reload");

        await ReloadAfterThePrimaryProjectChangesAsync(workspace, files);

        Assert.IsTrue(workspace.Documents.TryGetDocument(readme, out _), "Service's README is still loaded");
        Assert.IsFalse(workspace.Documents.TryGetModule(readme, out _), "and is still no module: the reload kept Service's other files");
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AProjectFolderAdded_IsLoadedAsAProject_AndItsModulesAreLoadedAndParsed()
    {
        var workspace = await new Workspace(Files()).LoadAsync(Client, Client, Shared);
        var util = new TextDocumentIdentifier(new Uri(Util));
        Assert.IsTrue(workspace.Documents.TryGetDocument(util.Uri.ToUri(), out var utilBefore));

        await workspace.ChangeAsync([Service], []);

        CollectionAssert.AreEqual(new[] { Client, Service }, workspace.Roots);
        Assert.AreEqual("Service", workspace.Projects.Projects[1].Project.ProjectInfo.Name);
        CollectionAssert.AreEqual(new[] { "Broken", "Service", "Util" }, workspace.Names(Service));
        CollectionAssert.AreEqual(new[] { "Client", "Util" }, workspace.Names(Client), "the primary project is as it was");
        Assert.IsTrue(workspace.Documents.TryGetDocument(util.Uri.ToUri(), out var utilAfter));
        CollectionAssert.AreEqual(new[] { Client, Service }, utilAfter.Projects.ToArray(), "one document, a module of each project that lists it");
        Assert.AreEqual(utilBefore.Version, utilAfter.Version, "a document loaded already is not read again");
        Assert.IsInstanceOfType<LoadedDocumentState>(workspace.States.GetCurrentState(new TextDocumentIdentifier(new Uri(Path.Combine(Service, "Broken.bas")))));

        await workspace.Refreshed;
        await workspace.Parsing.Received(1).ParseDocumentAsync(new Uri(Path.Combine(Service, "Service.bas")), Arg.Any<CancellationToken>());
        await workspace.Parsing.Received(1).ParseDocumentAsync(new Uri(Path.Combine(Service, "Broken.bas")), Arg.Any<CancellationToken>());
        await workspace.Parsing.DidNotReceive().ParseDocumentAsync(new Uri(Util), Arg.Any<CancellationToken>());
        Assert.IsEmpty(workspace.ToldTheClient);
    }

    [TestMethod]
    public async Task AProjectFolderAdded_IsWhatItIs_GivenAtInitialize()
    {
        var added = await new Workspace(Files()).LoadAsync(Client, Client, Shared);
        await added.ChangeAsync([Service], []);

        var given = await new Workspace(Files()).LoadAsync(Client, Client, Shared, Service);

        CollectionAssert.AreEqual(given.Roots, added.Roots);
        CollectionAssert.AreEqual(given.Projects.Roles().ToArray(), added.Projects.Roles().ToArray());
        CollectionAssert.AreEqual(given.Names(Service), added.Names(Service));
        CollectionAssert.AreEqual(given.Names(Client), added.Names(Client));
        CollectionAssert.AreEqual(given.Projects.Refused.ToArray(), added.Projects.Refused.ToArray());
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task ASourceFolderAdded_HasTheFileAProjectListsInItLoaded_ThatWasRefused()
    {
        var workspace = await new Workspace(Files()).LoadAsync(Client, Client);
        Assert.AreEqual((Client, UtilAsListed), workspace.Projects.Refused.Single(), "the premise: no folder held it");

        await workspace.ChangeAsync([Shared], []);

        CollectionAssert.AreEqual(new[] { Client }, workspace.Roots, "a folder that holds sources a project lists is no project");
        CollectionAssert.AreEqual(new[] { WorkspaceFolderRole.Project, WorkspaceFolderRole.Source }, workspace.Projects.Roles().Select(entry => entry.Role).ToArray());
        CollectionAssert.AreEqual(new[] { "Client", "Util" }, workspace.Names(Client));
        Assert.IsEmpty(workspace.Projects.Refused);

        await workspace.Refreshed;
        await workspace.Parsing.Received(1).ParseDocumentAsync(new Uri(Util), Arg.Any<CancellationToken>());
        await workspace.Symbols.Received().SyncWorkspaceAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<RDCore.SDK.Server.Progress.WorkProgressStep>?>(), Arg.Is(true));
    }

    [TestMethod]
    public async Task AProjectFolderRemoved_HasItsModulesUnloaded_AndKeepsTheFileAnotherProjectLists()
    {
        var workspace = await new Workspace(Files()).LoadAsync(Client, Client, Shared, Service);

        await workspace.ChangeAsync([], [Service]);

        CollectionAssert.AreEqual(new[] { Client }, workspace.Roots);
        Assert.IsFalse(workspace.Projects.Folders.Contains(Path.Combine(Service, "Service.bas")), "the folder left the workspace");
        foreach (var module in new[] { "Service.bas", "Broken.bas" })
        {
            var id = new TextDocumentIdentifier(new Uri(Path.Combine(Service, module)));
            Assert.IsFalse(workspace.Documents.TryGetDocument(id.Uri.ToUri(), out _), module);
            Assert.IsFalse(workspace.States.IsTracked(id), module);
        }

        Assert.IsTrue(workspace.Documents.TryGetDocument(new Uri(Util), out var util));
        CollectionAssert.AreEqual(new[] { Client }, util.Projects.ToArray());
        CollectionAssert.AreEqual(new[] { "Client", "Util" }, workspace.Names(Client));
        Assert.IsEmpty(workspace.ToldTheClient);
    }

    [TestMethod]
    public async Task ThePrimaryFolderRemoved_IsKept_WithItsProject()
    {
        var workspace = await new Workspace(Files()).LoadAsync(Client, Client, Shared);

        await workspace.ChangeAsync([], [Client]);

        CollectionAssert.AreEqual(new[] { Client }, workspace.Roots);
        Assert.IsTrue(workspace.Projects.Folders.Contains(Path.Combine(Client, "Client.bas")), "its folder is still one of the workspace");
        CollectionAssert.AreEqual(new[] { "Client", "Util" }, workspace.Names(Client));
        Assert.IsEmpty(workspace.Projects.Refused);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task ASourceFolderRemoved_RefusesTheFileAProjectListsInIt_AndTellsTheClient()
    {
        var workspace = await new Workspace(Files()).LoadAsync(Client, Client, Shared);

        await workspace.ChangeAsync([], [Shared]);

        CollectionAssert.AreEqual(new[] { "Client" }, workspace.Names(Client));
        Assert.IsFalse(workspace.Documents.TryGetDocument(new Uri(Util), out _));
        Assert.AreEqual((Client, UtilAsListed), workspace.Projects.Refused.Single());
        Assert.HasCount(1, workspace.ToldTheClient);
        StringAssert.Contains(workspace.ToldTheClient[0], UtilAsListed);

        // the module the primary project no longer has is taken out of the environment host.
        await workspace.Refreshed;
        await workspace.Symbols.Received(1).DiscardModuleAsync(Arg.Is("Util"), Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    [TestMethod]
    public async Task TheFoldersGivenAtInitialize_SaidAgain_ChangeNothing()
    {
        // OmniSharp's workspace folder manager says the folders of initialize once the server started.
        var workspace = await new Workspace(Files()).LoadAsync(Client, Client, Shared);

        await workspace.ChangeAsync([Client], []);
        await workspace.ChangeAsync([Shared], []);

        CollectionAssert.AreEqual(new[] { Client }, workspace.Roots);
        CollectionAssert.AreEqual(new[] { "Client", "Util" }, workspace.Names(Client));
        workspace.Refresh.DidNotReceive().Changed();
        await workspace.Parsing.DidNotReceive().ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    // the client adds the folder with the notification itself, which OmniSharp's workspace folder manager reads: the server that answered initialize still says it
    // takes it (workspace.workspaceFolders.changeNotifications).
    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AProjectFolderAdded_OverTheWire_IsLoadedAsAProject()
    {
        using var timeout = new CancellationTokenSource(SafetyCap);
        var workspace = new Workspace(Files());
        var toServer = new Pipe();
        var toClient = new Pipe();
        IDisposable? attached = null;
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader).WithOutput(toClient.Writer)
            .OnInitialize(async (server, request, token) =>
            {
                await workspace.LoadAsync(Client, [.. request.WorkspaceFolders!.Select(folder => folder.Uri.GetFileSystemPath())]);
                attached = workspace.Changes.Attach(server.WorkspaceFolderManager, server);
            }), timeout.Token);
        var connecting = LanguageClient.From(client => client
            .WithInput(toClient.Reader).WithOutput(toServer.Writer)
            .WithRootUri(DocumentUri.FromFileSystemPath(Client))
            .WithWorkspaceFolder(DocumentUri.FromFileSystemPath(Client), "Client")
            .WithWorkspaceFolder(DocumentUri.FromFileSystemPath(Shared), "Shared"), timeout.Token);

        await Task.WhenAll(serving, connecting).WaitAsync(SafetyCap);
        using var server = await serving;
        using var client = await connecting;
        Assert.IsTrue(client.ServerSettings.Capabilities.Workspace?.WorkspaceFolders is { Supported: true, ChangeNotifications: { IsBool: true, Bool: true } },
            "the server says it takes the notification");

        client.WorkspaceFoldersManager.Add(DocumentUri.FromFileSystemPath(Service), "Service");
        await workspace.Refreshed;

        // the folder is the one the client named, however its address spells the drive.
        Assert.HasCount(2, workspace.Roots);
        Assert.IsTrue(WorkspacePaths.Same(Service, workspace.Roots[1]), workspace.Roots[1]);
        CollectionAssert.AreEqual(new[] { "Broken", "Service", "Util" }, workspace.Names(Service));
        attached?.Dispose();
    }
}
