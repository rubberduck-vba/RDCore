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
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyName = assembly.GetName();
        var company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company;
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright;

        _writer.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Trace)
            .WithTitle($"{assemblyName.Name} [v{assemblyName.Version?.ToString(3) ?? "0.1a"}]")
            .WithMetric(MetricKind.StringLiteral, "{$COMPANY}", company)
            .WithMessageBody(Resources.CopyrightNotice.Replace("{$YEAR}", DateTimeOffset.UtcNow.Year.ToString())));

        _writer.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Trace)
            .WithTitle(Environment.NewLine + Resources.RDCoreSplash_Background)
            .WithMessageBody(Resources.RDCoreSplash_Foreground, nameof(ConsoleColor.White)));
    }
}
