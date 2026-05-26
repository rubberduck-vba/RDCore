using RDCore.CLI.App.Messages;
using RDCore.CLI.App.Messages.Model;
using RDCore.CLI.Themes.Model;
using System.Reflection;

namespace RDCore.CLI.App.Commands;

internal record class ShowSplashCommand : CLICommand
{
    private readonly IConsoleMessageWriter _writer = default!;
    private readonly IAppThemeService _themes;
    public ShowSplashCommand(IConsoleMessageWriter writer, IAppThemeService themes) : base("slash")
    {
        _writer = writer;
        _themes = themes;
    }

    public override void Execute()
    {
        var assemblyName = Assembly.GetExecutingAssembly().GetName();
        _writer.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Trace)
            .WithTitle($"{assemblyName.Name} [v{assemblyName.Version?.ToString(3) ?? "0.1a"}]")
            .WithMetric(MetricKind.IntegerValue, "{$YEAR}", DateTimeOffset.UtcNow.Year)
            .WithMessageBody(Resources.CopyrightNotice));

        _writer.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Trace)
            .WithMessageBody(Resources.RDCoreSplash_Background)
            .WithMessageOverlay(Resources.RDCoreSplash_Foreground))
            ;
    }
}
