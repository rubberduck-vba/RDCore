using NSubstitute;
using RDCore.CLI;
using RDCore.CLI.App.Repl;
using RDCore.CLI.ConsoleIO;
using RDCore.SDK.Platform;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The shell's splash is the platform's, with the shell's title art between the logo and the slogan (<see cref="ReplSplash"/>).
/// </summary>
[TestClass]
public sealed class ReplSplashTests
{
    [TestMethod]
    public void TheShellsSplash_IsThePlatformsLogo_ThenTheShellsTitle_ThenThePlatformsSloganAndNotice()
    {
        var console = Substitute.For<IReplConsole>();
        var writer = Substitute.For<IConsoleMessageWriter>();
        var shown = new List<string>();
        console.When(it => it.WriteLine(Arg.Any<IReadOnlyList<ReplTextRun>>()))
            .Do(call => shown.AddRange(call.Arg<IReadOnlyList<ReplTextRun>>().Select(run => $"{run.Style}:{run.Text}")));
        writer.When(it => it.WriteSlogan(Arg.Any<int>())).Do(call => shown.Add($"slogan:{call.Arg<int>()}"));
        writer.When(it => it.WriteLegalNotice()).Do(_ => shown.Add("notice"));

        new ReplSplash(writer).Write(console);

        var logo = PlatformSplash.Logo.Split('\n').Select(line => line.TrimEnd()).Select(line => line.Length == 0 ? line : new string(' ', 15) + line);
        var title = Resources.RDCoreSplash_Foreground.Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0);
        string[] expected =
        [
            .. logo.Select(line => $"{ReplTextStyle.SplashLogo}:{line}"),
            .. title.Select(line => $"{ReplTextStyle.SplashTitle}:{line}"),
            "slogan:36",
            "notice",
        ];
        CollectionAssert.AreEqual(
            expected,
            shown,
            "the logo over the shell's title, then the slogan centred under the title, then the notice");
        console.Received(2).WriteLine(Arg.Any<string>());
    }
}
