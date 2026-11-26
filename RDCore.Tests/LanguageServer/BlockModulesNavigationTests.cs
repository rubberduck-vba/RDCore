using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Completion;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A file of blocks is several modules: what the server answers at a position is of the module there, a block's or the file's own, and what it answers of
/// the whole document is of every module of it.
/// </summary>
[TestClass]
public sealed class BlockModulesNavigationTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-block-navigation-ws");

    // the file's own module (lines 0 to 3), a class block (4 to 9) and a module block (10 to 14).
    private const string Backup = "Attribute VB_Name = \"Backup\"\r\n"
        + "Public Sub Run()\r\n"
        + "    Debug.Print Helpers.Twice\r\n"
        + "End Sub\r\n"
        + "Class Account\r\n"
        + "Public Balance As Long\r\n"
        + "Public Sub Deposit()\r\n"
        + "    Balance = Balance + 1\r\n"
        + "End Sub\r\n"
        + "End Class\r\n"
        + "Module Helpers\r\n"
        + "Public Function Twice() As Long\r\n"
        + "    Twice = 2\r\n"
        + "End Function\r\n"
        + "End Module\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IOptions<SdkAppOptions> _options
        = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });

    public BlockModulesNavigationTests()
    {
        var backup = new WorkspaceDocument("Backup.bas", Root, Backup, version: 4);
        _documents.WorkspaceRoot.Returns(Root);
        _documents.GetAllDocuments().Returns([backup]);
        _documents.IsOpen(Arg.Any<TextDocumentIdentifier>()).Returns(true);
        var uri = backup.Id.Uri.ToUri();
        var parse = new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(backup));
        _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = backup; return true; });
        _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(parse));
        _parsing.TryGetCached(uri, out Arg.Any<ModuleParseResult>()).Returns(call => { call[1] = parse; return true; });
        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));
    }

    private static readonly Uri Document = DocumentUri.From(Path.Combine(Root, "Backup.bas")).ToUri();

    private static IReferencedLibraryService Libraries(ILibrarySource source)
    {
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(source, [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        return libraries;
    }

    private RenameService Rename()
        => new(_documents, _parsing, new IntrinsicSymbolResolver(), Libraries(new InMemoryLibrarySource()), _options, NullLogger<RenameService>.Instance,
            OneProject.Registry());

    private async Task<RenameWorkspace> ComposedAsync() => (await Rename().ComposeAsync(CancellationToken.None))!;

    [TestMethod]
    [DataRow(7, 16, 5, DisplayName = "a block's own field, from its procedure")]
    [DataRow(2, 26, 11, DisplayName = "a block's function, from the file's own module")]
    public async Task ANameInADocumentOfBlocks_IsDefinedInTheModuleThatDeclaresIt(int line, int character, int declaredOn)
    {
        var definitions = (await ComposedAsync()).Definitions(Document, new Position(line, character));

        Assert.AreEqual(Document.AbsoluteUri, definitions.Single().TargetUri.ToUri().AbsoluteUri);
        Assert.AreEqual(declaredOn, definitions.Single().TargetSelectionRange.Start.Line);
    }

    [TestMethod]
    public async Task AHoverInABlock_SaysWhatTheNameIs_InTheBlocksModule()
    {
        var hovered = (await ComposedAsync()).SymbolAt(Document, new Position(7, 16));

        Assert.AreEqual("Balance", hovered?.Symbol.Name);
        Assert.AreEqual(new Range(7, 14, 7, 21), hovered?.Range);
    }

    // a symbol of one module that another module of the document names is highlighted in both.
    [TestMethod]
    public async Task TheHighlightsOfAName_AreInEveryModuleOfTheDocument()
    {
        var highlights = (await ComposedAsync()).Highlights(Document, new Position(2, 26));

        CollectionAssert.AreEquivalent(new[] { new Range(2, 24, 2, 29), new Range(11, 16, 11, 21), new Range(12, 4, 12, 9) },
            highlights.Select(highlight => highlight.Range).ToArray());
    }

    [TestMethod]
    public async Task TheLensesOfADocument_AreOfTheMembersOfEachOfItsModules()
    {
        var lenses = (await ComposedAsync()).Lenses(Document);

        CollectionAssert.IsSubsetOf(new[] { 1, 6, 11 }, lenses.Select(lens => lens.Range.Start.Line).Distinct().ToArray());
    }

    // a rename that edits two modules of one document is one edit of the document, at its version: two would be a hazard to the client.
    [TestMethod]
    [DataRow(2, 26, DisplayName = "from the file's own module")]
    [DataRow(11, 18, DisplayName = "from the block that declares it")]
    public async Task ARenameAcrossTheModulesOfADocument_IsOneEditOfIt(int line, int character)
    {
        var answer = await Rename().RenameAsync(Document, new Position(line, character), "Thrice", CancellationToken.None);

        Assert.IsNotNull(answer.Value, answer.Refusal);
        var edit = answer.Value.DocumentChanges!.Single().TextDocumentEdit!;
        Assert.AreEqual(4, edit.TextDocument.Version);
        CollectionAssert.AreEquivalent(new[] { new Range(2, 24, 2, 29), new Range(11, 16, 11, 21), new Range(12, 4, 12, 9) },
            edit.Edits.Select(each => each.Range).ToArray());
    }

    // a block's module is named in the block's header: an edit of the file's VB_Name, or of its name, would rename the file's own module.
    [TestMethod]
    public async Task ABlocksModule_IsNotARenameTarget_SoNoEditIsMadeOfTheFile()
    {
        // Helpers, where the file's own module names it.
        var position = new Position(2, 18);

        var prepared = await Rename().PrepareAsync(Document, position, CancellationToken.None);
        var renamed = await Rename().RenameAsync(Document, position, "Doubles", CancellationToken.None);

        Assert.IsNull(prepared.Value, "no name to rename at the block's module");
        Assert.IsNull(renamed.Value, "neither the VB_Name attribute nor the file is edited");
    }

    // the twin of the row above: the file's own module is renamed as in a file of no blocks, its VB_Name and the file both.
    [TestMethod]
    public async Task TheFilesOwnModule_IsRenamed_ItsVbNameAndItsFile()
    {
        var answer = await Rename().RenameAsync(Document, new Position(0, 23), "Archive", CancellationToken.None);

        Assert.IsNotNull(answer.Value, answer.Refusal);
        var changes = answer.Value.DocumentChanges!.ToList();
        CollectionAssert.Contains(changes.Single(change => change.IsTextDocumentEdit).TextDocumentEdit!.Edits.Select(edit => edit.Range).ToArray(),
            new Range(0, 21, 0, 27));
        Assert.AreEqual(DocumentUri.From(Path.Combine(Root, "Archive.bas")).ToUri().AbsoluteUri,
            changes.Single(change => change.IsRenameFile).RenameFile!.NewUri.ToUri().AbsoluteUri);
    }

    private async Task<string[]> CompletedAtAsync(int line, int character)
    {
        var source = new InMemoryLibrarySource();
        var libraries = Libraries(source);
        var sut = new CompletionService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, source, new ClientVerbRegistry(), _options,
            NullLogger<CompletionService>.Instance, OneProject.Registry());
        return [.. (await sut.CompleteDocumentAsync(Document, new Position(line, character), CancellationToken.None)).Items.Select(item => item.Label)];
    }

    // a line of a class block is completed in that class: its fields are names there, and Me and RaiseEvent are statements of a class.
    [TestMethod]
    public async Task ALineOfAClassBlock_IsCompletedInThatClass()
    {
        var labels = await CompletedAtAsync(7, 4);

        CollectionAssert.Contains(labels, "Balance");
        CollectionAssert.Contains(labels, "RaiseEvent");
    }

    [TestMethod]
    public async Task ALineOfTheFilesOwnModule_IsCompletedInIt_WithTheBlocksModulesAmongItsNames()
    {
        var labels = await CompletedAtAsync(2, 4);

        CollectionAssert.Contains(labels, "Helpers");
        CollectionAssert.DoesNotContain(labels, "Balance");
        CollectionAssert.DoesNotContain(labels, "RaiseEvent");
    }
}
