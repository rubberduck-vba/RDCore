using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Console;
using RDCore.CLI.Themes;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>THEME &lt;name&gt;</c> sets the shell's theme; <c>THEME LIST</c> shows the themes there are, with the current one selected, and applies the one chosen.
/// </summary>
/// <remarks>
/// The choice is a <see cref="ConsoleMenu"/>, the same that answers the buttons of a program's <c>MsgBox</c>: the arrow keys move, <kbd>Enter</kbd> applies the theme
/// selected - its first letter applies it at once - and <kbd>Esc</kbd> keeps the current one. A shell whose input is a script has no keys to choose with, and is shown
/// the list, the current theme marked.
/// </remarks>
/// <param name="Themes">The themes, and the current one.</param>
/// <param name="Keys">Where the choice is read.</param>
/// <param name="Output">Where the menu is drawn; the console unless said otherwise.</param>
internal record class ThemeReplCommand(IAppThemeService Themes, IConsoleKeys Keys, TextWriter? Output = null) : IReplCommand, IReplCommandArguments
{
    private const string Indent = "  ";
    private const string ListArgument = "LIST";

    public string Name => ReplCommandNames.Theme;

    public IReadOnlyList<string> Aliases => [];

    public string Summary => Resources.Repl_Theme_Summary;

    // the one argument is LIST or the name of a theme, the current one marked.
    public IReadOnlyList<CompletionItem> CompleteArguments(ReplProgram program, string arguments)
    {
        if (arguments.TrimStart().Contains(' '))
        {
            return [];
        }

        var current = Themes.Theme.Name;
        return
        [
            new CompletionItem { Label = ListArgument, Kind = CompletionItemKind.Keyword, Detail = Resources.Repl_Theme_List_Detail, SortText = $"0{ListArgument}" },
            .. Themes.ThemeNames.Order(StringComparer.OrdinalIgnoreCase).Select(theme => new CompletionItem
            {
                Label = theme,
                Kind = CompletionItemKind.Color,
                Detail = theme.Equals(current, StringComparison.OrdinalIgnoreCase) ? "🎨" : null,
                SortText = $"1{theme.ToLowerInvariant()}",
            }),
        ];
    }

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        var current = Themes.Theme.Name;
        // in a stable order, so that the same keys choose the same theme every time.
        IReadOnlyList<string> themes = [.. Themes.ThemeNames.Order(StringComparer.OrdinalIgnoreCase)];

        if (ListArgument.Equals(arguments, StringComparison.OrdinalIgnoreCase))
        {
            if (Keys.IsInteractive && themes.Count > 0)
            {
                Choose(context, themes, current, token);
            }
            else
            {
                List(context, themes, current);
            }

            return ReplCommandResult.Continue;
        }

        var target = themes.SingleOrDefault(theme => theme.Equals(arguments, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentOutOfRangeException(nameof(arguments), arguments, Resources.Repl_Theme_ErrThemeNotFound);

        Themes.SetTheme(target);
        return ReplCommandResult.Continue;
    }

    private void Choose(ReplCommandContext context, IReadOnlyList<string> themes, string current, CancellationToken token)
    {
        var output = Output ?? System.Console.Out;
        var selected = Math.Max(0, themes.ToList().FindIndex(theme => theme.Equals(current, StringComparison.OrdinalIgnoreCase)));
        var menu = new ConsoleMenu(themes, selected);

        context.Console.WriteLine(Indent + Resources.Repl_Theme_ChooseHint);
        var chosen = menu.Run(Keys, output, Indent, token);
        output.WriteLine();
        context.Console.WriteLine();

        if (chosen is { } index && !themes[index].Equals(current, StringComparison.OrdinalIgnoreCase))
        {
            Themes.SetTheme(themes[index]);
        }
    }

    private static void List(ReplCommandContext context, IReadOnlyList<string> themes, string current)
    {
        foreach (var theme in themes)
        {
            var marker = current.Equals(theme, StringComparison.OrdinalIgnoreCase) ? "   🎨\t" : "\t";
            context.Console.WriteLine($"{marker} {theme}");
        }

        context.Console.WriteLine();
    }
}
