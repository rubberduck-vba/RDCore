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
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A module written in several documents (RD-VBAL §5.1) is renamed, navigated and refactored in the document each name is written in: a name in one part is
/// bound in that part, and found and edited where each part writes it, though the parts write the same lines.
/// </summary>
[TestClass]
public sealed class ModuleOfPartsAddressingTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-parts-addressing-ws");

    // Program's plain part declares count and last, writes count on line 4 and calls Add on line 8; its Partial part, a block of Extra.bas, writes count
    // twice on line 2, declares Add on line 5, and writes last in Note only. Circle's Partial part, a block of Extra.bas too, implements IShape.
    private const string PlainPart = "Option Explicit\r\nPrivate count As Long\r\nPrivate last As Long\r\nPublic Sub Reset()\r\n    count = 0\r\nEnd Sub\r\n"
        + "\r\nPublic Sub Main()\r\n    Add 1, 2\r\nEnd Sub\r\n";

    private const string PartialPart = "Partial Module Program\r\nPublic Sub Bump()\r\n    count = count + 1\r\nEnd Sub\r\n"
        + "\r\nPrivate Sub Add(ByVal n As Long, ByVal unused As Long)\r\n    Debug.Print n\r\nEnd Sub\r\n"
        + "\r\nPublic Sub Note()\r\n    last = 1\r\n    Debug.Print last\r\nEnd Sub\r\nEnd Module\r\n"
        + "Partial Class Circle\r\nImplements IShape\r\nEnd Class\r\n";

    private const string ClassHeader = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\n";

    private const string IShape = ClassHeader + "Attribute VB_Name = \"IShape\"\r\nOption Explicit\r\n\r\n"
        + "Public Function Area(ByVal factor As Double) As Double\r\nEnd Function\r\n";

    private const string Circle = ClassHeader + "Attribute VB_Name = \"Circle\"\r\nOption Explicit\r\n";

    private static readonly Dictionary<string, string> Texts = new()
    {
        ["Program.bas"] = PlainPart, ["Extra.bas"] = PartialPart, ["IShape.cls"] = IShape, ["Circle.cls"] = Circle,
    };

    // every place count is written: its declaration and its reference in Program.bas, and the two references of Extra.bas.
    private static readonly string[] Everywhere =
        ["Extra.bas (2,12)-(2,17)", "Extra.bas (2,4)-(2,9)", "Program.bas (1,8)-(1,13)", "Program.bas (4,4)-(4,9)"];

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    public ModuleOfPartsAddressingTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        var documents = Texts.Select(each => new WorkspaceDocument(each.Key, Root, each.Value, version: 2)).ToArray();
        _documents.GetAllDocuments().Returns(documents);
        _documents.IsOpen(Arg.Any<TextDocumentIdentifier>()).Returns(true);
        foreach (var document in documents)
        {
            var uri = document.Id.Uri.ToUri();
            var parse = FileLevelStatementsOn.Parser().Parse(uri, document.Text);
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(parse));
            _parsing.TryGetCached(uri, document.Version, out Arg.Any<ModuleParseResult>()).Returns(call => { call[2] = parse; return true; });
        }

        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(FileLevelStatementsOn.Parser().Parse(call.Arg<Uri>(), call.Arg<string>())));
    }

    private RenameService Sut()
    {
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(
                call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var options = WorkspaceOptions();
        return new RenameService(
            _documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
    }

    // a .bas declares blocks, a Partial one among them, only in a workspace that allows file-level statements (RD-VBAL §5.1).
    private static IOptions<SdkAppOptions> WorkspaceOptions() => Options.Create(new SdkAppOptions
    {
        Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba", FileLevelStatements = true },
    });

    private static Uri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName)).ToUri();

    private async Task<RenameWorkspace> WorkspaceAsync() => (await Sut().ComposeAsync(CancellationToken.None))!;

    // a place of a document, as the file and the range: what the assertions compare.
    private static string Place(DocumentUri document, Range range)
        => $"{Path.GetFileName(document.GetFileSystemPath())} ({range.Start.Line},{range.Start.Character})-({range.End.Line},{range.End.Character})";

    [TestMethod]
    [DataRow("Extra.bas", 2, 5, DisplayName = "from a reference in the Partial part")]
    [DataRow("Program.bas", 1, 9, DisplayName = "from its declaration in the plain part")]
    public async Task ARename_EditsEachPartInItsOwnDocument(string file, int line, int character)
    {
        var answer = await Sut().RenameAsync(Document(file), new Position(line, character), "total", CancellationToken.None);

        Assert.IsNotNull(answer.Value, answer.Refusal);
        var edits = answer.Value.DocumentChanges!.Select(change => change.TextDocumentEdit!)
            .SelectMany(change => change.Edits.Select(edit => (Place: Place(change.TextDocument.Uri, edit.Range), edit.NewText))).ToList();
        CollectionAssert.AreEqual(Everywhere, edits.Select(edit => edit.Place).Order(StringComparer.Ordinal).ToArray());
        Assert.IsTrue(edits.All(edit => edit.NewText == "total"), string.Join("; ", edits));
    }

    [TestMethod]
    public async Task TheReferences_OfANameInThePartialPart_AreFoundInEachDocument()
    {
        var (locations, _) = (await WorkspaceAsync()).References(Document("Extra.bas"), new Position(2, 5), includeDeclaration: true);

        CollectionAssert.AreEqual(Everywhere, locations.Select(location => Place(location.Uri, location.Range)).Order(StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public async Task TheDefinition_OfANameInThePartialPart_IsItsDeclarationInThePlainPart()
    {
        var definitions = (await WorkspaceAsync()).Definitions(Document("Extra.bas"), new Position(2, 5));

        Assert.AreEqual("Program.bas (1,8)-(1,13)", Place(definitions.Single().TargetUri, definitions.Single().TargetSelectionRange));
    }

    [TestMethod]
    public async Task TheHighlights_OfANameInThePartialPart_AreTheNamesOfItsDocument()
    {
        var highlights = (await WorkspaceAsync()).Highlights(Document("Extra.bas"), new Position(2, 13));

        CollectionAssert.AreEquivalent(new[] { new Range(2, 4, 2, 9), new Range(2, 12, 2, 17) }, highlights.Select(highlight => highlight.Range).ToArray());
    }

    [TestMethod]
    public async Task ARemovedParameter_IsRemovedFromThePartThatDeclaresIt_AndFromTheCallInTheOtherPart()
    {
        var removal = (await WorkspaceAsync()).RemoveParameter(Document("Extra.bas"), new Position(5, 40));

        Assert.IsNotNull(removal, "no parameter is named at unused");
        var (name, answer) = removal.Value;
        Assert.AreEqual("unused", name);
        Assert.IsNotNull(answer.Value, answer.Refusal);
        AssertEdited(
            Edited(answer.Value.DocumentChanges!.Select(change => change.TextDocumentEdit!)
                .Select(edit => (edit.TextDocument.Uri, edit.Edits.Select(each => (RangeOf(each.Range), each.NewText))))),
            ("Extra.bas", PartialPart.Replace("ByVal n As Long, ByVal unused As Long", "ByVal n As Long")),
            ("Program.bas", PlainPart.Replace("    Add 1, 2", "    Add 1")));
    }

    [TestMethod]
    public async Task ReorderedParameters_AreReorderedInThePartThatDeclaresThem_AndInTheCallInTheOtherPart()
    {
        var answer = await new ParameterReorderService(Sut()).ApplyAsync(
            Document("Extra.bas"), new SourcePosition(5, 13),
            [new RefactorAnswer
            {
                Id = ParameterReorderService.ParametersChoice, Items = [new RefactorItemAnswer { Id = "unused" }, new RefactorItemAnswer { Id = "n" }],
            }],
            [], CancellationToken.None);

        Assert.IsNotNull(answer.Edit, answer.Refusal);
        AssertEdited(
            Edited(answer.Edit.Documents.Select(document => (DocumentUri.From(document.Uri!), document.Edits.Select(edit => (edit.Range, edit.NewText))))),
            ("Extra.bas", PartialPart.Replace("ByVal n As Long, ByVal unused As Long", "ByVal unused As Long, ByVal n As Long")),
            ("Program.bas", PlainPart.Replace("    Add 1, 2", "    Add 2, 1")));
    }

    [TestMethod]
    [DataRow("Program.bas", 2, 9, DisplayName = "from its declaration in the plain part")]
    [DataRow("Extra.bas", 10, 5, DisplayName = "from its use in the Partial part")]
    public async Task AVariableOfTheModule_MovesIntoTheProcedureOfTheOtherPartThatUsesIt(string file, int line, int character)
    {
        var answer = await new MoveCloserToUsageService(Sut()).ApplyAsync(Document(file), new SourcePosition(line, character), [], [], CancellationToken.None);

        Assert.IsNotNull(answer.Edit, answer.Refusal);
        AssertEdited(
            Edited(answer.Edit.Documents.Select(document => (DocumentUri.From(document.Uri!), document.Edits.Select(edit => (edit.Range, edit.NewText))))),
            ("Extra.bas", PartialPart.Replace("    last = 1\r\n", "    Static last As Long\r\n    last = 1\r\n")),
            ("Program.bas", PlainPart.Replace("Private last As Long\r\n", string.Empty)));
    }

    [TestMethod]
    public async Task TheCallHierarchy_OfAProcedureOfThePartialPart_IsOfItsDocument_AndItsCallerIsOfTheOtherPart()
    {
        var workspace = await WorkspaceAsync();
        var item = workspace.PrepareHierarchy(Document("Extra.bas"), new Position(5, 13), types: false);

        Assert.IsNotNull(item, "no procedure is named at Add");
        Assert.AreEqual("Add", item.Symbol.Name);
        Assert.AreEqual("Extra.bas (5,12)-(5,15)", Place(workspace.Locate(item)!.Value.Uri, workspace.Locate(item)!.Value.SelectionRange));
        var call = workspace.IncomingCalls(item).Single();
        Assert.AreEqual("Main", call.Item.Symbol.Name);
        CollectionAssert.AreEqual(
            new[] { "Program.bas (8,4)-(8,7)" },
            call.Ranges.Select(range => Place(workspace.Locate(call.Item)!.Value.Uri, workspace.InTextOf(call.Item, range))).ToArray());
    }

    [TestMethod]
    public async Task AnImplementsDirectiveOfAPartialPart_IsImplementedInThatPart()
    {
        var options = WorkspaceOptions();
        var answer = await new ImplementInterfaceService(Sut(), _parsing, options).ImplementAsync(
            Document("Extra.bas"), new SourceRange(15, 0, 15, 0), CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        Assert.AreEqual("Extra.bas", Path.GetFileName(answer.Document.LocalPath));
        var implemented = Applied(PartialPart, answer.Edits!.Select(edit => (edit.Range, edit.NewText)));
        var stub = implemented.IndexOf("Private Function IShape_Area(ByVal factor As Double) As Double", StringComparison.Ordinal);
        Assert.IsTrue(stub > implemented.IndexOf("Implements IShape", StringComparison.Ordinal), implemented);
        Assert.IsTrue(stub < implemented.LastIndexOf("End Class", StringComparison.Ordinal), "the stub is inside the block: " + implemented);
        Assert.IsTrue(implemented.EndsWith("End Class\r\n", StringComparison.Ordinal), implemented);
    }

    // the text of each document an edit edits, by its file name.
    private static Dictionary<string, string> Edited(IEnumerable<(DocumentUri Document, IEnumerable<(SourceRange Range, string NewText)> Edits)> documents)
        => documents
            .GroupBy(document => Path.GetFileName(document.Document.GetFileSystemPath()))
            .ToDictionary(group => group.Key, group => Applied(Texts[group.Key], group.SelectMany(document => document.Edits)));

    private static void AssertEdited(Dictionary<string, string> edited, params (string File, string Text)[] expected)
    {
        CollectionAssert.AreEquivalent(expected.Select(each => each.File).ToArray(), edited.Keys.ToArray());
        foreach (var (file, text) in expected)
        {
            Assert.AreEqual(text, edited[file], file);
        }
    }

    // a text with edits of its ranges made, the last first.
    private static string Applied(string text, IEnumerable<(SourceRange Range, string NewText)> edits)
    {
        var starts = new List<int> { 0 };
        starts.AddRange(text.Select((character, at) => (character, at)).Where(each => each.character == '\n').Select(each => each.at + 1));
        foreach (var (range, newText) in edits.OrderByDescending(edit => edit.Range.Start))
        {
            var (from, to) = (starts[range.Start.Line] + range.Start.Character, starts[range.End.Line] + range.End.Character);
            text = text[..from] + newText + text[to..];
        }

        return text;
    }

    private static SourceRange RangeOf(Range range) => new(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);
}
