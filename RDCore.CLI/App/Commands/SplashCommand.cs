using System.Globalization;
using RDCore.SDK.Platform;

namespace RDCore.CLI.App.Commands;

/// <summary>
/// <c>rdc splash</c>: writes the platform's splash (<see cref="PlatformSplash"/>) as plain text, with no style, for what shows the platform without a shell
/// of its own: the publish script, a log.
/// </summary>
internal sealed class SplashCommand(TextWriter? output = null) : ICliCommand
{
    private TextWriter Output => output ?? global::System.Console.Out;

    public string Name => CommandNames.SplashCommand.Name;
    public IReadOnlyList<string> Aliases => [CommandNames.SplashCommand.Alias];
    public string Summary => Resources.Command_ShowSplash;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        foreach (var line in PlatformSplash.Lines(CultureInfo.CurrentUICulture))
        {
            await Output.WriteLineAsync(line.Text);
        }

        return 0;
    }
}
