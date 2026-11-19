using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Services.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services.States;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The language of a workspace whose client names none: a project imported from a Visual Basic 6 project is VB6, and any other stays the server's own. A
/// language the client names wins.
/// </summary>
[TestClass]
public sealed class WorkspaceLanguageTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-ws-language");

    [TestMethod]
    [DataRow("NavalBattle.vbp", null, "vb6", DisplayName = "a project imported from a .vbp, with no language named, is VB6")]
    [DataRow("NavalBattle.vbp", "{ \"Language\": \"vba\" }", "vba", DisplayName = "the language the client names wins")]
    [DataRow("NavalBattle.vbp", "{ \"Unattended\": true }", "vb6", DisplayName = "options that name no language leave it to the project")]
    [DataRow("NavalBattle.vbp", "{ \"Language\": \"cobol\" }", "vb6", DisplayName = "a language the platform does not serve is not named")]
    [DataRow(null, null, "vba", DisplayName = "a project that was not imported stays RD-VBA")]
    public async Task TheLanguageOfTheWorkspace_IsTheClientsOrElseTheProjectFilesOrElseTheServersOwn(string? sourceProject, string? options, string expected)
    {
        var workspace = new SdkWorkspaceOptions { WorkspaceUri = Root };
        var request = options is null ? new InitializeParams() : new InitializeParams { InitializationOptions = JObject.Parse(options) };
        var sut = Service(FileSystemWith(new RDCoreProject
        {
            Name = "NavalBattle",
            SourceProject = sourceProject,
            Modules = [new RDCoreModule { RelativeUri = "Module1.bas" }],
        }), () => workspace.SupportedLanguage);

        await sut.LoadAsync(Root, [Root], ask: null, CancellationToken.None, CoreLanguageServerApp.LanguageTakerOf(request, workspace));

        Assert.AreEqual(expected, workspace.Language);
    }

    // a folder with no project file, opened by the shell, whose lines are the platform's BASIC: the test expects the project made up for it to be the
    // folder's modules, the .bas and .cls files the shell's lines call, and not the shell's own .rdc programs, which are text files of lines.
    [TestMethod]
    [Ignore("Which files of a BASIC workspace are its modules is not defined: the SDK makes the folder a project of its .rdc files, the shell's programs.")]
    public async Task AFolderOpenedByTheShell_IsMadeAProjectOfItsModules()
    {
        var workspace = new SdkWorkspaceOptions { WorkspaceUri = Root };
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "Other.bas")] = new MockFileData("Attribute VB_Name = \"Other\"\r\nPublic Sub Divide()\r\nEnd Sub\r\n"),
            [Path.Combine(Root, "Thing.cls")] = new MockFileData("Attribute VB_Name = \"Thing\"\r\n"),
            [Path.Combine(Root, "Lines.rdc")] = new MockFileData("10 PRINT 1\r\n"),
        });
        var (sut, projectFiles) = ServiceAndProjectFiles(fs, () => workspace.SupportedLanguage);
        var request = new InitializeParams { InitializationOptions = JObject.Parse($"{{ \"Language\": \"{SupportedLanguages.BASIC.Id}\" }}") };

        await sut.LoadAsync(Root, [Root], ask: null, CancellationToken.None, CoreLanguageServerApp.LanguageTakerOf(request, workspace));

        Assert.AreEqual(SupportedLanguages.BASIC.Id, workspace.Language);
        Assert.IsTrue(projectFiles.Project.IsSynthesized);
        CollectionAssert.AreEqual(new[] { "Other.bas", "Thing.cls" },
            projectFiles.Project.ProjectInfo.Modules.Select(module => module.RelativeUri).ToArray());
    }

    // a folder of one Visual Basic 6 project with no project file of its own: the project made up for it says it is VB6's, and is made again of VB6's
    // source files once that is taken. A document module is RD-VBA's.
    [TestMethod]
    public async Task AFolderOfOneVbp_IsMadeAProjectOfVB6sSourceFiles()
    {
        var workspace = new SdkWorkspaceOptions { WorkspaceUri = Root };
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, "Probe.vbp")] = new MockFileData("Type=Exe\r\nModule=Module1; Module1.bas\r\n"),
            [Path.Combine(Root, "Module1.bas")] = new MockFileData("Attribute VB_Name = \"Module1\"\r\n"),
            [Path.Combine(Root, "Sheet1.doccls")] = new MockFileData("Attribute VB_Name = \"Sheet1\"\r\n"),
        });
        var (sut, projectFiles) = ServiceAndProjectFiles(fs, () => workspace.SupportedLanguage);

        await sut.LoadAsync(Root, [Root], ask: null, CancellationToken.None, CoreLanguageServerApp.LanguageTakerOf(new InitializeParams(), workspace));

        Assert.AreEqual(SupportedLanguages.VB6.Id, workspace.Language);
        Assert.IsTrue(projectFiles.Project.IsSynthesized);
        Assert.AreEqual("Probe.vbp", projectFiles.Project.ProjectInfo.SourceProject);
        CollectionAssert.AreEqual(new[] { "Module1.bas" }, projectFiles.Project.ProjectInfo.Modules.Select(module => module.RelativeUri).ToArray());
    }

    // a project imported from a Visual Basic 6 project once the workspace is open: its file says VB6 when it is read again.
    [TestMethod]
    public async Task AProjectFileReadAgain_ThatSaysAnotherLanguage_MakesTheWorkspaceItsLanguage_Once()
    {
        var workspace = new SdkWorkspaceOptions { WorkspaceUri = Root };
        var fs = FileSystemWith(new RDCoreProject { Name = "NavalBattle", Modules = [new RDCoreModule { RelativeUri = "Module1.bas" }] });
        var (sut, projectFiles) = ServiceAndProjectFiles(fs, () => workspace.SupportedLanguage);
        await sut.LoadAsync(Root, [Root], ask: null, CancellationToken.None, CoreLanguageServerApp.LanguageTakerOf(new InitializeParams(), workspace));
        var before = sut.RetakeLanguage();

        Imported(fs);
        await projectFiles.LoadAsync(Root);
        var taken = sut.RetakeLanguage();
        var again = sut.RetakeLanguage();

        Assert.IsNull(before);
        Assert.AreEqual(SupportedLanguages.VB6.Id, taken?.Id);
        Assert.IsNull(again);
        Assert.AreEqual(SupportedLanguages.VB6.Id, workspace.Language);
    }

    [TestMethod]
    public async Task AProjectFileReadAgain_ThatSaysAnotherLanguage_LeavesTheLanguageTheClientNamed()
    {
        var workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" };
        var request = new InitializeParams { InitializationOptions = JObject.Parse("{ \"Language\": \"vba\" }") };
        var fs = FileSystemWith(new RDCoreProject { Name = "NavalBattle", Modules = [new RDCoreModule { RelativeUri = "Module1.bas" }] });
        var (sut, projectFiles) = ServiceAndProjectFiles(fs, () => workspace.SupportedLanguage);
        await sut.LoadAsync(Root, [Root], ask: null, CancellationToken.None, CoreLanguageServerApp.LanguageTakerOf(request, workspace));

        Imported(fs);
        await projectFiles.LoadAsync(Root);

        Assert.IsNull(sut.RetakeLanguage());
        Assert.AreEqual("vba", workspace.Language);
    }

    // what rdcore.importProject writes: the project, with the Visual Basic 6 project it was imported from.
    private static void Imported(MockFileSystem fs)
        => fs.AddFile(Path.Combine(Root, ProjectFile.FileName), new MockFileData(JsonSerializer.Serialize(new ProjectFile(Root, new RDCoreProject
        {
            Name = "NavalBattle",
            SourceProject = "NavalBattle.vbp",
            Modules = [new RDCoreModule { RelativeUri = "Module1.bas" }],
        }))));

    private static MockFileSystem FileSystemWith(RDCoreProject project) => new(new Dictionary<string, MockFileData>
    {
        [Path.Combine(Root, ProjectFile.FileName)] = new MockFileData(JsonSerializer.Serialize(new ProjectFile(Root, project))),
        [Path.Combine(Root, "Module1.bas")] = new MockFileData("Attribute VB_Name = \"Module1\"\r\n"),
    });

    internal static WorkspaceService Service(MockFileSystem fs, Func<SupportedLanguage> language) => ServiceAndProjectFiles(fs, language).Workspace;

    // the workspace service, and the project file service it reads the project through.
    private static (WorkspaceService Workspace, ProjectFileService ProjectFiles) ServiceAndProjectFiles(MockFileSystem fs, Func<SupportedLanguage> language)
    {
        var state = Substitute.For<IServerStateProvider>();
        state.State.Returns(ServerState.Initializing);
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);
        var projectFiles = new ProjectFileService(NullLogger<ProjectFileService>.Instance, fs.Path, fs.File, new ProjectFileLoader(fs), language);
        return (new WorkspaceService(state, NullLogger<WorkspaceService>.Instance, fs.Path, fs.File, fs.Directory, projectFiles, documents, documentStates,
            language, new WorkspaceProjects(projectFiles), new ProjectFileLoader(fs)), projectFiles);
    }
}
