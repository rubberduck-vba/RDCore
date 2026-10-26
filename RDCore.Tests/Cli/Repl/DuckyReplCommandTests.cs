using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Semantics.Refactoring;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// <c>DUCKY</c> lists what the platform can do about the program, and does what is chosen: an edit is made to the buffer, and a rename asks for the new name.
/// </summary>
[TestClass]
public sealed class DuckyReplCommandTests
{
    private static readonly string ProgramPath = Path.Combine(Path.GetTempPath(), "rdcore-ducky-ws", "hello.rdc");
    private static readonly Uri Document = new(ProgramPath);

    private readonly ReplProgram _program = new();
    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();
    private readonly ScriptedKeys _keys = new(interactive: false);
    private readonly ReplDocument _document;
    private readonly ReplCommandContext _context;

    public DuckyReplCommandTests()
    {
        _program.Store(10, "Dim strName As String");
        _program.Store(20, "strName = \"a\"");
        _document = new ReplDocument(_program, _platform);
        _context = new ReplCommandContext(_program, _console, _platform, _document, []);
        _platform.PullDiagnosticsAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private async Task RunAsync(string arguments = "")
    {
        await _document.OpenAsync(ProgramPath, CancellationToken.None);
        await new DuckyReplCommand(new ReplUserInteraction(_keys, new StringWriter())).ExecuteAsync(_context, arguments, CancellationToken.None);
    }

    private void Offers(params CodeAction[] actions)
        => _platform.GetCodeActionsAsync(Arg.Any<Uri>(), Arg.Any<Range>(), Arg.Any<IReadOnlyList<Diagnostic>>(), Arg.Any<CancellationToken>()).Returns(actions);

    // the URI of the program as the server writes it: what a client reads back of DocumentUri, whose drive is c%3A where the shell's is C:.
    private static DocumentUri AsWritten => DocumentUri.Parse(DocumentUri.From(Document).ToString());

    // the edits of the program, at the version of the buffer.
    private WorkspaceEdit Edits(params TextEdit[] edits) => new()
    {
        DocumentChanges = new Container<WorkspaceEditDocumentChange>(new WorkspaceEditDocumentChange(new TextDocumentEdit
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = AsWritten, Version = _program.Version },
            Edits = new TextEditContainer(edits),
        })),
    };

    private static TextEdit Write(int line, int start, int end, string text) => new() { Range = new Range(line, start, line, end), NewText = text };

    private static CodeAction RenameCommand(string name, int line, int character) => new()
    {
        Title = RefactoringTitles.Rename(name),
        Command = new Command
        {
            Name = RefactoringCommands.Rename,
            Arguments = new JArray(DocumentUri.From(Document).ToString(), JToken.FromObject(new { line, character })),
        },
    };

    private string Text => _program.ToSourceText();

    [TestMethod]
    public async Task TheActionChosen_IsMadeToTheBuffer()
    {
        Offers(new CodeAction { Title = "Rename 'strName' to 'Name'", Edit = Edits(Write(0, 7, 14, "Name"), Write(1, 3, 10, "Name")) });
        _keys.Lines("1");

        await RunAsync();

        Assert.AreEqual("10 Dim Name As String\r\n20 Name = \"a\"\r\n", Text);
        _console.Received().WriteMessage(MessageKind.Success, Arg.Is<string>(message => message.Contains("Rename 'strName' to 'Name'")), Arg.Any<string?>());
    }

    [TestMethod]
    public async Task ARenameCommand_AsksForTheName_AndHasThePlatformRenameTheSymbol()
    {
        Offers(RenameCommand("strName", 0, 7));
        _platform.PrepareRenameAsync(Arg.Any<Uri>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new ReplRenameAnswer<PlaceholderRange>(new PlaceholderRange { Range = new Range(0, 7, 0, 14), Placeholder = "strName" }, null));
        _platform.RenameAsync(Arg.Any<Uri>(), new Position(0, 7), "Title", Arg.Any<CancellationToken>())
            .Returns(call => new ReplRenameAnswer<WorkspaceEdit>(Edits(Write(0, 7, 14, "Title"), Write(1, 3, 10, "Title")), null));
        _keys.Lines("1", "Title");

        await RunAsync("10");

        Assert.AreEqual("10 Dim Title As String\r\n20 Title = \"a\"\r\n", Text);
    }

    [TestMethod]
    public async Task ARenameThatIsCancelled_RenamesNothing()
    {
        Offers(RenameCommand("strName", 0, 7));
        _platform.PrepareRenameAsync(Arg.Any<Uri>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new ReplRenameAnswer<PlaceholderRange>(new PlaceholderRange { Range = new Range(0, 7, 0, 14), Placeholder = "strName" }, null));
        _keys.Lines("1", null);

        await RunAsync();

        Assert.AreEqual("10 Dim strName As String\r\n20 strName = \"a\"\r\n", Text);
        await _platform.DidNotReceive().RenameAsync(Arg.Any<Uri>(), Arg.Any<Position>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ARenameThePlatformRefuses_SaysWhy_AndRenamesNothing()
    {
        Offers(RenameCommand("strName", 0, 7));
        _platform.PrepareRenameAsync(Arg.Any<Uri>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new ReplRenameAnswer<PlaceholderRange>(new PlaceholderRange { Range = new Range(0, 7, 0, 14), Placeholder = "strName" }, null));
        _platform.RenameAsync(Arg.Any<Uri>(), Arg.Any<Position>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReplRenameAnswer<WorkspaceEdit>(null, "'Next' is a keyword of the language: nothing can be named 'Next'."));
        _keys.Lines("1", "Next");

        await RunAsync();

        Assert.AreEqual("10 Dim strName As String\r\n20 strName = \"a\"\r\n", Text);
        _console.Received().WriteMessage(MessageKind.Error, Arg.Any<string>(), "'Next' is a keyword of the language: nothing can be named 'Next'.");
    }

    [TestMethod]
    public async Task AnEditOfADocumentTheShellDoesNotHold_IsNotMade()
    {
        var edit = Edits(Write(0, 7, 14, "Name"));
        edit = edit with
        {
            DocumentChanges = new Container<WorkspaceEditDocumentChange>([.. edit.DocumentChanges!, new WorkspaceEditDocumentChange(new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.File(Path.Combine(Path.GetTempPath(), "Helpers.bas")), Version = 1 },
                Edits = new TextEditContainer(Write(0, 0, 1, "x")),
            })]),
        };
        Offers(new CodeAction { Title = "Rename", Edit = edit });
        _keys.Lines("1");

        await RunAsync();

        Assert.AreEqual("10 Dim strName As String\r\n20 strName = \"a\"\r\n", Text);
        _console.Received().WriteMessage(MessageKind.Error, Arg.Any<string>(), Arg.Is<string>(verbose => verbose.Contains("Helpers.bas")));
    }

    [TestMethod]
    public async Task NoAction_IsNothingToDo()
    {
        Offers();

        await RunAsync();

        _console.Received().WriteLine(Resources.Repl_Ducky_NothingToDo);
    }

    [TestMethod]
    public async Task ALineTheProgramDoesNotHave_IsUndefined()
    {
        await RunAsync("99");

        _console.Received().WriteMessage(MessageKind.Error, Resources.Repl_UndefinedLine, "99");
        await _platform.DidNotReceive().GetCodeActionsAsync(Arg.Any<Uri>(), Arg.Any<Range>(), Arg.Any<IReadOnlyList<Diagnostic>>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void TheEditsOfAText_AreMadeFromTheLastToTheFirst()
        => Assert.AreEqual("10 Dim count As Long\r\n20 count = count + 1\r\n", ReplTextEdits.Apply("10 Dim x As Long\r\n20 x = x + 1\r\n",
            [Write(0, 7, 8, "count"), Write(1, 3, 4, "count"), Write(1, 7, 8, "count")]));
}
