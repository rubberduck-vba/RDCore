using RDCore.CLI.App.Messages.Model;
using RDCore.CLI.Themes.Model;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace RDCore.CLI.App.Messages;

public interface IConsoleMessageWriter
{
    IConsoleMessageWriter Clear();
    IConsoleMessageWriter WriteMessage(ConsoleMessageBuilder builder, ConsoleColor? color = default);
    IConsoleMessageWriter WriteException(Exception exception);
    IConsoleMessageWriter WriteAssemblyInfo();
    IConsoleMessageWriter WriteSlogan();
    IConsoleMessageWriter WriteLegalNotice();
}

/// <summary>
/// 
/// </summary>
//internal class ThemeEnabledConsoleMessageWriter : IConsoleMessageWriter
//{

//}


/// <summary>
/// A default <c>ConsoleMessageWriter</c> implementation with a whopping 16 colors to play with.
/// </summary>
public class DefaultConsoleMessageWriter : IConsoleMessageWriter
{
    private IAppThemeService AppThemeService { get; }

    public DefaultConsoleMessageWriter(IAppThemeService appThemeService)
    {
        AppThemeService = appThemeService;
        Console.OutputEncoding = Encoding.Unicode;
        Console.BackgroundColor = (ConsoleColor)appThemeService.Theme.Config.Shell.BackgroundDefault;
        Console.ForegroundColor = (ConsoleColor)appThemeService.Theme.Config.Shell.ForegroundDefault;
    }

    public IConsoleMessageWriter Clear()
    {
        Console.Clear();
        return this;
    }

    public IConsoleMessageWriter WriteAssemblyInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyName = assembly.GetName();
        var company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company;

