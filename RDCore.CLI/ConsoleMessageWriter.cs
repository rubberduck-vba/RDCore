namespace RDCore.CLI;

internal class ConsoleMessageWriter
{
    private static readonly Lazy<Dictionary<MessageKind, Dictionary<MessagePart,ConsoleColor>>> _themeColors = new(() => new()
    {
        [MessageKind.Success] = new(){
            [MessagePart.Timestamp] = ConsoleColor.White,
            [MessagePart.Title] = ConsoleColor.Green,
            [MessagePart.Body] = ConsoleColor.DarkGreen,
            [MessagePart.Verbose] = ConsoleColor.Gray,
            [MessagePart.Metric] = ConsoleColor.White,
            [MessagePart.StackTrace] = ConsoleColor.DarkGray
        },
        [MessageKind.Error] = new()
        {
            [MessagePart.Timestamp] = ConsoleColor.White,
            [MessagePart.Title] = ConsoleColor.Red,
            [MessagePart.Body] = ConsoleColor.DarkRed,
            [MessagePart.Verbose] = ConsoleColor.Gray,
            [MessagePart.Metric] = ConsoleColor.Red,
            [MessagePart.StackTrace] = ConsoleColor.DarkRed
        },
        [MessageKind.Trace] = new()
        {
            [MessagePart.Timestamp] = ConsoleColor.Gray,
            [MessagePart.Title] = ConsoleColor.Gray,
            [MessagePart.Body] = ConsoleColor.DarkGray,
            [MessagePart.Verbose] = ConsoleColor.Gray,
            [MessagePart.Metric] = ConsoleColor.White,
            [MessagePart.StackTrace] = ConsoleColor.DarkGray
        },
        [MessageKind.Information] = new()
        {
            [MessagePart.Timestamp] = ConsoleColor.Gray,
            [MessagePart.Title] = ConsoleColor.Blue,
            [MessagePart.Body] = ConsoleColor.DarkBlue,
            [MessagePart.Verbose] = ConsoleColor.Gray,
            [MessagePart.Metric] = ConsoleColor.Cyan,
            [MessagePart.StackTrace] = ConsoleColor.DarkGray
        },
        [MessageKind.Warning] = new()
        {
            [MessagePart.Timestamp] = ConsoleColor.Gray,
            [MessagePart.Title] = ConsoleColor.Yellow,
            [MessagePart.Body] = ConsoleColor.DarkYellow,
            [MessagePart.Verbose] = ConsoleColor.Gray,
            [MessagePart.Metric] = ConsoleColor.White,
            [MessagePart.StackTrace] = ConsoleColor.DarkGray
        },

    }, LazyThreadSafetyMode.PublicationOnly);

    public static void WriteMessage(ConsoleMessageBuilder builder, ConsoleColor? color = default)
    {
        var scheme = _themeColors.Value[builder.Kind];
        var body = builder.Parts.OfType<ConsoleMessageBodyPart>().Single();
        var messageBody = builder.Parts
            .OfType<ConsoleMessageMetricPart>()
            .Aggregate(body.Body, (result, metric) => 
                result.Replace(metric.Placeholder, MetricPartFormatter.FormatValue(metric.Kind, metric.NumericValue)));

        WriteMessagePart(builder.Parts.OfType<ConsoleMessageTimestampPart>().SingleOrDefault(), scheme[MessagePart.Timestamp]);
        WriteMessageIcon(builder.Kind, color ?? scheme[MessagePart.Title]);
        WriteMessagePart(builder.Parts.OfType<ConsoleMessageTitlePart>().SingleOrDefault(), scheme[MessagePart.Title]);
        WriteNewLine();
        var metrics = builder.Parts.OfType<ConsoleMessageMetricPart>();
        WriteMessageBody(builder.Parts.OfType<ConsoleMessageBodyPart>().SingleOrDefault(), metrics, scheme, color);
        WriteNewLine();
        WriteMessagePart(builder.Parts.OfType<ConsoleMessageVerbosePart>().SingleOrDefault(), scheme[MessagePart.Verbose]);
        WriteMessagePart(builder.Parts.OfType<ConsoleMessageStackTracePart>().SingleOrDefault(), scheme[MessagePart.StackTrace]);
    }

    private static void WriteMessageIcon(MessageKind kind, ConsoleColor color)
    {
        var icon = kind switch
        {
            MessageKind.Success => "✅",
            MessageKind.Error => "💥",
            MessageKind.Warning => "⚠️",
            _ => default
        };
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

    private static void WriteMessageBody(ConsoleMessageBodyPart? part, IEnumerable<ConsoleMessageMetricPart> metrics, Dictionary<MessagePart, ConsoleColor> scheme, ConsoleColor? color = null)
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
        while (body.Contains("{$"))
        {
            if (metrics.Select(e => (Metric: e, Index: body.IndexOf(e.Placeholder))).OrderBy(e => e.Index).FirstOrDefault() is (ConsoleMessageMetricPart, int) value)
            {
                WriteMessagePart(body[..value.Index], color ?? scheme[MessagePart.Body]);
                WriteMessagePart(value.Metric.Value, scheme[MessagePart.Metric]);
                body = body[(value.Index + value.Metric.Placeholder.Length)..];
            }
            else
            {
                break;
            }
        }
        WriteMessagePart(body, color ?? scheme[MessagePart.Body]);
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
