using System.Globalization;
using RDCore.CLI.ConsoleIO;
using RDCore.SDK.Platform;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The shell's splash: the platform's (<see cref="PlatformSplash"/>), with the shell's own title art between the logo and the slogan, each part in the
/// theme's style. It is the one splash the shell shows as it starts, and the one its <c>SPLASH</c> command shows again.
/// </summary>
internal sealed class ReplSplash(IConsoleMessageWriter writer)
{
    // the title art goes under the logo with the logo this far in, as the two are drawn to sit together.
    private const int LogoIndent = 15;

    public void Write(IReplConsole console)
    {
        var title = new SplashTitle(Resources.RDCoreSplash_Foreground.Split('\n'), LogoIndent);
        foreach (var line in PlatformSplash.Lines(CultureInfo.CurrentUICulture, title))
        {
            switch (line.Part)
            {
                case SplashPart.Logo:
                    console.WriteLine([new ReplTextRun(line.Text, ReplTextStyle.SplashLogo)]);
                    break;
                case SplashPart.Title:
                    console.WriteLine([new ReplTextRun(line.Text, ReplTextStyle.SplashTitle)]);
                    break;
                case SplashPart.Slogan:
                    writer.WriteSlogan(line.Text.Length - line.Text.TrimStart().Length);
                    break;
                case SplashPart.CopyrightNotice:
                    writer.WriteLegalNotice();
                    break;
                default:
                    console.WriteLine();
                    break;
            }
        }
    }
}
