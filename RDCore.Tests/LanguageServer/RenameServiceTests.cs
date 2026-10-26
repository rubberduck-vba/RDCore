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
        foreach (var document in new[] { helpers, program })
        {
            var uri = document.Id.Uri.ToUri();
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(document))));
        }
    }

    private RenameService Sut()
    {
        var source = new InMemoryLibrarySource();
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(source, [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        return new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance);
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
}
