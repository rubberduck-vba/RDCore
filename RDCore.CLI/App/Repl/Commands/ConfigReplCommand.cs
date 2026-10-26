using RDCore.CLI.App.Configuration;
using RDCore.CLI.Themes;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Server.Configuration;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>CONFIG</c> (<c>CONFIGURE</c>) shows the settings of the platform, and sets them for the user (<see cref="ConfigCommand"/>).
/// </summary>
/// <remarks>
/// <para>
/// Its arguments are completed a part at a time - the sections, a setting of one, then a value of it - as its descriptor says. Setting the console's theme
/// (<c>CONFIG CLI.Theme</c>) is the theme it starts with, and does not change the one it has: that is <c>THEME</c>'s.
/// </para>
/// <para>
/// A setting that is set or reset takes effect in the shell's own settings, and the language server is told the live settings changed: it asks the shell for
/// them, and passes them on to the components it is the client of (<see cref="ILiveSettings"/>).
/// </para>
/// </remarks>
/// <param name="Config">The verb.</param>
/// <param name="Themes">The themes of the console, which its theme can be.</param>
/// <param name="LiveSettings">The shell's own live settings.</param>
internal sealed record class ConfigReplCommand(ConfigCommand Config, IAppThemeService Themes, ILiveSettings LiveSettings) : IReplCommand, IReplCommandDescriptor
{
    public string Name => ConfigCommand.Name;

    public IReadOnlyList<string> Aliases => [ConfigCommand.Alias];

    public string Summary => Resources.Config_Summary;

    public VerbDescriptor Descriptor => Config.Describe(Themes.ThemeNames);

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        var outputs = Config.Run(arguments);
        foreach (var output in outputs)
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

        // a setting was set or reset: the shell's own live settings are what the user's settings say now, and the platform is told.
        if (outputs.Any(output => output.Kind == MessageKind.Success))
        {
            LiveSettings.Refresh();
            await context.Platform.NotifyLiveSettingsChangedAsync(token);
        }

        return ReplCommandResult.Continue;
    }
}
