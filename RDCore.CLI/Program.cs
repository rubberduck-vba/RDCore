using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDCore.CLI.Themes.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services.States;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Text;

namespace RDCore.CLI;

public class Program
{
    private static readonly ConsoleMessageBuilder _messageBuilder = new();
    public static async Task<int> Main(string[] args)
    {
        var fileSystem = new FileSystem();
        
        var loader = new AppThemeLoaderService(options, fileSystem);
        var themeService = new AppThemeService(options, loader);
        var themes = new AppThemeService(options, loader);
        var writer = new ConsoleMessageWriter(themeService);
        
        var splash = new ShowSplashCommand(writer, themes);
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
            await new RDCoreConsoleClientServerApp().RunAsync();
        }
        catch (OperationCanceledException)
        {
            writer.WriteMessage(new ConsoleMessageBuilder()
                .WithKind(MessageKind.Information)
                .WithTitle(Resources.RDCore_Slogan)
                .WithMessageBody(Resources.CopyrightNotice));
        }
        catch (Exception exception)
        {
            writer.WriteMessage(_messageBuilder
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

internal record class ShowSplashCommand : CLICommand
{
    private readonly ConsoleMessageWriter _writer = default!;
    private readonly IAppThemeService _themes;
    public ShowSplashCommand(ConsoleMessageWriter writer, IAppThemeService themes) : base("slash")
    {
        _writer = writer;
        _themes = themes;
    }

    public override void Execute()
    {
        _writer.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Trace)
            .WithMessageBody(Resources.RDCoreSplash_Background)
            .WithMessageOverlay(Resources.RDCoreSplash_Foreground))
            ;
        _writer.WriteMessage(new ConsoleMessageBuilder()
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
    public ConsoleMessageBuilder WithMessageOverlay(string overlay) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(overlay));
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


internal class RDCoreConsoleClientServerApp : ServerApp
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