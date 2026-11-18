using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Debugging;
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
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>textDocument/inlineValue</c> answers, while the program is stopped, a lookup for each name of a local variable, a parameter or a module's variable
/// written in the procedure the program is stopped in, from its first line to the line of the stop, in the range asked for: none for a call, a constant,
/// a declaration, a name below the stop, or a name of another procedure.
/// </summary>
[TestClass]
public sealed class InlineValueHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-inline-value-ws");

    private const string Main = """
        Attribute VB_Name = "Main"
        Option Explicit
        Private Total As Long
        Private Const Limit As Long = 10

        Public Sub Run(ByVal Count As Long)
            Dim i As Long
            For i = 1 To Count
                Total = Total + i
                Helper
                Total = Total + Limit
            Next
            Dim later As Long
            later = Total
        End Sub

        Private Sub Helper()
            Dim other As Long
            other = 1
        End Sub
        """;

    private static InlineValueHandler Sut()
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
        return new InlineValueHandler(new RenameService(documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance,
            OneProject.Registry()));
    }

    // the lookups of a range of Main, the program stopped on a line of it: each name, and the line it is written on.
    private static async Task<(string Name, int Line)[]> LookupsAsync(int stoppedLine, Range? range = null)
    {
        var values = await Sut().Handle(new InlineValueParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(Path.Combine(Root, "Main.bas"))),
            Range = range ?? new Range(0, 0, 20, 0),
            Context = new InlineValueContext { FrameId = 1, StoppedLocation = new Range(stoppedLine, 8, stoppedLine, 29) },
        }, CancellationToken.None);

        Assert.IsNotNull(values);
        Assert.IsTrue(values.All(value => value is InlineValueVariableLookup { CaseSensitiveLookup: false }), "each value is a variable the client looks up");
        return [.. values.OfType<InlineValueVariableLookup>().Select(lookup => (lookup.VariableName!, lookup.Range.Start.Line))];
    }

    [TestMethod]
    public async Task TheLocalsTheParametersAndTheModulesVariables_AreLookedUp_FromTheProcedureStartToTheStop()
    {
        var lookups = await LookupsAsync(stoppedLine: 10);

        CollectionAssert.AreEqual(new[] { ("i", 7), ("Count", 7), ("Total", 8), ("Total", 8), ("i", 8), ("Total", 10), ("Total", 10) }, lookups,
            string.Join(", ", lookups));
    }

    [TestMethod]
    public async Task ACallAConstantAndANameBelowTheStop_AreNotLookedUp()
    {
        var names = (await LookupsAsync(stoppedLine: 10)).Select(lookup => lookup.Name).ToArray();

        CollectionAssert.DoesNotContain(names, "Helper", "a call");
        CollectionAssert.DoesNotContain(names, "Limit", "a constant");
        CollectionAssert.DoesNotContain(names, "later", "below the stop");
    }

    [TestMethod]
    public async Task TheNamesOfAnotherProcedure_AreNotLookedUp()
        => CollectionAssert.AreEqual(new[] { ("other", 18) }, await LookupsAsync(stoppedLine: 18));

    [TestMethod]
    public async Task OnlyTheNamesInTheRangeAskedFor_AreLookedUp()
        => CollectionAssert.AreEqual(new[] { ("Total", 8), ("Total", 8), ("i", 8) }, await LookupsAsync(stoppedLine: 10, new Range(8, 0, 9, 0)));

    [TestMethod]
    public async Task AStopInNoProcedure_IsAnsweredNothing()
        => Assert.IsEmpty(await LookupsAsync(stoppedLine: 2));
}
