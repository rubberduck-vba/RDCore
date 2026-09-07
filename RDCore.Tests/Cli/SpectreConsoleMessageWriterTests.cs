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
