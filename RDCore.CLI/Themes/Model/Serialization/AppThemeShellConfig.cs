using System.Globalization;
using System.Text.Json.Serialization;

namespace RDCore.CLI.Themes.Model.Serialization;

internal record class AppThemeShellConfig
{
    [JsonPropertyName("bg-default")]
    public string BackgroundDefault { get; init; } = "0c0a50";
    [JsonPropertyName("fg-default")]
    public string ForegroundDefault { get; init; } = "c0c0c0";

    [JsonPropertyName("bg-highlight")]
    public string BackgroundHighlight { get; init; } = "5f8dd3";
    [JsonPropertyName("fg-highlight")]
    public string ForegroundHighlight { get; init; } = "ffffff";
    [JsonPropertyName("bg-dbg-breakpoint")]
    public string BackgroundDebugBreakpoint { get; init; } = "b51a17";
    [JsonPropertyName("fg-dbg-breakpoint")]
    public string ForegroundDebugBreakpoint { get; init; } = "ffffff";
    [JsonPropertyName("bg-dbg-current")]
    public string BackgroundDebugCurrent { get; init; } = "fdbf00";
    [JsonPropertyName("fg-dbg-current")]
    public string ForegroundDebugCurrent { get; init; } = "000000";

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
        BackgroundDefault = int.Parse(shell.BackgroundDefault, NumberStyles.HexNumber);
        ForegroundDefault = int.Parse(shell.ForegroundDefault, NumberStyles.HexNumber);
        BackgroundHighlight = int.Parse(shell.BackgroundHighlight, NumberStyles.HexNumber); 
        ForegroundHighlight = int.Parse(shell.ForegroundHighlight, NumberStyles.HexNumber);
        BackgroundDebugBreakpoint = int.Parse(shell.BackgroundDebugBreakpoint, NumberStyles.HexNumber);
        ForegroundDebugBreakpoint = int.Parse(shell.ForegroundDebugBreakpoint, NumberStyles.HexNumber);
        BackgroundDebugCurrent = int.Parse(shell.BackgroundDebugCurrent, NumberStyles.HexNumber);
        ForegroundDebugCurrent = int.Parse(shell.ForegroundDebugCurrent, NumberStyles.HexNumber);

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