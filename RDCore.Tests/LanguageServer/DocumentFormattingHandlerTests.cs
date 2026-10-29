using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Formatting;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>textDocument/formatting</c> and <c>textDocument/rangeFormatting</c>: the edits that indent a document's lines, or those of a range, by the formatting
/// settings as they are when the request is made.
/// </summary>
[TestClass]
public sealed class DocumentFormattingHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-formatting-ws");

    private const string Module = "Sub A()\r\nIf x Then\r\ny = 1\r\nEnd If\r\nEnd Sub";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly SdkAppOptions _options = new();

    private void Holds(WorkspaceDocument document)
    {
        var uri = document.Id.Uri.ToUri();
        _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
        _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(new ModuleParser().Parse(uri, document.Text));
    }

    private CodeFormattingService Service() => new(_documents, _parsing, Options.Create(_options));

    private static readonly TextDocumentSelector Selector = new(new TextDocumentFilter { Pattern = "**/*.bas" });

    private static string[] Edited(TextEditContainer? edits)
        => [.. (edits ?? new TextEditContainer()).Select(edit => $"{edit.Range.Start.Line}:{edit.Range.Start.Character}-{edit.Range.End.Character} '{edit.NewText}'")];

    [TestMethod]
    public async Task ADocument_IsGivenTheEditsThatIndentItsLines()
    {
        var document = new WorkspaceDocument("Module1.bas", Root, Module);
        Holds(document);

        var edits = await new DocumentFormattingHandler(Service(), Selector, new IncomingDocumentChanges())
            .Handle(new DocumentFormattingParams { TextDocument = new TextDocumentIdentifier(document.Id.Uri) }, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "1:0-0 '    '", "2:0-0 '        '", "3:0-0 '    '" }, Edited(edits));
    }

    // the settings are read when the request is: one the user changed applies to the next request.
    [TestMethod]
    public async Task TheSettings_AreTheOnesThereAreWhenTheRequestIsMade()
    {
        var document = new WorkspaceDocument("Module1.bas", Root, Module);
        Holds(document);
        _options.Formatting.IndentSpaces = 2;

        var edits = await Service().FormatAsync(document.Id.Uri.ToUri(), lines: null, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "  ", "    ", "  " }, edits.Select(edit => edit.NewText).ToArray());
    }

    // a selection of whole lines ends at the start of the line after it, which it does not take in.
    [TestMethod]
    public async Task ARange_IsGivenTheEditsOfItsLines()
    {
        var document = new WorkspaceDocument("Module1.bas", Root, Module);
        Holds(document);

        var edits = await new DocumentRangeFormattingHandler(Service(), Selector, new IncomingDocumentChanges())
            .Handle(new DocumentRangeFormattingParams { TextDocument = new TextDocumentIdentifier(document.Id.Uri), Range = new Range(2, 0, 3, 0) }, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "2:0-0 '        '" }, Edited(edits));
    }

    [TestMethod]
    public async Task ADocumentTheServerDoesNotHold_IsGivenNoEdit()
        => Assert.IsEmpty(await Service().FormatAsync(new Uri(Path.Combine(Root, "Nope.bas")), lines: null, CancellationToken.None));

    // a program of the platform's BASIC is parsed as the body of a procedure its text does not write: the shell formats its lines.
    [TestMethod]
    public async Task AProgram_IsGivenNoEdit()
    {
        var document = new WorkspaceDocument("Program1.rdc", Root, "10 If x Then\r\n20 y = 1\r\n30 End If");
        Holds(document);

        Assert.IsEmpty(await Service().FormatAsync(document.Id.Uri.ToUri(), lines: null, CancellationToken.None));
    }
}
