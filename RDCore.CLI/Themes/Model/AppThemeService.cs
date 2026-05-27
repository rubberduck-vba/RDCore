using Microsoft.Extensions.Options;
using RDCore.CLI.App.Messages.Model;
using RDCore.CLI.Themes.Model.Serialization;
using System.Collections.Immutable;

namespace RDCore.CLI.Themes.Model;

public interface IAppThemeService
{
    /// <summary>
    /// Gets all available themes.
    /// </summary>
    ImmutableArray<AppTheme> Themes { get; }

    /// <summary>
    /// Gets the current theme.
    /// </summary>
    AppTheme Theme { get; }

    /// <summary>
    /// Sets the current theme.
    /// </summary>
    /// <param name="name">The name of the theme to apply.</param>
    void SetTheme(string name);
}

public class AppThemeService(IOptions<AppOptions> options, IAppThemeLoaderService loader) : IAppThemeService
{
    private Dictionary<string, AppTheme> _themes = [];
    public ImmutableArray<AppTheme> Themes => [.. _themes.Values];
    private string _selection = "rdc-default";

    public AppTheme Theme => _themes.TryGetValue(_selection, out var value) ? value : new(AppThemeModel.Default);

    public void SetTheme(string name)
    {
        if (IsThemingEnabled(MessageKind.Warning))
        {
            if (_themes.TryGetValue(name, out var theme))
            {

            }
            //else
            //{
            //    writer.WriteMessage(new ConsoleMessageBuilder()
            //        .WithKind(MessageKind.Error)
            //        .WithTimestamp(DateTimeOffset.UtcNow)
            //        .WithTitle(Resources.Error_ThemeNotFound)
            //        .WithVerbose($"👉 {name}"));
            //}
        }
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        if (IsThemingEnabled(MessageKind.Information))
        {
            _themes = (await loader.DiscoverThemesAsync(token)).ToDictionary(theme => theme.Name, theme => new AppTheme(theme));
        }
    }

    private bool IsThemingEnabled(MessageKind kind)
    {
        if (!options.Value.ThemesEnabled)
        {
            //writer.WriteMessage(new ConsoleMessageBuilder()
            //    .WithKind(kind)
            //    .WithTimestamp(DateTimeOffset.UtcNow)
            //    .WithTitle(Resources.Warn_ThemingDisabled)
            //    .WithVerbose(Resources.Warn_ThemingDisabled_Verbose));
            return false;
        }
        return true;
    }
}
