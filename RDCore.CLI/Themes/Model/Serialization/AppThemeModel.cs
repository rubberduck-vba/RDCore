using RDCore.CLI.App.Messages.Model;
using System.Globalization;
using System.Text.Json.Serialization;

namespace RDCore.CLI.Themes.Model.Serialization;

/* TODO nuke this with Spectre (Nuget) */

public record class AppThemeModel
{
    private static readonly Lazy<AppThemeModel> _default = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static AppThemeModel Default => _default.Value;

    [JsonIgnore]
    public string Source { get; init; } = "rdc";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "rd-default (fallback)";
    [JsonPropertyName("author")]
    public string Author { get; init; } = "9562-7303 Québec inc.";
    [JsonPropertyName("version")]
    public string Version { get; init; } = "0.1";
    [JsonPropertyName("config")]
    public AppThemeConfig Config { get; init; } = new();
}

public interface IThemeColorParser
{
    int ParseThemeColor(string value);
}
public record class AppTheme : IThemeColorParser
{
    public AppTheme(AppThemeModel theme)
    {
        Source = theme.Source;
        Name = theme.Name;
        Author = theme.Author;
        Version = theme.Version;

        Config = new AppThemeConfigModel(theme.Config, this);
    }

    public string Source { get; init; }
    public string Name { get; init; } 
    public string Author { get; init; }
    public string Version { get; init; }

    [JsonPropertyName("config")]
    public AppThemeConfigModel Config { get; init; }
    

    private Dictionary<string, int> Names => new()
    {
        ["bg-default"] = Config.Shell.BackgroundDefault,
        ["fg-default"] = Config.Shell.ForegroundDefault,
        ["bg-highlight"] = Config.Shell.BackgroundHighlight,
        ["fg-highlight"] = Config.Shell.ForegroundHighlight,
        ["bg-dbg-breakpoint"] = Config.Shell.BackgroundDebugBreakpoint,
        ["fg-dbg-breakpoint"] = Config.Shell.ForegroundDebugBreakpoint,
        ["bg-dbg-current"] = Config.Shell.BackgroundDebugCurrent,
        ["fg-dbg-current"] = Config.Shell.ForegroundDebugCurrent,

    };

    public int ParseThemeColor(string value)
    {
        if (value.StartsWith("${"))
        {
            var name = value[2..^1];
            if (Names.TryGetValue(name, out var lookup))
            {
                return lookup;
            }
            else
            {
                throw new FormatException("Invalid ${name}");
            }
        }
        else if (int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int result))
        {
            return result;
        }
        else if (Enum.TryParse<ConsoleColor>(value, out var color))
        {
            return (int)color;
        }

        throw new FormatException("Invalid value");
    }

    public int GetMessagePartColor(MessageKind kind, MessagePart part)
    {
        var config = kind switch 
        { 
            MessageKind.Trace => Config.Shell.Trace,
            MessageKind.Information => Config.Shell.Information,
            MessageKind.Warning => Config.Shell.Warning,
            MessageKind.Error => Config.Shell.Error,
            MessageKind.Success => Config.Shell.Success,

            _ => Config.Shell.Trace
        };
        return part switch
        {
            MessagePart.Timestamp => config.ThemeLight,
            MessagePart.Title => config.ThemeAccent1,
            MessagePart.Body => config.ThemeAccent1,
            MessagePart.Verbose => config.ThemeDark,
            MessagePart.StackTrace => config.ThemeDark,
            MessagePart.Metric => config.ThemeAccent2,

            _ => config.ThemeAccent1
        };
    }

    public string GetMessageIcon(MessageKind kind)
    {
        var config = kind switch
        {
            MessageKind.Trace => Config.Shell.Trace,
            MessageKind.Information => Config.Shell.Information,
            MessageKind.Warning => Config.Shell.Warning,
            MessageKind.Error => Config.Shell.Error,
            MessageKind.Success => Config.Shell.Success,

            _ => Config.Shell.Trace
        };
        return config.Icon;
    }
}