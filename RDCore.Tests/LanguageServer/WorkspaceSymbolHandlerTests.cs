using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Server.Configuration;
using RDCore.Symbols;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>workspace/symbol</c> answers the symbols of every document of the workspace whose name matches what is asked, as their outlines say them: named with
/// what they are declared in, located at the name they declare, in the text of their document.
/// </summary>
[TestClass]
public sealed class WorkspaceSymbolHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-workspace-symbol-ws");

    private const string Helpers = """
        Attribute VB_Name = "Helpers"
        Public Total As Long
        Public Enum Colour
            Red
            TotalBlack
        End Enum

        Public Function GetTotal() As Long
            GetTotal = Total
        End Function
        """;

    private const string Class1 = """
        VERSION 1.0 CLASS
        BEGIN
          MultiUse = -1  'True
        END
        Attribute VB_Name = "Class1"
        Public Property Get Subtotal() As Long
        End Property
        """;

    // a program is the body of a Main procedure it does not write: that procedure, and its module, are all its outline has.
    private const string Program = "10 Dim grandTotal As Long\r\n20 grandTotal = 1\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    public WorkspaceSymbolHandlerTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        Hold(new WorkspaceDocument("Helpers.bas", Root, Helpers, version: 1),
            new WorkspaceDocument("Class1.cls", Root, Class1, version: 1),
            new WorkspaceDocument("Main.rdc", Root, Program, version: 1));
    }

    private void Hold(params WorkspaceDocument[] held)
    {
        _documents.GetAllDocuments().Returns(held);
        foreach (var document in held)
        {
            var uri = document.Id.Uri.ToUri();
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(document))));
        }
    }

    private WorkspaceSymbolHandler Sut()
        => new(_documents, _parsing, new IntrinsicSymbolResolver(), Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } }));

    private static DocumentUri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName));

    private static async Task<WorkspaceSymbol[]> SearchAsync(WorkspaceSymbolHandler handler, string query)
        => [.. (await handler.Handle(new WorkspaceSymbolParams { Query = query }, CancellationToken.None))!];

    [TestMethod]
    public async Task TheSymbolsThatMatch_ComeClosestFirst_FromEveryDocument()
    {
        var found = await SearchAsync(Sut(), "total");

        // a procedure's locals are no symbol of the workspace, as they are none of the outline: grandTotal is not answered.
        CollectionAssert.AreEqual(new[] { "Total", "TotalBlack", "GetTotal", "Subtotal" }, found.Select(symbol => symbol.Name).ToArray());
    }

    [TestMethod]
    public async Task ASymbol_IsNamedWithWhatItIsDeclaredIn_AndLocatedAtItsName()
    {
        var found = await SearchAsync(Sut(), "total");

        var variable = found.Single(symbol => symbol.Name == "Total");
        Assert.AreEqual("Helpers", variable.ContainerName);
        Assert.AreEqual(SymbolKind.Field, variable.Kind, "a variable of a module is a field of it, as the outline says");
        Assert.AreEqual(Document("Helpers.bas"), variable.Location!.Location!.Uri);
        Assert.AreEqual(new Range(1, 7, 1, 12), variable.Location.Location.Range);

        Assert.AreEqual("Helpers.Colour", found.Single(symbol => symbol.Name == "TotalBlack").ContainerName);
        Assert.AreEqual(new Range(7, 16, 7, 24), found.Single(symbol => symbol.Name == "GetTotal").Location!.Location!.Range);
        Assert.AreEqual("Class1", found.Single(symbol => symbol.Name == "Subtotal").ContainerName);
    }

    [TestMethod]
    public async Task ASymbolOfAProgram_IsLocatedInItsLines()
    {
        var ofTheProgram = (await SearchAsync(Sut(), "Main")).Where(symbol => symbol.Location!.Location!.Uri == Document("Main.rdc")).ToArray();

        Assert.IsNotEmpty(ofTheProgram);
        Assert.IsTrue(ofTheProgram.All(symbol => symbol.Location!.Location!.Range.Start == new Position(0, 0)),
            "the Main the program is the body of begins on a header it does not write: at the program's first line");
    }

    // textDocument/documentSymbol answers the same outline: a program's in its lines, not those of the module it is the body of.
    [TestMethod]
    public async Task TheOutlineOfAProgram_IsInItsLines()
    {
        var handler = new DocumentSymbolHandler(_documents, _parsing, new IntrinsicSymbolResolver(),
            Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } }));

        var outline = (await handler.Handle(new DocumentSymbolParams { TextDocument = new TextDocumentIdentifier(Document("Main.rdc")) }, CancellationToken.None))!
            .Single().DocumentSymbol!;
        var main = outline.Children!.Single(child => child.Name == "Main");

        Assert.AreEqual(new Position(0, 0), main.Range.Start);
        Assert.IsLessThanOrEqualTo(2, main.Range.End.Line, "not on the End Sub the program does not write, a line past its last");
    }

    [TestMethod]
    public async Task AModule_IsASymbol()
    {
        var module = (await SearchAsync(Sut(), "Class1")).First();

        Assert.AreEqual("Class1", module.Name);
        Assert.AreEqual(SymbolKind.Class, module.Kind);
        Assert.IsNull(module.ContainerName);
    }

    [TestMethod]
    public async Task ADocumentWhoseTextChanges_IsSearchedAsItIsNow()
    {
        var handler = Sut();
        Assert.IsNotEmpty(await SearchAsync(handler, "GetTotal"));

        Hold(new WorkspaceDocument("Helpers.bas", Root, Helpers.Replace("GetTotal", "SumOf"), version: 2));

        Assert.IsEmpty(await SearchAsync(handler, "GetTotal"));
        Assert.HasCount(1, await SearchAsync(handler, "SumOf"));
    }

    [TestMethod]
    public async Task NothingThatMatches_IsAnsweredWithNothing()
        => Assert.IsEmpty(await SearchAsync(Sut(), "zzz"));
}
