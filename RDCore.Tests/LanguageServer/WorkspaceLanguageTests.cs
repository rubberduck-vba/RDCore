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
    [DataRow("Battleship.vbp", null, "vb6", DisplayName = "a project imported from a .vbp, with no language named, is VB6")]
    [DataRow("Battleship.vbp", "{ \"Language\": \"vba\" }", "vba", DisplayName = "the language the client names wins")]
    [DataRow("Battleship.vbp", "{ \"Unattended\": true }", "vb6", DisplayName = "options that name no language leave it to the project")]
    [DataRow("Battleship.vbp", "{ \"Language\": \"cobol\" }", "vb6", DisplayName = "a language the platform does not serve is not named")]
    [DataRow(null, null, "vba", DisplayName = "a project that was not imported stays RD-VBA")]
    public async Task TheLanguageOfTheWorkspace_IsTheClientsOrElseTheProjectFilesOrElseTheServersOwn(string? sourceProject, string? options, string expected)
    {
        var workspace = new SdkWorkspaceOptions { WorkspaceUri = Root };
        var request = options is null ? new InitializeParams() : new InitializeParams { InitializationOptions = JObject.Parse(options) };
        var sut = Service(FileSystemWith(new RDCoreProject
        {
            Name = "Battleship",
            SourceProject = sourceProject,
            Modules = [new RDCoreModule { RelativeUri = "Module1.bas" }],
        }), () => workspace.SupportedLanguage);

        await sut.LoadAsync(Root, [Root], ask: null, CancellationToken.None, CoreLanguageServerApp.LanguageTakerOf(request, workspace));

        Assert.AreEqual(expected, workspace.Language);
    }

    private static MockFileSystem FileSystemWith(RDCoreProject project) => new(new Dictionary<string, MockFileData>
    {
        [Path.Combine(Root, ProjectFile.FileName)] = new MockFileData(JsonSerializer.Serialize(new ProjectFile(Root, project))),
        [Path.Combine(Root, "Module1.bas")] = new MockFileData("Attribute VB_Name = \"Module1\"\r\n"),
    });

    private static WorkspaceService Service(MockFileSystem fs, Func<SupportedLanguage> language)
    {
        var state = Substitute.For<IServerStateProvider>();
        state.State.Returns(ServerState.Initializing);
        var documentStates = new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance);
        var documents = new WorkspaceDocumentService(documentStates, NullLogger<WorkspaceDocumentService>.Instance, fs.Path, fs.File);
        var projectFiles = new ProjectFileService(NullLogger<ProjectFileService>.Instance, fs.Path, fs.File, new ProjectFileLoader(fs), language);
        return new WorkspaceService(state, NullLogger<WorkspaceService>.Instance, fs.Path, fs.File, fs.Directory, projectFiles, documents, documentStates,
            language, new WorkspaceProjects(projectFiles), new ProjectFileLoader(fs));
    }
}
