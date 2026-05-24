using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services.States;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace RDCore.CLI;

internal class Program
{
    private static readonly ConsoleMessageBuilder _messageBuilder = new();
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.Unicode;

        var splash = new ShowSplashCommand();
        splash.Execute();

        // TODO move to appsettings.json
        var options = new ServerOptions 
        {
            ConnectTimeoutSeconds = 5,
            HealthCheckIntervalSeconds = 5,
            MaximumInstances = 1,
            ShutdownTimeoutSeconds = 5,
            Verbose = true
        };
        var stateProvider = new ServerStateProvider(options);

        try
        {
            await new RDCoreConsoleClientServerApp(stateProvider).RunAsync();
        }
        catch (OperationCanceledException)
        {
            ConsoleMessageWriter.WriteMessage(new ConsoleMessageBuilder()
                .WithKind(MessageKind.Information)
                .WithMetric(MetricKind.NumericValue, "{$YEAR}", DateTimeOffset.UtcNow.Year)
                .WithTitle(Resources.RDCore_Slogan)
                .WithMessageBody(Resources.CopyrightNotice));
        }
        catch (Exception exception)
        {
            ConsoleMessageWriter.WriteMessage(_messageBuilder
                .WithKind(MessageKind.Error)
                .WithTitle(exception)
                .WithMessageBody(exception)
                .WithStackTrace(exception));
       }

        return stateProvider.State.ExitCode;
    }
}

internal abstract record class CLICommand(string Name, string? Alias = default) 
{
    public abstract void Execute();
}

internal record class ShowSplashCommand() : CLICommand("splash")
{
    public override void Execute()
    {
        ConsoleMessageWriter.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Trace)
            .WithMessageBody(Resources.RDCore_Splash), ConsoleColor.Blue);
        ConsoleMessageWriter.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Information)
            .WithMetric(MetricKind.IntegerValue, "{$YEAR}", DateTimeOffset.UtcNow.Year)
            .WithTitle(Resources.RDCore_Slogan)
            .WithMessageBody(Resources.CopyrightNotice));
    }
}

internal enum MessagePart
{
    Timestamp,
    Title,
    Body,
    Verbose,
    Metric,
    StackTrace,
}

internal enum MessageKind
{
    Trace,
    Information,
    Warning,
    Error,
    Success,
}

internal enum MetricKind
{
    IntegerValue,
    NumericValue,
    PercentageValue,
    StopwatchMilliseconds,
}

internal class MetricPartFormatter
{
    public static string FormatValue<T>(MetricKind kind, T value) 
        => kind switch
        {
            MetricKind.IntegerValue => $"{value}",
            MetricKind.NumericValue => $"{value:N1}",
            MetricKind.PercentageValue => $"{value:P1}",
            MetricKind.StopwatchMilliseconds => $"{value} ms",
            _ => value?.ToString() ?? string.Empty
        };
}

internal class ExceptionFormatter
{
    public static string FormatTitle(Exception exception)
        => exception switch
        {
            VBApplicationErrorException e => $"[{e.GetType().Name}] {e.ErrorNumber}" + ((e.ErrorSource.Length > 0) ? $" ({e.ErrorSource})" : string.Empty),
            VBCompileErrorException e => $"[{e.ToDiagnosticCode()}] @ {e.Location}",
            VBRuntimeErrorException e => $"[{e.ToDiagnosticCode()}] @ {e.Location}",
            _ => $"{exception.GetType().Name}"
        };
    public static string FormatBody(Exception exception)
        => exception switch
        {
            VBApplicationErrorException e => $" >> {e.Description}",
            _ => $" >> {exception.Message}"
        };
    public static string? FormatVerbose(Exception exception)
        => exception switch
        {
            VBCompileErrorException e => string.IsNullOrWhiteSpace(e.Verbose) ? default : $"    {e.Verbose}",
            VBRuntimeErrorException e => string.IsNullOrWhiteSpace(e.Verbose) ? default : $"    {e.Verbose}",
            _ => default
        };
    public static string? FormatStackTrace(Exception exception)
        => exception switch
        {
            // TODO surface the internal stack trace
            VBApplicationErrorException or VBCompileErrorException or VBRuntimeErrorDivisionByZeroException => default, 
            _ => exception.StackTrace
        };
}

internal abstract record class ConsoleMessagePart(MessagePart Part, string Value) { }

internal record class ConsoleMessageTimestampPart(DateTimeOffset Timestamp) : ConsoleMessagePart(MessagePart.Timestamp, $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}]") { }
internal record class ConsoleMessageTimestampPartFactory
{
    public static ConsoleMessagePart CreateTimestampPart(DateTimeOffset timestamp) => new ConsoleMessageTimestampPart(timestamp);
}
internal record class ConsoleMessageStackTracePart(Exception Exception) : ConsoleMessagePart(MessagePart.StackTrace, Exception.StackTrace ?? string.Empty) { }
internal record class ConsoleMessageStackTracePartFactory
{
    public static ConsoleMessagePart CreateStackTracePart(Exception exception) => new ConsoleMessageStackTracePart(exception);
}

