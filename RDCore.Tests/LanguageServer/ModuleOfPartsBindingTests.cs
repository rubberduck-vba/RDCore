using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Completion;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Configuration;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The names a module of parts binds in each of its parts (RD-VBAL §5.1): one module, whose every part sees the names of the others, and whose procedures are
/// each in the part that writes them, wherever their lines are in the other parts' documents.
/// </summary>
[TestClass]
public sealed class ModuleOfPartsBindingTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-parts-binding-ws");

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    // the plain part, and the part another file writes: their procedures are on the same lines of their own documents.
    private const string Plain = """
        Attribute VB_Name = "Main"
        Option Explicit
        Public Sub Foo()
            Dim inFoo As Long

        End Sub
        """;

    private const string Partial = """
        Attribute VB_Name = "Extra"
        Partial Module Main
        Public Sub Bar()
            Dim inBar As Long

        End Sub
        End Module
        """;

    private readonly WorkspaceDocument _plain = new("Main.bas", Root, Plain.ReplaceLineEndings("\r\n"));
    private readonly WorkspaceDocument _partial = new("Extra.bas", Root, Partial.ReplaceLineEndings("\r\n"));

    public ModuleOfPartsBindingTests()
    {
        // a list is made of the document's text with the line taken out, parsed as a fragment.
        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));

        _documents.WorkspaceRoot.Returns(Root);
        _documents.GetAllDocuments().Returns([_plain, _partial]);
        foreach (var document in new[] { _plain, _partial })
        {
            var uri = document.Id.Uri.ToUri();
            var parse = new ModuleParser().Parse(uri, document.Text);
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            _parsing.TryGetCached(uri, out Arg.Any<ModuleParseResult>()).Returns(call => { call[1] = parse; return true; });
        }
    }

    private CompletionService Sut()
    {
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        return new CompletionService(_documents, _parsing, new IntrinsicSymbolResolver(), Substitute.For<IReferencedLibraryService>(),
            new InMemoryLibrarySource(), new ClientVerbRegistry(), options, NullLogger<CompletionService>.Instance, OneProject.Registry());
    }

    // the blank line of the procedure's body, the fifth of either document.
    private async Task<string[]> LabelsInTheBodyOfAsync(WorkspaceDocument document)
    {
        var list = await Sut().CompleteDocumentAsync(document.Id.Uri.ToUri(), new Position(4, 4), CancellationToken.None);
        return [.. list.Items.Select(item => item.Label)];
    }

    [TestMethod]
    public async Task InAProcedureOfTheSecondPart_ItsOwnLocalsAreNamed_AndNotThoseOfAProcedureOfTheFirstPartOnTheSameLines()
    {
        var labels = await LabelsInTheBodyOfAsync(_partial);

        CollectionAssert.Contains(labels, "inBar");
        CollectionAssert.DoesNotContain(labels, "inFoo", "a local of the first part's procedure");
    }

    [TestMethod]
    public async Task InAProcedureOfTheFirstPart_ItsOwnLocalsAreNamed_AndNotThoseOfAProcedureOfTheSecondPartOnTheSameLines()
    {
        var labels = await LabelsInTheBodyOfAsync(_plain);

        CollectionAssert.Contains(labels, "inFoo");
        CollectionAssert.DoesNotContain(labels, "inBar", "a local of the second part's procedure");
    }

    [TestMethod]
    [DataRow("Extra.bas", "Foo", DisplayName = "the second part names the first part's procedure")]
    [DataRow("Main.bas", "Bar", DisplayName = "the first part names the second part's procedure")]
    public async Task EachPart_NamesTheProceduresOfTheOthers_OfOneModule(string file, string other)
    {
        var labels = await LabelsInTheBodyOfAsync(file == "Main.bas" ? _plain : _partial);

        CollectionAssert.Contains(labels, other);
    }
}
