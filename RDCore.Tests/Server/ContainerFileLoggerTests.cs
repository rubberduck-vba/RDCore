using Microsoft.Extensions.Logging;
using RDCore.SDK.Server.Logging;

namespace RDCore.Tests.Server;

/// <summary>
/// The records of OmniSharp's own container, written to the component's file through the application's logger.
/// </summary>
[TestClass]
public sealed class ContainerFileLoggerTests
{
    // what the application's logger is given.
    private sealed class AppLogger(LogLevel minimum = LogLevel.Information) : ILogger
    {
        public List<(LogLevel Level, EventId Event, Exception? Exception, string Message)> Logged { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Logged.Add((logLevel, eventId, exception, formatter(state, exception)));
    }

    [TestMethod]
    public void ARecordOfTheContainer_IsWrittenWithItsCategory_AsAContainerRecord()
    {
        var app = new AppLogger();
        var failure = new InvalidOperationException("the output loop ended");

        new ContainerFileLoggerProvider(app).CreateLogger("OmniSharp.Extensions.JsonRpc.InputHandler")
            .Log(LogLevel.Critical, new EventId(1), "unhandled exception", failure, (state, _) => state);

        var (level, logged, exception, message) = app.Logged.Single();
        Assert.AreEqual(LogLevel.Critical, level);
        Assert.AreEqual(ContainerFileLoggerProvider.ContainerRecord, logged);
        Assert.AreSame(failure, exception, "the exception is the file's to write, whole");
        Assert.AreEqual("OmniSharp.Extensions.JsonRpc.InputHandler: unhandled exception", message);
    }

    [TestMethod]
    public void ARecordBelowTheApplicationsLevel_IsNotWritten()
    {
        var app = new AppLogger(LogLevel.Information);
        var logger = new ContainerFileLoggerProvider(app).CreateLogger("OmniSharp.Extensions.JsonRpc.ProcessScheduler");

        logger.Log(LogLevel.Debug, default, "Queueing Parallel:textDocument/diagnostic:6 request for processing", null, (state, _) => state);

        Assert.IsFalse(logger.IsEnabled(LogLevel.Debug));
        Assert.IsEmpty(app.Logged);
    }

    [TestMethod]
    public void AContainerRecord_IsNotSentToTheClientAgain()
    {
        // the container's own provider has sent it: sent again, a warning that sending raises would be sent once more, and so on.
        var trace = new ClientTraceLogger(() => throw new AssertFailedException("a container record reached the client trace"), "RDCore.LS");

        trace.Log(LogLevel.Warning, ContainerFileLoggerProvider.ContainerRecord, "OmniSharp.Extensions.LanguageServer.Server.LspServerOutputFilter: Tried to send",
            null, (state, _) => state);
    }
}
