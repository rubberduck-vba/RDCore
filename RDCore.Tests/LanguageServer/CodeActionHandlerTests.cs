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
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The code actions of a range of a document: the renames that fix its naming diagnostics, and a rename of each name it declares.
/// </summary>
[TestClass]
public sealed class CodeActionHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-codeaction-ws");

    private const string Program = "10 Dim strName As String\r\n20 Dim x As Long\r\n30 strName = \"a\"\r\n40 Debug.Print strName, x\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    public CodeActionHandlerTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        var program = new WorkspaceDocument("Main.rdc", Root, Program, version: 2);
        _documents.GetAllDocuments().Returns([program]);
        var uri = program.Id.Uri.ToUri();
        _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = program; return true; });
        _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(program))));
    }

    private CodeActionHandler Sut()
    {
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        return new CodeActionHandler(new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance),
            _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem());
    }

    private static DocumentUri Document => DocumentUri.From(Path.Combine(Root, "Main.rdc"));

    private static Diagnostic Finding(Range range, params string[] suggestions) => new()
    {
        Range = range,
        Code = "RDC01002",
        Source = "RDCore",
        Message = "Hungarian",
        Data = new DiagnosticFixes { Rename = new RenameFix { Suggestions = suggestions } }.ToData(),
    };

    private async Task<List<CodeAction>> ActionsAsync(Range range, CodeActionKind[]? only = null, params Diagnostic[] diagnostics)
    {
        var culture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            var answer = await Sut().Handle(new CodeActionParams
            {
                TextDocument = new TextDocumentIdentifier(Document),
                Range = range,
                Context = new CodeActionContext
                {
                    Diagnostics = new Container<Diagnostic>(diagnostics),
                    Only = only is null ? null : new Container<CodeActionKind>(only),
                },
            }, CancellationToken.None);
            return [.. answer!.Select(item => item.CodeAction!)];
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }

    [TestMethod]
    public async Task ADiagnosticFixedByARename_IsOfferedTheRenameToEachSuggestion_AndToANameTheUserChooses()
    {
        var diagnostic = Finding(new Range(0, 7, 0, 14), "Name");

        var actions = await ActionsAsync(new Range(0, 7, 0, 14), [CodeActionKind.QuickFix], diagnostic);

        Assert.HasCount(2, actions);
        Assert.AreEqual("Rename 'strName' to 'Name'", actions[0].Title);
        Assert.IsTrue(actions[0].IsPreferred);
        var edits = actions[0].Edit!.DocumentChanges!.Single().TextDocumentEdit!.Edits.Select(edit => edit.Range).ToArray();
        CollectionAssert.AreEqual(new[] { new Range(0, 7, 0, 14), new Range(2, 3, 2, 10), new Range(3, 15, 3, 22) }, edits);

        Assert.AreEqual("Rename 'strName'…", actions[1].Title);
        Assert.AreEqual(RefactoringCommands.Rename, actions[1].Command!.Name);
        Assert.AreEqual(Document.ToString(), actions[1].Command!.Arguments![0].ToString());
        Assert.AreEqual(diagnostic, actions[1].Diagnostics!.Single());
    }

    [TestMethod]
    public async Task ADiagnosticFixedByEdits_IsOfferedTheEdits_AsTheAnalyzerMadeThem()
    {
        var edits = new[]
        {
            new TextEdit { Range = new Range(1, 0, 1, 0), NewText = "    Dim result As Long\r\n" },
            new TextEdit { Range = new Range(2, 4, 2, 9), NewText = "result" },
        };
        var diagnostic = new Diagnostic
        {
            Range = new Range(2, 4, 2, 9),
            Code = "RDC00115",
            Source = "RDCore",
            Message = "Implicit result local",
            Data = new DiagnosticFixes { Edit = new EditFix { Title = "Declare a local for the return value of 'Total'", Edits = edits } }.ToData(),
        };

        var action = Assert.ContainsSingle(await ActionsAsync(new Range(2, 4, 2, 9), [CodeActionKind.QuickFix], diagnostic));

        Assert.AreEqual("Declare a local for the return value of 'Total'", action.Title);
        Assert.IsTrue(action.IsPreferred);
        Assert.AreEqual(diagnostic, action.Diagnostics!.Single());
        var (document, changes) = action.Edit!.Changes!.Single();
        Assert.AreEqual(Document, document);
        CollectionAssert.AreEqual(edits.Select(edit => (edit.Range, edit.NewText)).ToArray(), changes.Select(edit => (edit.Range, edit.NewText)).ToArray());
    }

    [TestMethod]
    public async Task ASuggestionThatCannotBeMade_IsNotOffered()
    {
        var actions = await ActionsAsync(new Range(0, 7, 0, 14), [CodeActionKind.QuickFix], Finding(new Range(0, 7, 0, 14), "x"));

        CollectionAssert.AreEqual(new[] { "Rename 'strName'…" }, actions.Select(action => action.Title).ToArray(), "x is declared already");
    }

    [TestMethod]
    public async Task EachNameARangeDeclares_IsOfferedARename()
    {
        var actions = await ActionsAsync(new Range(0, 0, 3, 0));

        CollectionAssert.AreEqual(new[] { "Rename 'strName'…", "Rename 'x'…" }, actions.Select(action => action.Title).ToArray());
        Assert.IsTrue(actions.All(action => action.Kind == CodeActionHandler.RefactorRename));
    }

    [TestMethod]
    public async Task AtAPosition_TheNameThere_IsOfferedARename()
    {
        var actions = await ActionsAsync(new Range(3, 24, 3, 24));

        CollectionAssert.AreEqual(new[] { "Rename 'x'…" }, actions.Select(action => action.Title).ToArray());
    }

    [TestMethod]
    public async Task AClientThatAsksForQuickFixesOnly_IsNotOfferedTheRefactorings()
        => Assert.IsEmpty(await ActionsAsync(new Range(0, 0, 3, 0), [CodeActionKind.QuickFix]));
}
