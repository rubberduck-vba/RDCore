using NSubstitute;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Themes;
using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using Spectre.Console;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// <c>ANALYZE</c> writes a finding on two lines: its glyph, where it is, its code and the page that explains it, in the title style of its severity; and what it
/// says under it, in the verbose style.
/// </summary>
[TestClass]
public sealed class ReplAnalyzeTests
{
    private readonly ReplProgram _program = new();
    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();

    private async Task AnalyzeAsync(params SessionDiagnostic[] findings)
    {
        _program.Store(10, "x = 1");
        _program.Store(20, "y = ");
        _platform.Provides<SessionAnalyze>().Returns(true);
        _platform.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AnalyzeSessionResult { Diagnostics = findings, Providers = 3 });
        var workspace = await ReplWorkspace.CreateAsync(new MockFileSystem(), Substitute.For<IProjectFileWriter>());
        var context = new ReplCommandContext(_program, _console, _platform, new ReplDocument(_program, _platform, workspace), []);

        await new AnalyzeReplCommand().ExecuteAsync(context, string.Empty, CancellationToken.None);
    }

    [TestMethod]
    public async Task AFinding_IsItsPlaceAndCode_AndThePageThatExplainsIt_AndWhatItSaysApart()
    {
        await AnalyzeAsync(new SessionDiagnostic { Code = "VBC09302", Severity = SessionDiagnosticSeverity.Error, Message = "Variable not defined", Line = 3, Column = 1 });

        _console.Received(1).WriteFinding(MessageKind.Error, "20:1 VBC09302",
            new Uri("https://rubberduck-vba.github.io/RDCore/diagnostics/vbc09302.html"), "Variable not defined");
    }

    [TestMethod]
    public async Task AFindingWithNoCode_HasNoPage_AndTheKindOfItsSeverity()
    {
        await AnalyzeAsync(new SessionDiagnostic { Severity = SessionDiagnosticSeverity.Warning, Message = "Check this", Line = 2, Column = 3 });

        _console.Received(1).WriteFinding(MessageKind.Warning, "10:3", null, "Check this");
    }

    // ---- how it is written ----

    private static string Render(Action<ReplConsole> write, bool links)
    {
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.Yes, ColorSystem = ColorSystemSupport.TrueColor, Out = new AnsiConsoleOutput(output) });
        console.Profile.Capabilities.Links = links;
        console.Profile.Width = 200;
        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(new AppTheme(new AppThemeLoaderService(new MockFileSystem()).LoadBuiltInDefault()));
        write(new ReplConsole(Substitute.For<IConsoleMessageWriter>(), console, themes));
        return output.ToString();
    }

    [TestMethod]
    public void TheTwoLines_AreTheTitleAndTheDescription_TheDescriptionIndentedUnderIt()
    {
        var output = Render(console => console.WriteFinding(MessageKind.Error, "20:1 VBC09302",
            new Uri("https://rubberduck-vba.github.io/RDCore/diagnostics/vbc09302.html"), "Variable not defined"), links: false);

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(2, lines);
        StringAssert.Contains(lines[0], "20:1 VBC09302");
        StringAssert.Contains(lines[0], "https://rubberduck-vba.github.io/RDCore/diagnostics/vbc09302.html");
        StringAssert.Contains(lines[1], "  ");
        StringAssert.Contains(lines[1], "Variable not defined");
    }

    [TestMethod]
    public void ThePage_IsALink_WhereTheConsoleDrawsLinks_AndOnlyItsAddressWhereItDoesNot()
    {
        var url = new Uri("https://rubberduck-vba.github.io/RDCore/diagnostics/vbc09302.html");

        var linked = Render(console => console.WriteFinding(MessageKind.Error, "20:1 VBC09302", url, "x"), links: true);
        var plain = Render(console => console.WriteFinding(MessageKind.Error, "20:1 VBC09302", url, "x"), links: false);

        StringAssert.Contains(linked, "\u001b]8;");
        Assert.DoesNotContain("\u001b]8;", plain);
        StringAssert.Contains(plain, url.AbsoluteUri);
    }
}
