using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Extract Method's duplicates in a module written in several documents (RD-VBAL §5.1): a duplicate in another part of the selection's module is offered as
/// one of the module's, and the call that replaces it is written in that part's own document.
/// </summary>
[TestClass]
public sealed class ExtractMethodDuplicatesInPartsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-extractmethod-parts-ws");

    // Program's plain part: lines 3 and 4 are extracted.
    private const string PlainPart =
        "Option Explicit\r\n" +
        "Public Sub Run(ByVal n As Long)\r\n" +
        "    Dim x As Long\r\n" +
        "    x = n * 2\r\n" +
        "    Debug.Print x\r\n" +
        "End Sub\r\n";

    // Program's Partial part, a block of Extra.bas: Twin, lines 3 and 4, does what the selection does, and is Private to the module.
    private const string PartialPart =
        "Partial Module Program\r\n" +
        "Private Sub Twin(ByVal m As Long)\r\n" +
        "    Dim y As Long\r\n" +
        "    y = m * 2\r\n" +
        "    Debug.Print y\r\n" +
        "End Sub\r\n" +
        "End Module\r\n";

    private const string InPartialPart = "y = m * 2";

    private static readonly SourceRange Selection = new(3, 0, 5, 0);

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IOptions<SdkAppOptions> _options = Options.Create(new SdkAppOptions
    {
        Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba", FileLevelStatements = true },
    });

    private readonly RenameService _rename;

    public ExtractMethodDuplicatesInPartsTests()
    {
        // a .bas file declares a block only where file-level statements are allowed (Workspace.FileLevelStatements).
        var parser = new ModuleParser(workspaceOptions: Options.Create(_options.Value.Workspace));
        _documents.WorkspaceRoot.Returns(Root);
        WorkspaceDocument[] documents = [new("Program.bas", Root, PlainPart, version: 3), new("Extra.bas", Root, PartialPart, version: 5)];
        _documents.GetAllDocuments().Returns(documents);
        foreach (var document in documents)
        {
            var uri = document.Id.Uri.ToUri();
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(parser.Parse(uri, document.Text)));
        }

        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(parser.Parse(call.Arg<Uri>(), call.Arg<string>())));

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(
                call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        _rename = new RenameService(
            _documents, _parsing, new IntrinsicSymbolResolver(), libraries, _options, NullLogger<RenameService>.Instance, OneProject.Registry());
    }

    private static Uri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName)).ToUri();

    private ExtractMethodService Sut() => new(_rename, _parsing, _options);

    [TestMethod]
    public async Task ADuplicateInAnotherPartOfTheModule_IsOfferedTicked_AsOneOfTheModules()
    {
        var prepared = await Sut().PrepareAsync(Document("Program.bas"), Selection, CancellationToken.None);

        var item = prepared.Choices.Single(choice => choice.Id == ExtractMethodService.DuplicatesChoice).Items.Single();
        StringAssert.EndsWith(item.Label, InPartialPart);
        StringAssert.Contains(item.Label, "Extra.bas", "where the duplicate is: the document of its part");
        Assert.IsNull(item.Reason, "a Private procedure of the module is called from each of its parts");
        Assert.AreEqual("true", item.Choices.Single(choice => choice.Id == ExtractMethodService.DuplicateReplaceChoice).Default);
    }

    [TestMethod]
    public async Task ADuplicateInAnotherPartOfTheModule_IsReplacedInThatPartsDocument()
    {
        var answer = await Sut().ApplyAsync(
            Document("Program.bas"), Selection, [new RefactorAnswer { Id = ExtractMethodService.NameChoice, Value = "Doubled" }], [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var edited = answer.Edit!.Documents.ToDictionary(document => Path.GetFileName(document.Uri!.LocalPath), document => document.Edits);
        CollectionAssert.AreEquivalent(new[] { "Program.bas", "Extra.bas" }, edited.Keys.ToArray());
        StringAssert.Contains(DeclarationEdits.ApplyTo(PlainPart, edited["Program.bas"]), "Public Sub Run(ByVal n As Long)\r\n    Doubled n\r\nEnd Sub");
        Assert.AreEqual(PartialPart.Replace("    Dim y As Long\r\n    y = m * 2\r\n    Debug.Print y\r\n", "    Doubled m\r\n", StringComparison.Ordinal),
            DeclarationEdits.ApplyTo(PartialPart, edited["Extra.bas"]));
        Assert.AreEqual("Extra.bas", Path.GetFileName(answer.Replacements.Single().Document.LocalPath));
    }
}
