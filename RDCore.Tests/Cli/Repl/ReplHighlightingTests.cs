using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Themes;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;
using Spectre.Console;
using Spectre.Console.Testing;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// A listing is highlighted by the semantic tokens the language server answers with (<strong>LSP 3.17</strong> §Semantic Tokens), in the colours of the theme.
/// </summary>
[TestClass]
public sealed class ReplHighlightingTests
{
    private static int Type(string name) => SemanticTokenLegend.TokenTypes.ToList().IndexOf(name);

    // ---- the answer ----

    [TestMethod]
    public void TheAnswer_IsFiveIntegersATokenAndEachIsRelativeToTheOneBefore()
    {
        var tokens = ReplSemanticToken.Decode([0, 0, 3, 0, 0, 0, 4, 1, 5, 0, 2, 2, 5, 3, 0]);

        CollectionAssert.AreEqual(
            new[]
            {
                new ReplSemanticToken(0, 0, 3, 0, 0),
                new ReplSemanticToken(0, 4, 1, 5, 0),
                new ReplSemanticToken(2, 2, 5, 3, 0),
            },
            tokens.ToArray());
    }

    [TestMethod]
    public void AnAnswerThatIsNotWholeTokens_IsDecodedAsFarAsItGoes()
        => Assert.HasCount(1, ReplSemanticToken.Decode([0, 0, 3, 0, 0, 1, 2]));

    [TestMethod]
    [DataRow("keyword", 0, ReplTextStyle.Keyword)]
    [DataRow("comment", 0, ReplTextStyle.Comment)]
    [DataRow("string", 0, ReplTextStyle.String)]
    [DataRow("number", 0, ReplTextStyle.Number)]
    [DataRow("operator", 0, ReplTextStyle.Plain)]
    [DataRow("variable", 0, ReplTextStyle.Identifier)]
    [DataRow("variable", SemanticTokenLegend.ReadOnly, ReplTextStyle.IdentifierConst)]
    [DataRow("class", 0, ReplTextStyle.IdentifierClass)]
    public void ATokenIsShownAsTheThemeShowsItsKind(string type, int modifiers, ReplTextStyle expected)
        => Assert.AreEqual(expected, new ReplSemanticToken(0, 0, 1, Type(type), modifiers).Style);

    // ---- the runs ----

    [TestMethod]
    public void ALine_IsCutIntoTheRunsItsTokensMake_AndTheTextBetweenThemIsPlain()
    {
        var runs = ReplHighlighter.Runs("10 Dim x", [new(0, 0, 2, Type("number"), 0), new(0, 3, 3, Type("keyword"), 0), new(0, 7, 1, Type("variable"), 0)]);

        CollectionAssert.AreEqual(
            new[]
            {
                new ReplTextRun("10", ReplTextStyle.Number),
                new ReplTextRun(" ", ReplTextStyle.Plain),
                new ReplTextRun("Dim", ReplTextStyle.Keyword),
                new ReplTextRun(" ", ReplTextStyle.Plain),
                new ReplTextRun("x", ReplTextStyle.Identifier),
            },
            runs.ToArray());
    }

    [TestMethod]
    public void ALineWithNoTokens_IsOneRun_AndSoIsTheTextAfterTheLastOne()
    {
        Assert.AreEqual(new ReplTextRun("x = 1", ReplTextStyle.Plain), ReplHighlighter.Runs("x = 1", []).Single());
        Assert.AreEqual(new ReplTextRun(" = 1", ReplTextStyle.Plain), ReplHighlighter.Runs("x = 1", [new(0, 0, 1, Type("variable"), 0)])[1]);
    }

    [TestMethod]
    public void ATokenThatIsNotOnTheLine_IsNotTaken()
    {
        var runs = ReplHighlighter.Runs("x", [new(0, 0, 1, Type("variable"), 0), new(0, 5, 4, Type("keyword"), 0)]);

        Assert.AreEqual("x", string.Concat(runs.Select(run => run.Text)));
    }

    // ---- the console ----

