using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Commands;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Tests.Runtime.Libraries;
using System.IO.Abstractions.TestingHelpers;
using System.Text;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>rdcore.importProject</c> makes the <c>.rdproj</c> of a VB6 project in the folder of its <c>.vbp</c>: the one it names, or the one of a folder, which the
/// user chooses among several; a project file there is replaced only once the user says so.
/// </summary>
[TestClass]
public sealed class ImportProjectCommandTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-import-ws");
    private static readonly string Other = Path.Combine(Path.GetTempPath(), "rdcore-import-other");

    private const string Battleship = "Name=\"Battleship\"\r\nModule=modMain; modMain.bas\r\nObject={831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0; MSCOMCTL.OCX\r\n";

    private readonly MockFileSystem _fileSystem = new();
    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IWorkspaceReloadService _reload = Substitute.For<IWorkspaceReloadService>();
    private readonly IProjectFileWriter _writer = Substitute.For<IProjectFileWriter>();
    private readonly ILanguageServerFacade _server = Substitute.For<ILanguageServerFacade>();
    private readonly Answers _context = new();

    public ImportProjectCommandTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        _fileSystem.AddDirectory(Root);
        _fileSystem.AddDirectory(Other);
        _reload.ChangeAsync(Arg.Any<Func<ProjectFile, ProjectFile>>(), Arg.Any<CancellationToken>()).Returns(WorkspaceReloadResult.Unchanged);
    }

    // what the user is asked, and what the user answers: the next answer, or none.
    private sealed class Answers : IServerCommandContext
    {
        public List<(string Message, IReadOnlyList<string> Choices)> Asked { get; } = [];
        public Queue<string?> Next { get; } = new();

        public Task<string?> AskAsync(string message, IReadOnlyList<string> choices, CancellationToken token)
        {
            Asked.Add((message, choices));
            return Task.FromResult(Next.Count > 0 ? Next.Dequeue() : null);
        }
    }

    private ImportProjectCommand Sut()
        => new(_documents, _reload, _writer, new ProjectFileLoader(_fileSystem), new InMemoryLibrarySource(), _fileSystem, Options.Create(new SdkAppOptions()), _server);

    private void Write(string path, string text) => _fileSystem.AddFile(path, new MockFileData(Encoding.Latin1.GetBytes(text)));

    private static JArray Argument(JToken argument) => new(argument);

    private static string Uri(string path) => DocumentUri.FromFileSystemPath(path).ToString();

    [TestMethod]
    public async Task AVbpOfTheWorkspacesFolder_IsTheWorkspacesProject_AsTheServerWritesIt()
    {
        Write(Path.Combine(Root, "Battleship.vbp"), Battleship);

        var answer = (JObject)(await Sut().ExecuteAsync(Argument(Uri(Path.Combine(Root, "Battleship.vbp"))), _context, CancellationToken.None))!;

        var change = (Func<ProjectFile, ProjectFile>)_reload.ReceivedCalls().Single().GetArguments()[0]!;
        var project = change(new ProjectFile(Root)).ProjectInfo;
        Assert.AreEqual("Battleship", project.Name);
        CollectionAssert.AreEqual(new[] { "modMain.bas" }, project.Modules.Select(module => module.RelativeUri).ToArray());
        await _writer.DidNotReceive().SaveAsync(Arg.Any<ProjectFile>(), Arg.Any<CancellationToken>());

        Assert.AreEqual("Battleship", answer.Value<string>("name"));
        Assert.AreEqual(Uri(Path.Combine(Root, ProjectFile.FileName)), answer.Value<string>("projectFile"));
        var note = (JObject)answer["notes"]!.Single();
        Assert.AreEqual("Control", note.Value<string>("kind"));
        Assert.AreEqual("MSCOMCTL.OCX", note.Value<string>("subject"));
        Assert.IsFalse(string.IsNullOrEmpty(note.Value<string>("message")));
        Assert.IsEmpty(_context.Asked, "nothing to replace, nothing to choose");
    }

    // a client that keeps a .vbp and a .rdproj in step imports the .vbp again after each change, asks its user nothing, and loses none of the platform's
    // own settings.
    [TestMethod]
    public async Task AReimportThatOverwrites_AsksNothing_AndKeepsWhatTheVbpDoesNotDescribe()
    {
        Write(Path.Combine(Other, "Battleship.vbp"), Battleship);
        await new ProjectFileWriter(_fileSystem).SaveAsync(new ProjectFile(Other, new RDCoreProject { Name = "Old" }) with { Configuration = ["rdcore.settings.json"] });

        await Sut().ExecuteAsync(Argument(new JObject { ["uri"] = Uri(Path.Combine(Other, "Battleship.vbp")), ["overwrite"] = true }), _context, CancellationToken.None);

        Assert.IsEmpty(_context.Asked, "no window/showMessageRequest");
        await _writer.Received(1).SaveAsync(Arg.Is<ProjectFile>(project => project.ProjectInfo.Name == "Battleship"
            && project.Configuration.SequenceEqual(new[] { "rdcore.settings.json" })), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AReimportOfTheWorkspacesFolder_KeepsWhatTheVbpDoesNotDescribe()
    {
        Write(Path.Combine(Root, "Battleship.vbp"), Battleship);

        await Sut().ExecuteAsync(Argument(new JObject { ["uri"] = Uri(Path.Combine(Root, "Battleship.vbp")), ["overwrite"] = true }), _context, CancellationToken.None);

        var change = (Func<ProjectFile, ProjectFile>)_reload.ReceivedCalls().Single().GetArguments()[0]!;
        var project = change(new ProjectFile(Root, new RDCoreProject { Name = "Old" }) with { Configuration = ["rdcore.settings.json"] });
        Assert.AreEqual("Battleship", project.ProjectInfo.Name);
        CollectionAssert.AreEqual(new[] { "rdcore.settings.json" }, project.Configuration);
    }

    [TestMethod]
    public async Task AProjectFileThatCannotBeRead_IsReplacedWhole()
    {
        Write(Path.Combine(Other, "Battleship.vbp"), Battleship);
        Write(Path.Combine(Other, ProjectFile.FileName), "this is no JSON");

        await Sut().ExecuteAsync(Argument(new JObject { ["uri"] = Uri(Path.Combine(Other, "Battleship.vbp")), ["overwrite"] = true }), _context, CancellationToken.None);

        await _writer.Received(1).SaveAsync(Arg.Is<ProjectFile>(project => project.ProjectInfo.Name == "Battleship" && project.Configuration.Length == 0),
            Arg.Any<CancellationToken>());
    }

    // the workspace an import changes is changed as any change of the project is, which asks the client to refresh what it shows (WorkspaceReloadServiceTests).
    [TestMethod]
    public async Task AVbpOfAnotherFolder_IsWrittenThere()
    {
        Write(Path.Combine(Other, "Battleship.vbp"), Battleship);

        await Sut().ExecuteAsync(Argument(Uri(Path.Combine(Other, "Battleship.vbp"))), _context, CancellationToken.None);

        // the folder is the one of the .vbp's URI, whose drive a file URI spells in lowercase.
        await _writer.Received(1).SaveAsync(Arg.Is<ProjectFile>(project => string.Equals(project.Uri, Other, StringComparison.OrdinalIgnoreCase) && project.ProjectInfo.Name == "Battleship"),
            Arg.Any<CancellationToken>());
        await _reload.DidNotReceive().ChangeAsync(Arg.Any<Func<ProjectFile, ProjectFile>>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task TheTextTheClientSends_IsTheVbps()
    {
        await Sut().ExecuteAsync(Argument(new JObject { ["uri"] = Uri(Path.Combine(Other, "Unsaved.vbp")), ["text"] = "Name=\"Unsaved\"" }),
            _context, CancellationToken.None);

        await _writer.Received(1).SaveAsync(Arg.Is<ProjectFile>(project => project.ProjectInfo.Name == "Unsaved"), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(null, DisplayName = "the user chooses nothing")]
    [DataRow("Cancel", DisplayName = "the user cancels")]
    public async Task AProjectFileThere_IsNotReplaced_UnlessTheUserSaysSo(string? answer)
    {
        Write(Path.Combine(Other, "Battleship.vbp"), Battleship);
        Write(Path.Combine(Other, ProjectFile.FileName), "{}");
        _context.Next.Enqueue(answer);

        await Assert.ThrowsAsync<ServerCommandRefusedException>(() => Sut().ExecuteAsync(Argument(Uri(Path.Combine(Other, "Battleship.vbp"))), _context, CancellationToken.None));

        Assert.HasCount(1, _context.Asked);
        CollectionAssert.AreEqual(new[] { VbpImportText.Replace(), VbpImportText.Cancel() }, _context.Asked[0].Choices.ToArray());
        await _writer.DidNotReceive().SaveAsync(Arg.Any<ProjectFile>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AProjectFileThere_IsReplaced_WhenTheUserSaysSo_OrTheClientSaysOverwrite()
    {
        Write(Path.Combine(Other, "Battleship.vbp"), Battleship);
        Write(Path.Combine(Other, ProjectFile.FileName), "{}");
        _context.Next.Enqueue(VbpImportText.Replace());

        await Sut().ExecuteAsync(Argument(Uri(Path.Combine(Other, "Battleship.vbp"))), _context, CancellationToken.None);
        await Sut().ExecuteAsync(Argument(new JObject { ["uri"] = Uri(Path.Combine(Other, "Battleship.vbp")), ["overwrite"] = true }), _context, CancellationToken.None);

        Assert.HasCount(1, _context.Asked, "overwrite asks nothing");
        await _writer.Received(2).SaveAsync(Arg.Any<ProjectFile>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AFolderOfSeveralProjects_AsksWhichOne()
    {
        Write(Path.Combine(Other, "Alpha.vbp"), "Name=\"Alpha\"");
        Write(Path.Combine(Other, "Bravo.vbp"), "Name=\"Bravo\"");
        _context.Next.Enqueue("Bravo.vbp");

        await Sut().ExecuteAsync(Argument(Uri(Other)), _context, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Alpha.vbp", "Bravo.vbp" }, _context.Asked.Single().Choices.ToArray());
        await _writer.Received(1).SaveAsync(Arg.Is<ProjectFile>(project => project.ProjectInfo.Name == "Bravo"), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task NoArgument_IsTheWorkspacesFolder()
    {
        Write(Path.Combine(Root, "Battleship.vbp"), Battleship);

        await Sut().ExecuteAsync(null, _context, CancellationToken.None);

        await _reload.Received(1).ChangeAsync(Arg.Any<Func<ProjectFile, ProjectFile>>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AFolderWithNoProject_IsRefused()
        => await Assert.ThrowsAsync<ServerCommandRefusedException>(() => Sut().ExecuteAsync(Argument(Uri(Other)), _context, CancellationToken.None));
}
