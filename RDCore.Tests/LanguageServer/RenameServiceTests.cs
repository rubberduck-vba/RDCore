using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A rename of the server renames a symbol in every document of the workspace, in the text the client has: a program of the platform's BASIC is its lines.
/// </summary>
[TestClass]
public sealed class RenameServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-rename-ws");

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    private const string Helpers = """
        Attribute VB_Name = "Helpers"
        Public strTitle As String

        Public Sub Show()
            Debug.Print strTitle
        End Sub
        """;

    private const string Program = "5 Dim count As Long\r\n10 strTitle = \"Hello\"\r\n20 Debug.Print strTitle\r\n";

    public RenameServiceTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        var helpers = new WorkspaceDocument("Helpers.bas", Root, Helpers, version: 3);
        var program = new WorkspaceDocument("Main.rdc", Root, Program, version: 7);
        _documents.GetAllDocuments().Returns([helpers, program]);

        // the client has every document open, unless a test says otherwise.
        _documents.IsOpen(Arg.Any<TextDocumentIdentifier>()).Returns(true);
        foreach (var document in new[] { helpers, program })
        {
            var uri = document.Id.Uri.ToUri();
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(document))));
        }
    }

    private RenameService Sut(IWorkspaceProjects? projects = null)
    {
        var source = new InMemoryLibrarySource();
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(source, [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        return new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, projects ?? OneProject.Registry());
    }

    private static Uri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName)).ToUri();

    [TestMethod]
    public async Task ARename_InAProgram_EditsItsLines_AndTheModulesOfTheWorkspace_AtTheirVersions()
    {
        var answer = await Sut().RenameAsync(Document("Main.rdc"), new Position(2, 15), "Title", CancellationToken.None);

        Assert.IsNotNull(answer.Value, answer.Refusal);
        var changes = answer.Value.DocumentChanges!.Select(change => change.TextDocumentEdit!).ToList();
        var program = changes.Single(change => change.TextDocument.Uri.ToUri().AbsoluteUri == Document("Main.rdc").AbsoluteUri);
        var helpers = changes.Single(change => change.TextDocument.Uri.ToUri().AbsoluteUri == Document("Helpers.bas").AbsoluteUri);

        Assert.AreEqual(7, program.TextDocument.Version);
        CollectionAssert.AreEqual(new[] { new Range(1, 3, 1, 11), new Range(2, 15, 2, 23) }, program.Edits.Select(edit => edit.Range).ToArray(), "the lines of the program, not of its module");
        Assert.AreEqual(3, helpers.TextDocument.Version);
        CollectionAssert.AreEqual(new[] { new Range(1, 7, 1, 15), new Range(4, 16, 4, 24) }, helpers.Edits.Select(edit => edit.Range).ToArray());
        Assert.IsTrue(changes.SelectMany(change => change.Edits).All(edit => edit.NewText == "Title"));
    }

    // a module the client never opened has no version the client knows: its edits name none, as null on the wire, and are of the file as it is on disk.
    [TestMethod]
    public async Task ARename_ThatEditsAModuleTheClientHasNotOpened_NamesNoVersionForIt()
    {
        _documents.IsOpen(Arg.Is<TextDocumentIdentifier>(id => id.Uri.ToUri().AbsoluteUri == Document("Helpers.bas").AbsoluteUri)).Returns(false);

        var answer = await Sut().RenameAsync(Document("Main.rdc"), new Position(2, 15), "Title", CancellationToken.None);

        Assert.IsNotNull(answer.Value, answer.Refusal);
        var changes = answer.Value.DocumentChanges!.Select(change => change.TextDocumentEdit!).ToList();
        var program = changes.Single(change => change.TextDocument.Uri.ToUri().AbsoluteUri == Document("Main.rdc").AbsoluteUri);
        var helpers = changes.Single(change => change.TextDocument.Uri.ToUri().AbsoluteUri == Document("Helpers.bas").AbsoluteUri);
        Assert.AreEqual(7, program.TextDocument.Version, "the module the client has open, at its version");
        Assert.IsNull(helpers.TextDocument.Version);
        Assert.HasCount(2, helpers.Edits, "the module is renamed all the same");

        var wire = Newtonsoft.Json.Linq.JObject.Parse(new RDCore.SDK.Server.Serialization.PullDiagnosticsLspSerializer().SerializeObject(helpers));
        Assert.IsTrue(((Newtonsoft.Json.Linq.JObject)wire["textDocument"]!).TryGetValue("version", out var version), wire.ToString());
        Assert.AreEqual(Newtonsoft.Json.Linq.JTokenType.Null, version!.Type, "null, as LSP 3.17 says, and not left out");
    }

    [TestMethod]
    public async Task APreparedRename_SaysTheNameAsItIsDeclared()
    {
        var answer = await Sut().PrepareAsync(Document("Main.rdc"), new Position(1, 5), CancellationToken.None);

        Assert.IsNotNull(answer.Value, answer.Refusal);
        Assert.AreEqual(new Range(1, 3, 1, 11), answer.Value.Range);
        Assert.AreEqual("strTitle", answer.Value.Placeholder);
    }

    [TestMethod]
    public async Task ARefusedRename_SaysWhere_TheWayTheProgramNumbersItsLines()
    {
        var culture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            var answer = await Sut().RenameAsync(Document("Helpers.bas"), new Position(1, 9), "count", CancellationToken.None);

            Assert.IsNull(answer.Value);
            Assert.AreEqual("'count' already means Main.Main.count at Main.rdc 10: the rename would change what the code refers to.", answer.Refusal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }

    [TestMethod]
    public async Task AModuleNamedLikeItsFile_IsRenamedWithItsFile_AfterItsTextIsEdited()
    {
        // the VB_Name of Helpers.bas, renamed from its own attribute.
        var answer = await Sut().RenameAsync(Document("Helpers.bas"), new Position(0, 22), "Tools", CancellationToken.None);

        Assert.IsNotNull(answer.Value, answer.Refusal);
        var changes = answer.Value.DocumentChanges!.ToList();
        var helpers = changes.Single(change => change.IsTextDocumentEdit).TextDocumentEdit!;
        Assert.AreEqual(Document("Helpers.bas").AbsoluteUri, helpers.TextDocument.Uri.ToUri().AbsoluteUri);
        Assert.AreEqual(new Range(0, 21, 0, 28), helpers.Edits.Single().Range);
        Assert.IsTrue(changes[^1].IsRenameFile, "the file is renamed last, once the edits of its text are made");
        Assert.AreEqual(Document("Tools.bas").AbsoluteUri, changes[^1].RenameFile!.NewUri.ToUri().AbsoluteUri);
        Assert.AreEqual(true, changes[^1].RenameFile!.Options?.IgnoreIfExists, "a file of that name is left as it is");
    }

    [TestMethod]
    public async Task AForm_IsRenamedWithItsBinaryFile_AndItsDesignerNamesTheRenamedOne()
    {
        const string text = "VERSION 5.00\r\nBegin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} Dialog\r\n   Caption = \"Dialog\"\r\n   OleObjectBlob = \"Dialog.frx\":0000\r\nEnd\r\n"
            + "Attribute VB_Name = \"Dialog\"\r\nPublic Sub Go()\r\nEnd Sub\r\n";
        var dialog = new WorkspaceDocument("Dialog.frm", Root, text, version: 1);
        var all = _documents.GetAllDocuments().ToList();
        _documents.GetAllDocuments().Returns([.. all, dialog]);
        var uri = dialog.Id.Uri.ToUri();
        _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = dialog; return true; });
        _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(dialog))));

        var answer = await Sut().RenameAsync(Document("Dialog.frm"), new Position(5, 22), "Prompt", CancellationToken.None);

        Assert.IsNotNull(answer.Value, answer.Refusal);
        var changes = answer.Value.DocumentChanges!.ToList();
        var renames = changes.Where(change => change.IsRenameFile).Select(change => change.RenameFile!).ToList();
        CollectionAssert.AreEqual(
            new[] { (Document("Dialog.frm").AbsoluteUri, Document("Prompt.frm").AbsoluteUri), (Document("Dialog.frx").AbsoluteUri, Document("Prompt.frx").AbsoluteUri) },
            renames.Select(rename => (rename.OldUri.ToUri().AbsoluteUri, rename.NewUri.ToUri().AbsoluteUri)).ToArray());
        Assert.IsTrue(renames.All(rename => rename.Options is { Overwrite: false, IgnoreIfExists: false }), "neither file is renamed over one that exists");
        var named = changes[^1].TextDocumentEdit!;
        Assert.AreEqual(Document("Prompt.frm").AbsoluteUri, named.TextDocument.Uri.ToUri().AbsoluteUri, "the renamed designer names the renamed binary file");
        Assert.AreEqual((new Range(3, 20, 3, 30), "Prompt.frx"), (named.Edits.Single().Range, named.Edits.Single().NewText));
        var designer = changes.First(change => change.IsTextDocumentEdit).TextDocumentEdit!;
        Assert.IsTrue(designer.Edits.Any(edit => edit.Range.Start.Line == 1 && edit.NewText == "Prompt"), "the designer's Begin line");
    }

    [TestMethod]
    public async Task AProject_IsRenamedInItsQualifiers_AndInItsProjectFile()
    {
        const string text = "Attribute VB_Name = \"Caller\"\r\nPublic Sub Go()\r\n    Billing.Helpers.Show\r\nEnd Sub\r\n";
        var caller = new WorkspaceDocument("Caller.bas", Root, text, version: 2);
        var all = _documents.GetAllDocuments().ToList();
        _documents.GetAllDocuments().Returns([.. all, caller]);
        var uri = caller.Id.Uri.ToUri();
        _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = caller; return true; });
        _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(caller))));

        var projects = Substitute.For<IWorkspaceProjects>();
        projects.Projects.Returns([new WorkspaceProject(Root, new ProjectFile(Root, new RDCoreProject { Name = "Billing" }))]);
        projects.Folders.Returns(new WorkspaceFolders([]));
        var projectFile = Path.Combine(Root, ProjectFile.FileName);
        Directory.CreateDirectory(Root);
        File.WriteAllText(projectFile, "{\r\n  \"SchemaVersion\": 1,\r\n  \"Name\": \"Billing\"\r\n}\r\n");
        try
        {
            var answer = await Sut(projects).RenameAsync(Document("Caller.bas"), new Position(2, 6), "Invoicing", CancellationToken.None);

            Assert.IsNotNull(answer.Value, answer.Refusal);
            var changes = answer.Value.DocumentChanges!.Select(change => change.TextDocumentEdit!).ToList();
            var qualifier = changes.Single(change => change.TextDocument.Uri.ToUri().AbsoluteUri == Document("Caller.bas").AbsoluteUri);
            Assert.AreEqual((new Range(2, 4, 2, 11), "Invoicing"), (qualifier.Edits.Single().Range, qualifier.Edits.Single().NewText));
            var project = changes.Single(change => change.TextDocument.Uri == DocumentUri.FromFileSystemPath(projectFile));
            Assert.AreEqual((new Range(2, 11, 2, 18), "Invoicing"), (project.Edits.Single().Range, project.Edits.Single().NewText), "the name the project file writes");
        }
        finally
        {
            File.Delete(projectFile);
        }
    }
}
