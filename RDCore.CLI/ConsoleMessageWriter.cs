using RDCore.CLI.Themes.Model;
using System.Text;

namespace RDCore.CLI;

internal class ConsoleMessageWriter
{
    private IAppThemeService AppThemeService { get; }

    public ConsoleMessageWriter(IAppThemeService appThemeService)
    {
        AppThemeService = appThemeService;
        Console.OutputEncoding = Encoding.Unicode;
        Console.BackgroundColor = (ConsoleColor)appThemeService.Theme.Config.Shell.BackgroundDefault;
        Console.ForegroundColor = (ConsoleColor)appThemeService.Theme.Config.Shell.ForegroundDefault;
    }

    public void Clear() => Console.Clear();

    public void WriteMessage(ConsoleMessageBuilder builder, ConsoleColor? color = default)
    {
        var theme = AppThemeService.Theme;

        var bodyParts = builder.Parts.OfType<ConsoleMessageBodyPart>().ToArray();
        var body = bodyParts[0];
        var overlay = bodyParts.Length > 1 ? bodyParts[1] : null;

        var messageBody = builder.Parts
            .OfType<ConsoleMessageMetricPart>()
            .Aggregate(body.Body, (result, metric) => 
                result.Replace(metric.Placeholder, MetricPartFormatter.FormatValue(metric.Kind, metric.NumericValue)));

        WriteMessagePart(builder.Parts.OfType<ConsoleMessageTimestampPart>().SingleOrDefault(), (ConsoleColor)theme.GetMessagePartColor(builder.Kind, MessagePart.Timestamp));
        WriteMessageIcon(builder.Kind, color ?? (ConsoleColor)theme.GetMessagePartColor(builder.Kind, MessagePart.Title));
        WriteMessagePart(builder.Parts.OfType<ConsoleMessageTitlePart>().SingleOrDefault(), (ConsoleColor)theme.GetMessagePartColor(builder.Kind, MessagePart.Title));
        WriteNewLine();
        var metrics = builder.Parts.OfType<ConsoleMessageMetricPart>();
        var pos = Console.GetCursorPosition();
        WriteMessageBody(body, metrics, builder.Kind, color);
        if (overlay is not null)
        {
            Console.SetCursorPosition(pos.Left, pos.Top);

            WriteMessageBody(builder.Parts.OfType<ConsoleMessageBodyPart>().SingleOrDefault(), metrics, builder.Kind, color);

        }
        WriteNewLine();
        WriteMessagePart(builder.Parts.OfType<ConsoleMessageVerbosePart>().SingleOrDefault(), (ConsoleColor)theme.GetMessagePartColor(builder.Kind, MessagePart.Verbose));
        WriteMessagePart(builder.Parts.OfType<ConsoleMessageStackTracePart>().SingleOrDefault(), (ConsoleColor)theme.GetMessagePartColor(builder.Kind, MessagePart.StackTrace));
    }

    private void WriteMessageIcon(MessageKind kind, ConsoleColor color)
    {
        var icon = AppThemeService.Theme.Config.Shell;
        if (icon is not null)
        {
            var revertColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(icon);
            Console.ForegroundColor = revertColor;
        }
    }

    private static void WriteNewLine() => Console.Write(Environment.NewLine);
    public static void WriteReadyPrompt() => Console.WriteLine($"✅ {Resources.Prompt_READY}");

    private void WriteMessageBody(ConsoleMessageBodyPart? part, IEnumerable<ConsoleMessageMetricPart> metrics, MessageKind kind, ConsoleColor? color = null)
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

        if (metrics.Any(metric => metric.Kind == MetricKind.StopwatchMilliseconds))
        {
            Console.Write("  >> ⏱️");
        }

        var body = part.Body;
        var theme = AppThemeService.Theme;

        while (body.Contains("{$"))
        {
            if (metrics.Select(e => (Metric: e, Index: body.IndexOf(e.Placeholder))).OrderBy(e => e.Index).FirstOrDefault() is (ConsoleMessageMetricPart, int) value)
            {
                WriteMessagePart(body[..value.Index], color ?? (ConsoleColor)theme.GetMessagePartColor(kind, MessagePart.Body));
                WriteMessagePart(value.Metric.Value, (ConsoleColor)theme.GetMessagePartColor(kind, MessagePart.Metric));
                body = body[(value.Index + value.Metric.Placeholder.Length)..];
            }
            else
            {
                break;
            }
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
