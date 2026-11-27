using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Folding;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A selection grows, step by step, through each node of the syntax tree written around the position: a name, its expression, its statement, the block, the
/// procedure, the module.
/// </summary>
[TestClass]
public sealed class SelectionRangeHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-selection-range-ws");

    private const string Module1 = """
        Attribute VB_Name = "Module1"
        Public Sub Main()
            If True Then
                Debug.Print Len("abc") + 1
            End If
        End Sub
        """;

    private const string Program = "10 Dim x As Long\r\n20 x = Len(\"abc\") + 1\r\n";

    // the file's own module (lines 0 to 2), and a class block (3 to 7).
    private const string Backup = "Attribute VB_Name = \"Backup\"\r\nPublic Sub Run()\r\nEnd Sub\r\n"
        + "Class Account\r\nPublic Sub Deposit()\r\n    Debug.Print Len(\"abc\")\r\nEnd Sub\r\nEnd Class\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    public SelectionRangeHandlerTests()
    {
        WorkspaceDocument[] held =
            [new("Module1.bas", Root, Module1, version: 1), new("Main.rdc", Root, Program, version: 1), new("Backup.bas", Root, Backup, version: 1)];
        _documents.Holds(held);
        // Backup.bas has blocks, in a workspace that allows statements outside procedures.
        foreach (var document in held)
        {
            var uri = document.Id.Uri.ToUri();
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(FileLevelStatementsOn.Parser().Parse(uri, ParsingClientService.ModuleSourceOf(document))));
        }
    }

    private async Task<SelectionRange> SelectAsync(string fileName, Position position)
        => (await new SelectionRangeHandler(_documents, _parsing).Handle(new SelectionRangeParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(Path.Combine(Root, fileName))),
            Positions = new Container<Position>(position),
        }, CancellationToken.None))!.Single();

    // the ranges of the selection, innermost first.
    private static List<Range> Chain(SelectionRange selection)
    {
        var ranges = new List<Range>();
        for (var step = selection; step is not null; step = step.Parent)
        {
            ranges.Add(step.Range);
        }

        return ranges;
    }

    private static bool Contains(Range outer, Range inner)
        => (outer.Start.Line, outer.Start.Character).CompareTo((inner.Start.Line, inner.Start.Character)) <= 0
            && (inner.End.Line, inner.End.Character).CompareTo((outer.End.Line, outer.End.Character)) <= 0;

    [TestMethod]
    public async Task ASelectionInAName_GrowsToItsExpression_ItsStatement_TheBlock_TheProcedure_AndTheModule()
    {
        // in "Len", on the Debug.Print line inside the If.
        var chain = Chain(await SelectAsync("Module1.bas", new Position(3, 22)));
        var lines = Module1.Split('\n');

        Assert.IsGreaterThanOrEqualTo(5, chain.Count);
        for (var i = 1; i < chain.Count; i++)
        {
            Assert.IsTrue(Contains(chain[i], chain[i - 1]) && chain[i] != chain[i - 1], $"step {i} grows the one before it");
        }

        Assert.AreEqual(3, chain[0].Start.Line, "the innermost step is on the line of the position");
        Assert.IsTrue(chain.Any(range => range.Start.Line == 2 && range.End.Line == 4), "the If block, from If to End If");
        Assert.IsTrue(chain.Any(range => range.Start.Line == 1 && range.End.Line == 5), "the procedure, from Sub to End Sub");
        Assert.AreEqual(lines.Length - 1, chain[^1].End.Line, "the outermost step is the whole module");
    }

    // a position in a block is in the block's module: the selection grows through its nodes to its procedure, and last to the block, header to end.
    [TestMethod]
    public async Task ASelectionInABlock_GrowsInTheBlocksModule_ToTheBlock()
    {
        // in "Len", in the class block's procedure.
        var chain = Chain(await SelectAsync("Backup.bas", new Position(5, 18)));

        Assert.AreEqual(5, chain[0].Start.Line, "the innermost step is on the line of the position");
        Assert.IsTrue(chain.Any(range => range.Start.Line == 4 && range.End.Line == 6), "the procedure, from Sub to End Sub");
        Assert.AreEqual(new Range(3, 0, 7, 9), chain[^1], "the outermost step is the block");
        Assert.IsTrue(chain.All(range => range.Start.Line >= 3), "nothing of the file's own module");
    }

    // the twin of the test above: a position in the file's own module grows in that module, and never to the block.
    [TestMethod]
    public async Task ASelectionInTheFilesOwnModule_DoesNotGrowToABlock()
    {
        var chain = Chain(await SelectAsync("Backup.bas", new Position(1, 12)));

        Assert.IsTrue(chain.Any(range => range.Start.Line == 1 && range.End.Line == 2), "the procedure Run");
        Assert.IsTrue(chain.All(range => range.End.Line <= 2), "nothing of the block");
    }

    [TestMethod]
    public async Task ASelectionInAProgram_IsInItsLines()
    {
        var chain = Chain(await SelectAsync("Main.rdc", new Position(1, 8)));

        Assert.AreEqual(1, chain[0].Start.Line, "20 x = Len(...), the program's second line");
        Assert.IsTrue(chain.All(range => range.Start.Line >= 0));
    }

    [TestMethod]
    public async Task APositionOfADocumentTheWorkspaceDoesNotHold_IsAnsweredWithAnEmptyRange()
    {
        var selection = await SelectAsync("Nowhere.bas", new Position(2, 3));

        Assert.AreEqual(new Range(2, 3, 2, 3), selection.Range);
        Assert.IsNull(selection.Parent);
    }
}
