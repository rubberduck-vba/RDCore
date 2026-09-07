using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.ConsoleIO;

[TestClass]
public sealed class RDCoreConsoleLoggerTests
{
    private sealed class CapturingWriter : IConsoleMessageWriter
    {
        public List<ConsoleMessageBuilder> Written { get; } = [];
        public ConsoleMessageBuilder? Last => Written.Count == 0 ? null : Written[^1];

        public IConsoleMessageWriter WriteMessage(ConsoleMessageBuilder builder)
        {
            Written.Add(builder);
            return this;
        }

        public IConsoleMessageWriter Clear() => this;
        public IConsoleMessageWriter WriteException(Exception exception) => this;
        public IConsoleMessageWriter WriteAssemblyInfo() => this;
        public IConsoleMessageWriter WriteSlogan() => this;
        public IConsoleMessageWriter WriteLegalNotice() => this;
    }

    private static (RDCoreConsoleLogger Logger, CapturingWriter Writer) NewLogger(LogLevel floor, bool verbose = true)
    {
        var options = new SdkServerOptions { TraceLevel = floor, Verbose = verbose };
        var writer = new CapturingWriter();
        return (new RDCoreConsoleLogger(Options.Create(options), writer), writer);
    }

    [TestMethod]
    public void IsEnabled_FloorSemantics()
    {
        var (trace, _) = NewLogger(LogLevel.Trace);
        Assert.IsTrue(trace.IsEnabled(LogLevel.Information));
        Assert.IsTrue(trace.IsEnabled(LogLevel.Error));
        Assert.IsFalse(trace.IsEnabled(LogLevel.None));

        var (warn, _) = NewLogger(LogLevel.Warning);
        Assert.IsFalse(warn.IsEnabled(LogLevel.Information));
        Assert.IsTrue(warn.IsEnabled(LogLevel.Warning));

        var (off, _) = NewLogger(LogLevel.None);
        Assert.IsFalse(off.IsEnabled(LogLevel.Error));
    }

    [TestMethod]
    public void Log_KeepsTitleBodyAndVerbose_ThroughTheImmutableChain()
    {
        var (logger, writer) = NewLogger(LogLevel.Trace, verbose: true);

        logger.Log(LogLevel.Warning, "TTL", "the body", "the detail");

        var built = writer.Last!;
        Assert.AreEqual(MessageKind.Warning, built.Kind);
        Assert.AreEqual("TTL", built.Title);
        Assert.AreEqual("the body", built.Body);
        Assert.AreEqual("the detail", built.Verbose);
    }

    [TestMethod]
    public void Log_TState_HonoursTheMessageTemplateFormatter()
    {
        var (logger, writer) = NewLogger(LogLevel.Trace);

        ((ILogger)logger).LogInformation("{Count} items defined", 5);

        Assert.AreEqual("5 items defined", writer.Last!.Body);
    }

    [TestMethod]
    public void Log_BelowFloor_WritesNothing()
    {
        var (logger, writer) = NewLogger(LogLevel.Error);

        ((ILogger)logger).LogInformation("ignored");

        Assert.AreEqual(0, writer.Written.Count);
    }

    [TestMethod]
    public void BeginScope_ReturnsADisposable()
    {
        var (logger, _) = NewLogger(LogLevel.Trace);
        using var scope = logger.BeginScope("s");
        Assert.IsNotNull(scope);
    }
}
