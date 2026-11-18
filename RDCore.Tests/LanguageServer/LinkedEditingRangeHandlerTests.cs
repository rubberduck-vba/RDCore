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

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>textDocument/linkedEditingRange</c> answers, at the name of a local variable, a parameter or a line label, each place its procedure writes it, in
/// whatever case it is written: none for a name of the module, a name of another procedure, a parameter a call names in a named argument, or a
/// name written in brackets.
/// </summary>
[TestClass]
public sealed class LinkedEditingRangeHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-linked-editing-ws");

    private const string Main = """
        Attribute VB_Name = "Main"
        Option Explicit
        Private Total As Long

        Public Sub Run(ByVal Count As Long)
            Dim i As Long
            For i = 1 To count
                Total = Total + I
                If Total > 100 Then GoTo Done
            Next
        Done:
            Debug.Print Count
        End Sub

        Private Sub Other()
            Dim Total As Long
            Total = 1
            Debug.Print total
        End Sub

        Private Sub Retry()
            On Error GoTo [Try Again]
        [Try Again]:
        End Sub

        Private Sub Named(ByVal Size As Long)
            Debug.Print Size
        End Sub

        Private Sub Caller()
            Named Size:=1
        End Sub
        """;

    private static readonly string[] Lines = Main.ReplaceLineEndings("\n").Split('\n');

    private static LinkedEditingRangeHandler Sut()
    {
        var documents = Substitute.For<IWorkspaceDocumentService>();
        var parsing = Substitute.For<IParsingClientService>();
        documents.WorkspaceRoot.Returns(Root);
        var main = new WorkspaceDocument("Main.bas", Root, Main.ReplaceLineEndings("\r\n"), version: 1);
        documents.GetAllDocuments().Returns([main]);
        var uri = main.Id.Uri.ToUri();
        parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(main))));

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        return new LinkedEditingRangeHandler(new RenameService(documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance,
            OneProject.Registry()));
    }

    // the linked ranges at a name written on a line of Main (its last occurrence there): the text of each, and the line it is on.
    private static async Task<(string Text, int Line)[]> LinkedAsync(int line, string name)
    {
        var ranges = await Sut().Handle(new LinkedEditingRangeParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(Path.Combine(Root, "Main.bas"))),
            Position = new Position(line, Lines[line].LastIndexOf(name, StringComparison.Ordinal) + 1),
        }, CancellationToken.None);

        Assert.IsNotNull(ranges);
        Assert.IsNull(ranges.WordPattern, "the client checks the name with its own word pattern");
        Assert.IsTrue(ranges.Ranges.All(range => range.Start.Line == range.End.Line), "each range is on one line");
        return [.. ranges.Ranges.Select(range => (Lines[range.Start.Line][range.Start.Character..range.End.Character], range.Start.Line))];
    }

    [TestMethod]
    public async Task ALocal_IsLinked_WhereverItsProcedureWritesIt_InAnyCase()
        => CollectionAssert.AreEqual(new[] { ("i", 5), ("i", 6), ("I", 7) }, await LinkedAsync(line: 5, "i"), "Dim i, For i, + I");

    [TestMethod]
    public async Task AParameter_IsLinked_WhereverItsProcedureWritesIt_InAnyCase()
        => CollectionAssert.AreEqual(new[] { ("Count", 4), ("count", 6), ("Count", 11) }, await LinkedAsync(line: 6, "count"));

    [TestMethod]
    public async Task ALocalThatShadowsAModuleVariable_IsLinked_OnlyInsideItsProcedure()
        => CollectionAssert.AreEqual(new[] { ("Total", 15), ("Total", 16), ("total", 17) }, await LinkedAsync(line: 16, "Total"),
            "none of the module's Total, which Run writes");

    [TestMethod]
    public async Task ALineLabel_IsLinked_WhereItIsDefinedAndWhereItIsJumpedTo()
        => CollectionAssert.AreEqual(new[] { ("Done", 8), ("Done", 10) }, await LinkedAsync(line: 8, "Done"));

    [TestMethod]
    public async Task AModuleVariable_AProcedure_AndAnotherModulesName_AreNotLinked()
    {
        Assert.IsEmpty(await LinkedAsync(line: 7, "Total"), "a variable of the module");
        Assert.IsEmpty(await LinkedAsync(line: 4, "Run"), "a procedure");
        Assert.IsEmpty(await LinkedAsync(line: 11, "Debug"), "a name of a library");
    }

    [TestMethod]
    public async Task ANameWrittenInBrackets_IsNotLinked()
        => Assert.IsEmpty(await LinkedAsync(line: 21, "Try"));

    [TestMethod]
    public async Task AParameterThatANamedArgumentNamesOutsideItsProcedure_IsNotLinked()
        => Assert.IsEmpty(await LinkedAsync(line: 26, "Size"), "Named Size:=1, in Caller");
}
