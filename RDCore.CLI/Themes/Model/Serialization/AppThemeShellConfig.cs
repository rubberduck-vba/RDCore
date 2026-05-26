using RDCore.CLI.App.Messages.Model;
using System.Text.Json.Serialization;

namespace RDCore.CLI.Themes.Model.Serialization;

internal record class AppThemeShellConfig
{
    [JsonPropertyName("bg-default")]
    public string BackgroundDefault { get; init; } = ConsoleColor.Black.ToString();
    [JsonPropertyName("fg-default")]
    public string ForegroundDefault { get; init; } = ConsoleColor.Gray.ToString();

    [JsonPropertyName("bg-highlight")]
    public string BackgroundHighlight { get; init; } = ConsoleColor.Blue.ToString();
    [JsonPropertyName("fg-highlight")]
    public string ForegroundHighlight { get; init; } = ConsoleColor.White.ToString();
    [JsonPropertyName("bg-dbg-breakpoint")]
    public string BackgroundDebugBreakpoint { get; init; } = ConsoleColor.DarkRed.ToString();
    [JsonPropertyName("fg-dbg-breakpoint")]
    public string ForegroundDebugBreakpoint { get; init; } = ConsoleColor.White.ToString();
    [JsonPropertyName("bg-dbg-current")]
    public string BackgroundDebugCurrent { get; init; } = ConsoleColor.Yellow.ToString();
    [JsonPropertyName("fg-dbg-current")]
    public string ForegroundDebugCurrent { get; init; } = ConsoleColor.Black.ToString();

    [JsonPropertyName("error")]
    public AppThemeMessageTypeConfig Error { get; init; } = new();
    [JsonPropertyName("success")]
    public AppThemeMessageTypeConfig Success { get; init; } = new();
    [JsonPropertyName("information")]
    public AppThemeMessageTypeConfig Information { get; init; } = new();
    [JsonPropertyName("warning")]
    public AppThemeMessageTypeConfig Warning { get; init; } = new();
    [JsonPropertyName("trace")]
    public AppThemeMessageTypeConfig Trace { get; init; } = new();
}

internal record class AppThemeShellConfigModel
{
    public AppThemeShellConfigModel(AppThemeShellConfig shell, IThemeColorParser parser)
    {
        BackgroundDefault = ConsoleMessagePart.ParseConfigColor(shell.BackgroundDefault, fallback: ConsoleColor.Black); 
        ForegroundDefault = ConsoleMessagePart.ParseConfigColor(shell.ForegroundDefault, fallback: ConsoleColor.Gray);
        BackgroundHighlight = ConsoleMessagePart.ParseConfigColor(shell.BackgroundHighlight);
        ForegroundHighlight = ConsoleMessagePart.ParseConfigColor(shell.ForegroundHighlight);
        BackgroundDebugBreakpoint = ConsoleMessagePart.ParseConfigColor(shell.BackgroundDebugBreakpoint);
        ForegroundDebugBreakpoint = ConsoleMessagePart.ParseConfigColor(shell.ForegroundDebugBreakpoint);
        BackgroundDebugCurrent = ConsoleMessagePart.ParseConfigColor(shell.BackgroundDebugCurrent);
        ForegroundDebugCurrent = ConsoleMessagePart.ParseConfigColor(shell.ForegroundDebugCurrent);

        Error = new(shell.Error, parser);
        Success = new(shell.Success, parser);
        Information = new(shell.Information, parser);
        Warning = new(shell.Warning, parser);
        Trace = new(shell.Trace, parser);
    }

    public int BackgroundDefault { get; init; }
    public int ForegroundDefault { get; init; }
    public int BackgroundHighlight { get; init; }
    public int ForegroundHighlight { get; init; }
    public int BackgroundDebugBreakpoint { get; init; }
    public int ForegroundDebugBreakpoint { get; init; }
    public int BackgroundDebugCurrent { get; init; }
    public int ForegroundDebugCurrent { get; init; } 

    public AppThemeMessageTypeConfigModel Error { get; init; }
    public AppThemeMessageTypeConfigModel Success { get; init; }
    public AppThemeMessageTypeConfigModel Information { get; init; }
    public AppThemeMessageTypeConfigModel Warning { get; init; }
    public AppThemeMessageTypeConfigModel Trace { get; init; }
}