    [TestMethod]
    public void AStyledLine_ReadsAsTheTextItIs_WhateverIsInIt()
    {
        var console = new TestConsole();
        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(AppTheme.Neutral);

        new ReplConsole(Substitute.For<IConsoleMessageWriter>(), console, themes).WriteLine(
            [new ReplTextRun("Print ", ReplTextStyle.Keyword), new ReplTextRun("\"[x]\"", ReplTextStyle.String)]);

        StringAssert.Contains(console.Output, "Print \"[x]\"");
    }

    [TestMethod]
    public void AKeyword_IsInTheColourTheThemeGivesIt()
    {
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.Yes, ColorSystem = ColorSystemSupport.TrueColor, Out = new AnsiConsoleOutput(output) });
        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(new AppTheme(new AppThemeLoaderService(new MockFileSystem()).LoadBuiltInDefault()));

        new ReplConsole(Substitute.For<IConsoleMessageWriter>(), console, themes).WriteLine([new ReplTextRun("Dim", ReplTextStyle.Keyword)]);

        StringAssert.Contains(output.ToString(), "38;2;62;97;255");
    }

    // ---- LIST ----

    private readonly ReplProgram _program = new();
    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();

    private async Task<ReplCommandContext> ContextWithListingAsync()
    {
        var workspace = await ReplWorkspace.CreateAsync(new MockFileSystem(), Substitute.For<IProjectFileWriter>());
        return new ReplCommandContext(_program, _console, _platform, new ReplDocument(_program, _platform, workspace), []);
    }

    [TestMethod]
    public async Task AListing_IsHighlightedByWhatTheLanguageServerSays_AndTheProgramIsADocumentOfIt()
    {
        _program.Store(10, "Dim x");
        _platform.GetSemanticTokensAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ReplSemanticToken>>([new(0, 0, 2, Type("number"), 0), new(0, 3, 3, Type("keyword"), 0)]));
        var context = await ContextWithListingAsync();

        await new ListReplCommand().ExecuteAsync(context, "", CancellationToken.None);

        await _platform.Received(1).OpenDocumentAsync(Arg.Any<Uri>(), "10 Dim x\r\n", Arg.Any<int>(), Arg.Any<CancellationToken>());
        _console.Received(1).WriteLine(Arg.Is<IReadOnlyList<ReplTextRun>>(runs => runs.Any(run => run.Style == ReplTextStyle.Keyword && run.Text == "Dim")));
    }

    [TestMethod]
    public async Task ALineThatIsListedAfterAnother_IsHighlightedByTheTokensOfItsOwnLine_AndAlignedToTheNumbers()
    {
        _program.Store(5, "x = 1");
        _program.Store(100, "Dim y");
        _platform.GetSemanticTokensAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ReplSemanticToken>>([new(1, 4, 3, Type("keyword"), 0)]));
        var context = await ContextWithListingAsync();

        await new ListReplCommand().ExecuteAsync(context, "100", CancellationToken.None);

        _console.Received(1).WriteLine(Arg.Is<IReadOnlyList<ReplTextRun>>(runs =>
            string.Concat(runs.Select(run => run.Text)) == "100 Dim y" && runs.Any(run => run.Style == ReplTextStyle.Keyword && run.Text == "Dim")));
    }

    [TestMethod]
    public async Task AListingTheLanguageServerCannotHighlight_IsAListingAllTheSame()
    {
        _program.Store(10, "Dim x");
        _platform.GetSemanticTokensAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("the server is gone"));
        var context = await ContextWithListingAsync();

        await new ListReplCommand().ExecuteAsync(context, "", CancellationToken.None);

        _console.Received(1).WriteLine("10 Dim x");
    }

    [TestMethod]
    public async Task AListingOfAShellWithNoWorkspaceOfItsOwn_IsPlain()
    {
        _program.Store(10, "Dim x");
        var context = new ReplCommandContext(_program, _console, _platform, new ReplDocument(_program, _platform), []);

        await new ListReplCommand().ExecuteAsync(context, "", CancellationToken.None);

        _console.Received(1).WriteLine("10 Dim x");
        await _platform.DidNotReceiveWithAnyArgs().GetSemanticTokensAsync(default!, default);
    }
}
