using NSubstitute;
using RDCore.CLI.App.Console;
using RDCore.CLI.Themes;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using Spectre.Console.Testing;

namespace RDCore.Tests.Cli;

[TestClass]
public sealed class SpectreConsoleMessageWriterTests
{
    private static (SpectreConsoleMessageWriter Sut, TestConsole Console) NewSut()
    {
        var console = new TestConsole();
        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(AppTheme.Neutral);
        return (new SpectreConsoleMessageWriter(console, themes), console);
    }

    [TestMethod]
    public void RendersTitleBodyAndSubstitutesPlaceholders()
    {
        var (sut, console) = NewSut();

        sut.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Success)
            .WithTitle("Describe Extension")
            .WithMessageBody("wrote {$FILE}")
            .WithPlaceholder("FILE", "extension.manifest.json"));

        var output = console.Output;
        StringAssert.Contains(output, "Describe Extension");
        StringAssert.Contains(output, "wrote extension.manifest.json");
    }

    [TestMethod]
    public void Slogan_ReadsAsItAlwaysHas()
    {
        var (sut, console) = NewSut();

        sut.WriteSlogan();

        StringAssert.Contains(console.Output, "V I V A T  ♥  C U C U M I S ™");
    }

    // the heart is written in red, whatever the theme: its colour is part of what it means.
    [TestMethod]
    public void Slogan_WritesTheHeartInRed()
    {
        var console = new TestConsole().EmitAnsiSequences().Colors(Spectre.Console.ColorSystem.TrueColor);
        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(AppTheme.Neutral);

        new SpectreConsoleMessageWriter(console, themes).WriteSlogan();

        StringAssert.Contains(console.Output, "\u001b[38;2;255;0;0m♥");
    }

    [TestMethod]
    public void BodyLessBuilder_DoesNotThrow()
    {
        var (sut, console) = NewSut();

        sut.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Trace).WithTitle("title only"));

        StringAssert.Contains(console.Output, "title only");
    }

    [TestMethod]
    public void WriteException_ProducesOutput()
    {
        var (sut, console) = NewSut();

        sut.WriteException(new InvalidOperationException("kaboom"));

        StringAssert.Contains(console.Output, "kaboom");
    }
}
