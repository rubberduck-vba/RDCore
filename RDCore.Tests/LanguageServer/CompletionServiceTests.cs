using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Completion;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The completion list at a position of a module: what is typed on the line need not parse, the rest of the module and the workspace say what the names are, and
/// the type of what a member is accessed on says what its members are.
/// </summary>
[TestClass]
public sealed class CompletionServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-completion-ws");

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    private const string Widget = """
        VERSION 1.0 CLASS
        BEGIN
          MultiUse = -1  'True
        END
        Attribute VB_Name = "Widget"
        Public Name As String
        Private Secret As Long

        Public Function Grow(ByVal By As Long) As Widget
        End Function

        Public Property Get Parts() As Collection
        End Property
        """;

    private const string Helpers = """
        Attribute VB_Name = "Helpers"
        Public Function Twice(ByVal Value As Long) As Long
            Twice = Value * 2
        End Function
        """;

    public CompletionServiceTests()
    {
        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));

        var widget = new WorkspaceDocument("Widget.cls", Root, Widget);
        var helpers = new WorkspaceDocument("Helpers.bas", Root, Helpers);
        _documents.WorkspaceRoot.Returns(Root);
        WorkspaceHas(widget, helpers);
    }

    private void WorkspaceHas(params WorkspaceDocument[] documents)
    {
        _documents.GetAllDocuments().Returns(documents);
        foreach (var document in documents)
        {
            var uri = document.Id.Uri.ToUri();
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            var parse = new ModuleParser().Parse(uri, document.Text);
            _parsing.TryGetCached(uri, out Arg.Any<ModuleParseResult>()).Returns(call => { call[1] = parse; return true; });
        }
    }

    private CompletionService Sut(string language = "vba")
    {
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns([]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = language } });
        return new CompletionService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<CompletionService>.Instance);
    }

    // the module with the cursor where the `|` is, which is taken out of the source.
    private static (string Source, Position Position) At(string source)
    {
        var lines = source.ReplaceLineEndings("\r\n").Split("\r\n");
        var line = Array.FindIndex(lines, text => text.Contains('|'));
        var position = new Position(line, lines[line].IndexOf('|'));
        lines[line] = lines[line].Replace("|", string.Empty);
        return (string.Join("\r\n", lines), position);
    }

    private async Task<string[]> LabelsAsync(string module)
    {
        var (source, position) = At(module);
        var list = await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None);
        return [.. list.Items.Select(item => item.Label)];
    }

    [TestMethod]
    public async Task TheMembersOfAVariable_AreTheMembersOfItsDeclaredType_ThoughTheLineDoesNotParse()
    {
        var labels = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Dim w As New Widget
                Debug.Print w.|
            End Sub
            """);

        CollectionAssert.AreEquivalent(new[] { "Name", "Grow", "Parts" }, labels);
    }

    [TestMethod]
    public async Task TheMembersOfACall_AreTheMembersOfWhatItReturns()
    {
        var labels = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Dim w As New Widget
                w.Grow(2).Parts.|
            End Sub
            """);

        CollectionAssert.IsSubsetOf(new[] { "Add", "Count", "Item", "Remove" }, labels);
    }

    [TestMethod]
    public async Task TheMembersOfAWithBlocksObject_AreOfferedAfterADotWithNothingBeforeIt()
    {
        var labels = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                With New Widget
                    .|
                End With
            End Sub
            """);

        CollectionAssert.AreEquivalent(new[] { "Name", "Grow", "Parts" }, labels);
    }

    [TestMethod]
    public async Task TheMembersOfAModule_AreItsPublicOnes()
        => CollectionAssert.AreEqual(new[] { "Twice" }, await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                x = Helpers.Tw|
            End Sub
            """));

    [TestMethod]
    public async Task ANameWhereAStatementStarts_IsOfferedTheNamesInScope_AndTheStatementKeywords()
    {
        var labels = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Private Total As Long
            Public Sub Run(ByVal Amount As Long)
                |
            End Sub
            """);

        CollectionAssert.IsSubsetOf(new[] { "Amount", "Total", "Run", "Twice", "Helpers", "MsgBox", "Dim", "For" }, labels);
        CollectionAssert.DoesNotContain(labels, "Widget", "a class is no value");
    }

    [TestMethod]
    public async Task AsIsOfferedTypes_AndNewTheClassesThatCanBeCreated()
    {
        var types = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Dim w As Wi|
            End Sub
            """);
        CollectionAssert.IsSubsetOf(new[] { "Widget", "Long", "Collection" }, types);

        var classes = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Set w = New |
            End Sub
            """);
        CollectionAssert.Contains(classes, "Widget");
        CollectionAssert.DoesNotContain(classes, "Long");
    }

    [TestMethod]
    public async Task NothingIsOffered_InAComment()
        => Assert.AreEqual(0, (await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                ' w.|
            End Sub
            """)).Length);

    [TestMethod]
    public async Task AProgramOfTheBasic_IsCompletedWhereItsLinesAreInTheText()
    {
        var program = new WorkspaceDocument("hello.rdc", Root, "100 DIM W AS NEW Widget\r\n110 W.\r\n");
        WorkspaceHas(new WorkspaceDocument("Widget.cls", Root, Widget), program);

        var list = await Sut("basic").CompleteDocumentAsync(program.Id.Uri.ToUri(), new Position(1, 6), CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { "Name", "Grow", "Parts" }, list.Items.Select(item => item.Label).ToArray());
    }

    [TestMethod]
    public async Task AnItem_SaysWhatItIs()
    {
        var (source, position) = At("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                x = Helpers.|
            End Sub
            """);
        var item = (await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None)).Items.Single();

        Assert.AreEqual(CompletionItemKind.Function, item.Kind);
        Assert.AreEqual("Function Twice(ByVal Value As Long) As Long", item.Detail);
    }
}