        return WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Trace)
            .WithMessageBody($"{assemblyName.Name} [v{assemblyName.Version?.ToString(3) ?? "0.1a"}]")
            .WithPlaceholder("COMPANY", company));
    }

    public IConsoleMessageWriter WriteLegalNotice()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyName = assembly.GetName();
        var company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company;

        return WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Trace)
            .WithMessageBody(Resources.CopyrightNotice.Replace("{$YEAR}", DateTimeOffset.UtcNow.Year.ToString())) // manually replaced to avoid highlighting
            .WithPlaceholder("COMPANY", company)
            .WithLineBreak());
    }

    public IConsoleMessageWriter WriteSlogan() => WriteMessage(
        new ConsoleMessageBuilder()
            .WithKind(MessageKind.Information)
            .WithMessageBody(new string(' ', 38) + Resources.RDCore_Slogan, nameof(ConsoleColor.DarkRed))
            .WithPlaceholder("VIVAT", "V I V A T")
            .WithPlaceholder("CUCUMIS", "C U C U M I S")
            .WithLineBreak());

    public string ToFormattedString(ConsoleMessagePart part) => new StringBuilder()
        .AppendLine(part.Value)
        .ToString();

    public IConsoleMessageWriter WriteException(Exception exception) =>
        WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Error)
            .WithTitle(exception)
            .WithMessageBody(exception)
            .WithStackTrace(exception));

    public IConsoleMessageWriter WriteMessage(ConsoleMessageBuilder builder, ConsoleColor? color = default)
    {
        var theme = AppThemeService.Theme;

        var bodyParts = builder.Parts.OfType<ConsoleMessageBodyPart>().ToArray();
        var body = bodyParts[0];
        //var overlay = builder.Parts.OfType<ConsoleMessageOverlayMessagePart>().ToArray();

        var messageBody = builder.Parts
            .OfType<ConsoleMessageStringLiteralPlaceholderPart>()
            .Aggregate(body.Body, (result, metric) =>
                result.Replace(metric.Placeholder, PlaceholderPartFormatter.FormatValue(metric.Kind, metric.StringValue)));

        WriteMessagePart(builder.Parts.OfType<ConsoleMessageTimestampPart>().SingleOrDefault(), (ConsoleColor)theme.GetMessagePartColor(builder.Kind, MessagePart.Timestamp));
        WriteMessageIcon(builder.Kind, color ?? (ConsoleColor)theme.GetMessagePartColor(builder.Kind, MessagePart.Title));
        WriteMessagePart(builder.Parts.OfType<ConsoleMessageTitlePart>().SingleOrDefault(), (ConsoleColor)theme.GetMessagePartColor(builder.Kind, MessagePart.Title));
        WriteNewLine();
        var metrics = builder.Parts.OfType<ConsoleMessageStringLiteralPlaceholderPart>();

        //var (Left, Top) = Console.GetCursorPosition();
        //var lines = body.Body.Split("\n").Length;
        WriteMessageBody(body, metrics, builder.Kind, color ?? Enum.Parse<ConsoleColor>(body.ColorOverride ?? ((ConsoleColor)theme.GetMessagePartColor(builder.Kind, body.Part)).ToString()));

        //foreach (var layer in overlay)
        //{
        //    Console.SetCursorPosition(Left, Top);
        //    WriteMessageBody(builder.Parts.OfType<ConsoleMessageBodyPart>().SingleOrDefault(), metrics, builder.Kind, (ConsoleColor)ConsoleMessagePart.ParseConfigColor(layer.Color));

        //}
        //WriteNewLine();
        WriteMessagePart(builder.Parts.OfType<ConsoleMessageVerbosePart>().SingleOrDefault(), (ConsoleColor)theme.GetMessagePartColor(builder.Kind, MessagePart.Verbose));
        WriteMessagePart(builder.Parts.OfType<ConsoleMessageStackTracePart>().SingleOrDefault(), (ConsoleColor)theme.GetMessagePartColor(builder.Kind, MessagePart.StackTrace));

        if (builder.IsWithLineBreak)
        {
            WriteNewLine();
        }

        return this;
    }

    private void WriteMessageIcon(MessageKind kind, ConsoleColor color)
    {
        var icon = AppThemeService.Theme.GetMessageIcon(kind);
        if (icon is not null)
        {
            var revertColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write($"{icon} ");
            Console.ForegroundColor = revertColor;
        }
    }

    private static void WriteNewLine() => Console.Write(Environment.NewLine);

    private void WriteMessageBody(ConsoleMessageBodyPart? part, IEnumerable<ConsoleMessageStringLiteralPlaceholderPart> metrics, MessageKind kind, ConsoleColor? color = null)
    {
        if (part is null)
        {
            return;
        }

        var revertColor = Console.ForegroundColor;
        if (color != null)
        {
            Console.ForegroundColor = (ConsoleColor)color;
        }

        if (metrics.Any(metric => metric.Kind == PlaceholderKind.StopwatchMilliseconds))
        {
            Console.Write("  >> ⏱️");
        }

        var body = part.Body;
        var theme = AppThemeService.Theme;

        var previous = body;
        while (body.Contains("{$"))
        {
            previous = body;
            foreach (var value in metrics.Cast<ConsoleMessageStringLiteralPlaceholderPart>().Select(e => (Metric: e, Index: body.IndexOf(e.Placeholder))).OrderBy(e => e.Index))
            {
                if (value.Index >= 0)
                {
                    WriteMessagePart(body[..value.Index], color ?? (ConsoleColor)theme.GetMessagePartColor(kind, MessagePart.Body));
                    WriteMessagePart(value.Metric.Value, (ConsoleColor)theme.GetMessagePartColor(kind, MessagePart.Metric));
                    body = body[(value.Index + value.Metric.Placeholder.Length)..];
                    break;
                }
            }
            Debug.Assert(body != previous);
        }
        WriteMessagePart(body, color ?? (ConsoleColor)theme.GetMessagePartColor(kind, MessagePart.Body));
        Console.ForegroundColor = revertColor;
    }

    private static void WriteMessagePart(ConsoleMessagePart? part, ConsoleColor color)
    {
        if (part is null)
        {
            return;
        }
        var revertColor = Console.ForegroundColor;
        Console.ForegroundColor = color;

        Console.Write(part.Value + "\t");
        Console.ForegroundColor = revertColor;
    }
    private static void WriteMessagePart(string part, ConsoleColor color)
    {
        if (part is null)
        {
            return;
        }
        var revertColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(part);
        Console.ForegroundColor = revertColor;
    }
}