internal record class ConsoleMessageVerbosePart(string Verbose) : ConsoleMessagePart(MessagePart.Verbose, Verbose) { }
internal record class ConsoleMessageVerbosePartFactory
{
    public static ConsoleMessagePart CreateVerbosePart(string verbose) => new ConsoleMessageVerbosePart(verbose);
}
internal record class ConsoleMessageTitlePart(string Title) : ConsoleMessagePart(MessagePart.Title, Title) { }
internal record class ConsoleMessageTitlePartFactory
{
    public static ConsoleMessagePart CreateTitlePart(string title) => new ConsoleMessageTitlePart(title);
    public static ConsoleMessagePart CreateTitlePart(Exception exception) => new ConsoleMessageTitlePart(ExceptionFormatter.FormatTitle(exception));
    public static ConsoleMessagePart CreateTitlePart(VBRuntimeErrorException exception) => new ConsoleMessageTitlePart(ExceptionFormatter.FormatTitle(exception));
    public static ConsoleMessagePart CreateTitlePart(VBCompileErrorException exception) => new ConsoleMessageTitlePart(ExceptionFormatter.FormatTitle(exception));
}
internal record class ConsoleMessageBodyPart(string Body) : ConsoleMessagePart(MessagePart.Body, Body) { }
internal record class ConsoleMessageBodyPartFactory
{
    public static ConsoleMessagePart CreateMessageBodyPart(string body) => new ConsoleMessageBodyPart(body);
    public static ConsoleMessagePart CreateMessageBodyPart(Exception exception) => new ConsoleMessageBodyPart(exception.Message);
}

internal record class ConsoleMessageMetricPart(MetricKind Kind, string Placeholder, double NumericValue) : ConsoleMessagePart(MessagePart.Metric, MetricPartFormatter.FormatValue(Kind, NumericValue)) { }
internal record class ConsoleMessageMetricPartFactory
{
    public static ConsoleMessagePart CreateMetricPart(MetricKind kind, string placeholder, double value) => new ConsoleMessageMetricPart(kind, placeholder, value);
}

internal record class ConsoleMessageBuilder
{
    public MessageKind Kind { get; init; } = MessageKind.Trace;
    public ImmutableArray<ConsoleMessagePart> Parts { get; init; } = [];

    public ConsoleMessageBuilder WithKind(MessageKind kind) => this with { Kind = kind };
    public ConsoleMessageBuilder WithTimestamp(DateTimeOffset timestamp) => WithUniquePart(ConsoleMessageTimestampPartFactory.CreateTimestampPart(timestamp));
    public ConsoleMessageBuilder WithTitle(string title) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(title));
    public ConsoleMessageBuilder WithTitle(VBCompileErrorException exception) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(exception));
    public ConsoleMessageBuilder WithTitle(VBRuntimeErrorException exception) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(exception));
    public ConsoleMessageBuilder WithTitle(VBApplicationErrorException exception) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(exception));
    public ConsoleMessageBuilder WithTitle(Exception exception) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(exception));
    public ConsoleMessageBuilder WithMessageBody(string body) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(body));
    public ConsoleMessageBuilder WithMessageBody(Exception exception) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(exception));
    public ConsoleMessageBuilder WithVerbose(string verbose) => WithUniquePart(ConsoleMessageVerbosePartFactory.CreateVerbosePart(verbose));
    public ConsoleMessageBuilder WithStackTrace(Exception exception) => WithUniquePart(ConsoleMessageStackTracePartFactory.CreateStackTracePart(exception));
    public ConsoleMessageBuilder WithMetric(MetricKind kind, string placeholder, double value) => WithPart(ConsoleMessageMetricPartFactory.CreateMetricPart(kind, placeholder, value));
    public ConsoleMessageBuilder WithPart(ConsoleMessagePart part) => this with { Parts = [.. Parts, part] };
    private ConsoleMessageBuilder WithUniquePart<TPart>(TPart part) where TPart : ConsoleMessagePart
    {
        if (!Parts.Any(e => e.Part == part.Part))
        {
            return WithPart(part);
        }
        return this;
    }
}

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


internal interface IConsoleWriterService
{
    void WriteLine(string message);
}

internal class RDCoreConsoleClientServerApp(IServerStateProvider serverStateProvider) : ServerApp(serverStateProvider)
{
    protected override void ConfigureAppServices(IServiceCollection services)
    {
        //throw new NotImplementedException();
    }

    protected override void ConfigureLogging(ILoggingBuilder builder)
    {
        base.ConfigureLogging(builder);
    }
}