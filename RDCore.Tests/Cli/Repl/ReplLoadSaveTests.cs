using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.SDK.ConsoleIO.Model;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// <c>LOAD</c> and <c>SAVE</c> read and write a <c>.rdc</c> file, and the language server is told of the program as an editor tells it of a document
/// (<strong>LSP 3.17</strong> §Text Document Synchronization): opened, changed, about to be saved, saved, closed.
/// </summary>
[TestClass]
public sealed class ReplLoadSaveTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-repl-ws");
    private static readonly string ProgramPath = Path.Combine(Root, "hello.rdc");

    private readonly MockFileSystem _files = new(new Dictionary<string, MockFileData>(), Root);
    private readonly ReplProgram _program = new();
    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();
    private readonly ReplDocument _document;
    private readonly ReplCommandContext _context;

    public ReplLoadSaveTests()
    {
        _platform.WillSaveDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<TextEdit>>([]));
        _document = new ReplDocument(_program, _platform);
        _context = new ReplCommandContext(_program, _console, _platform, _document, []);
    }

    private Task<ReplCommandResult> LoadAsync(string arguments) => new LoadReplCommand(_files).ExecuteAsync(_context, arguments, CancellationToken.None);

    private Task<ReplCommandResult> SaveAsync(string arguments) => new SaveReplCommand(_files).ExecuteAsync(_context, arguments, CancellationToken.None);

    // ---- the buffer as a file ----

    [TestMethod]
    public void TheTextOfAProgram_IsItsLinesInNumberOrder()
    {
        _program.Store(20, "Print X");
        _program.Store(10, "X = 1");

        Assert.AreEqual("10 X = 1\r\n20 Print X\r\n", _program.ToSourceText());
    }

    [TestMethod]
    public void AProgramThatIsLoaded_ReplacesWhatWasThere_AndIsANewVersion()
    {
        _program.Store(5, "old");
        var version = _program.Version;

        var rejected = _program.Load("30 Print X\n10 X = 1\r\n\r\n");

        Assert.IsEmpty(rejected);
        CollectionAssert.AreEqual(new[] { 10, 30 }, _program.Lines().Select(line => line.Number).ToArray());
        Assert.IsGreaterThan(version, _program.Version);
    }

    [TestMethod]
    public void AFileThatIsNotAProgram_LeavesTheProgramAsItWas_AndNamesTheLines()
    {
        _program.Store(5, "kept");

        var rejected = _program.Load("10 X = 1\r\nSub Foo()\r\n20\r\n");

        CollectionAssert.AreEqual(new[] { 2, 3 }, rejected.ToArray());
        CollectionAssert.AreEqual(new[] { 5 }, _program.Lines().Select(line => line.Number).ToArray());
    }

    // ---- LOAD ----

    [TestMethod]
    public async Task Load_ReplacesTheProgram_AndOpensItAsADocumentOfTheVersionItHas()
    {
        _files.AddFile(ProgramPath, new MockFileData("10 X = 1\r\n"));

        await LoadAsync("hello");

        Assert.AreEqual("10 X = 1\r\n", _program.ToSourceText());
        Assert.AreEqual(new Uri(ProgramPath), _document.Uri);
        await _platform.Received(1).OpenDocumentAsync(new Uri(ProgramPath), "10 X = 1\r\n", _program.Version, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Load_OfAFileThatIsNotThere_IsAnError_AndTheProgramIsLeftAlone()
    {
        _program.Store(10, "kept");

        await LoadAsync("nope.rdc");

        _console.Received(1).WriteMessage(MessageKind.Error, Arg.Any<string>(), Arg.Any<string?>());
        Assert.AreEqual(1, _program.Count);
        await _platform.DidNotReceiveWithAnyArgs().OpenDocumentAsync(default!, default!, default, default);
    }

    [TestMethod]
    public async Task Load_OfAFileThatIsNotAProgram_IsAnError_AndTheDocumentIsNotOpened()
    {
        _files.AddFile(ProgramPath, new MockFileData("Public Sub Foo()\r\nEnd Sub"));

        await LoadAsync("hello.rdc");

        _console.Received(1).WriteMessage(MessageKind.Error, Arg.Any<string>(), Arg.Any<string?>());
        Assert.IsFalse(_document.IsOpen);
    }

    [TestMethod]
    public async Task Load_OfAnotherFile_ClosesTheDocumentThatWasOpen()
    {
        _files.AddFile(ProgramPath, new MockFileData("10 X = 1\r\n"));
        _files.AddFile(Path.Combine(Root, "other.rdc"), new MockFileData("10 Y = 2\r\n"));
        await LoadAsync("hello");

        await LoadAsync("other");

        Received.InOrder(() =>
        {
            _platform.OpenDocumentAsync(new Uri(ProgramPath), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
            _platform.CloseDocumentAsync(new Uri(ProgramPath), Arg.Any<CancellationToken>());
            _platform.OpenDocumentAsync(new Uri(Path.Combine(Root, "other.rdc")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        });
    }

    // ---- the lines typed after it ----

    [TestMethod]
    public async Task AnEditOfALoadedProgram_IsAChangeOfTheDocument_WithTheWholeTextAndTheNextVersion()
    {
        _files.AddFile(ProgramPath, new MockFileData("10 X = 1\r\n"));
        await LoadAsync("hello");

        _program.Store(20, "Print X");
        await _document.SynchronizeAsync(CancellationToken.None);

        await _platform.Received(1).ChangeDocumentAsync(new Uri(ProgramPath), _program.Version, "10 X = 1\r\n20 Print X\r\n", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task NothingEditedSinceTheServerWasTold_IsNotAChange()
    {
        _files.AddFile(ProgramPath, new MockFileData("10 X = 1\r\n"));
        await LoadAsync("hello");

        await _document.SynchronizeAsync(CancellationToken.None);

        await _platform.DidNotReceiveWithAnyArgs().ChangeDocumentAsync(default!, default, default!, default);
    }

    [TestMethod]
    public async Task AProgramThatIsNotADocument_IsNeverChanged()
    {
        _program.Store(10, "X = 1");

        await _document.SynchronizeAsync(CancellationToken.None);

        await _platform.DidNotReceiveWithAnyArgs().ChangeDocumentAsync(default!, default, default!, default);
    }

    // ---- SAVE ----

    [TestMethod]
    public async Task Save_WritesTheFile_BetweenTheServerBeingToldItIsAboutToAndItBeingToldItDid()
    {
        _program.Store(10, "X = 1");

        await SaveAsync("hello");

        Assert.AreEqual("10 X = 1\r\n", _files.File.ReadAllText(ProgramPath));
        Received.InOrder(() =>
        {
            _platform.OpenDocumentAsync(new Uri(ProgramPath), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
            _platform.WillSaveDocumentAsync(new Uri(ProgramPath), Arg.Any<CancellationToken>());
            _platform.SaveDocumentAsync(new Uri(ProgramPath), "10 X = 1\r\n", Arg.Any<CancellationToken>());
        });
    }

    [TestMethod]
    public async Task Save_WithNoName_IsTheFileTheProgramCameFrom_AndTheServerIsToldOfTheEditsFirst()
    {
        _files.AddFile(ProgramPath, new MockFileData("10 X = 1\r\n"));
        await LoadAsync("hello");
        _program.Store(20, "Print X");

        await SaveAsync("");

        Assert.AreEqual("10 X = 1\r\n20 Print X\r\n", _files.File.ReadAllText(ProgramPath));
        Received.InOrder(() =>
        {
            _platform.ChangeDocumentAsync(new Uri(ProgramPath), Arg.Any<int>(), "10 X = 1\r\n20 Print X\r\n", Arg.Any<CancellationToken>());
            _platform.WillSaveDocumentAsync(new Uri(ProgramPath), Arg.Any<CancellationToken>());
            _platform.SaveDocumentAsync(new Uri(ProgramPath), "10 X = 1\r\n20 Print X\r\n", Arg.Any<CancellationToken>());
        });
        await _platform.Received(1).OpenDocumentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Save_WithNoName_OfAProgramThatCameFromNowhere_IsAnError()
    {
        _program.Store(10, "X = 1");

        await SaveAsync("");

        _console.Received(1).WriteMessage(MessageKind.Error, Arg.Any<string>(), Arg.Any<string?>());
        Assert.IsFalse(_files.FileExists(ProgramPath));
    }

    [TestMethod]
    public async Task Save_ToAnotherFile_IsTheOtherFileThatIsOpen()
    {
        _files.AddFile(ProgramPath, new MockFileData("10 X = 1\r\n"));
        await LoadAsync("hello");

        await SaveAsync("copy");

        var copy = Path.Combine(Root, "copy.rdc");
        Assert.IsTrue(_files.FileExists(copy));
        Assert.AreEqual(new Uri(copy), _document.Uri);
        await _platform.Received(1).CloseDocumentAsync(new Uri(ProgramPath), Arg.Any<CancellationToken>());
    }

    // ---- NEW, EXIT ----

    [TestMethod]
    public async Task New_ClosesTheDocument_AndEmptiesTheProgram()
    {
        _files.AddFile(ProgramPath, new MockFileData("10 X = 1\r\n"));
        await LoadAsync("hello");

        await new NewReplCommand().ExecuteAsync(_context, "", CancellationToken.None);

        Assert.IsTrue(_program.IsEmpty);
        Assert.IsFalse(_document.IsOpen);
        await _platform.Received(1).CloseDocumentAsync(new Uri(ProgramPath), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Exit_ClosesTheDocument()
    {
        _files.AddFile(ProgramPath, new MockFileData("10 X = 1\r\n"));
        await LoadAsync("hello");

        var result = await new ExitReplCommand().ExecuteAsync(_context, "", CancellationToken.None);

        Assert.AreEqual(ReplCommandResult.Exit, result);
        await _platform.Received(1).CloseDocumentAsync(new Uri(ProgramPath), Arg.Any<CancellationToken>());
    }
}
