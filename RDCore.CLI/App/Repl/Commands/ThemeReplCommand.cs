using RDCore.CLI.Themes;

namespace RDCore.CLI.App.Repl.Commands;

internal record class ThemeReplCommand(IAppThemeService Themes) : IReplCommand
{
    public string Name => ReplCommandNames.Theme;

    public IReadOnlyList<string> Aliases => [];

    public string Summary => Resources.Repl_Theme_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        var current = Themes.Theme.Name;
        var themes = Themes.ThemeNames;

        if ("LIST".Equals(arguments, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var theme in themes)
            {
                var marker = current.Equals(theme, StringComparison.OrdinalIgnoreCase) ? "   🎨\t" : "\t";
                context.Console.WriteLine($"{marker} {theme}");
            }
            context.Console.WriteLine();
            return ReplCommandResult.Continue;
        }

        var target = themes.SingleOrDefault(theme => theme.Equals(arguments, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentOutOfRangeException(nameof(arguments), arguments, Resources.Repl_Theme_ErrThemeNotFound);

        Themes.SetTheme(target);
        return ReplCommandResult.Continue;
    }
}
