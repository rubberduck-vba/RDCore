using RDCore.CLI.App.Configuration;
using RDCore.CLI.Themes;
using RDCore.SDK.Platform.Commands;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>CONFIG</c> (<c>CONFIGURE</c>) shows the settings of the platform, and sets them for the user (<see cref="ConfigCommand"/>).
/// </summary>
/// <remarks>
/// Its arguments are completed a part at a time - the sections, a setting of one, then a value of it - as its descriptor says. Setting the console's theme
/// (<c>CONFIG CLI.Theme</c>) is the theme it starts with, and does not change the one it has: that is <c>THEME</c>'s.
/// </remarks>
/// <param name="Config">The verb.</param>
/// <param name="Themes">The themes of the console, which its theme can be.</param>
internal sealed record class ConfigReplCommand(ConfigCommand Config, IAppThemeService Themes) : IReplCommand, IReplCommandDescriptor
{
    public string Name => ConfigCommand.Name;

    public IReadOnlyList<string> Aliases => [ConfigCommand.Alias];

    public string Summary => Resources.Config_Summary;

    public VerbDescriptor Descriptor => Config.Describe(Themes.ThemeNames);

    public Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        foreach (var output in Config.Run(arguments))
        {
            if (output.Kind is { } kind)
            {
                context.Console.WriteMessage(kind, output.Text, output.Detail);
            }
            else
            {
                context.Console.WriteLine(output.Text);
            }
        }

        return Task.FromResult(ReplCommandResult.Continue);
    }
}
