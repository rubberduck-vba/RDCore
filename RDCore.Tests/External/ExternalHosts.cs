using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RDCore.External;
using RDCore.External.Client;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions;

namespace RDCore.Tests.External;

/// <summary>
/// External hosts of a test's own: one that a test takes down is not the one the tests that run alongside it are using.
/// </summary>
internal static class ExternalHosts
{
    /// <summary>
    /// An external host of the test process, started when it is first needed.
    /// </summary>
    public static ExternalHost New() => New(canStart: () => true);

    /// <summary>
    /// An external host of the test process that starts only once <paramref name="canStart"/> says it can: until then, each start fails, as one that could not be
    /// made does.
    /// </summary>
    /// <param name="canStart">Whether the host can be started now.</param>
    public static ExternalHost New(Func<bool> canStart)
    {
        // what the host writes is the test's output, and not the test process's own streams: a host a test takes down on purpose says so in that test's
        // output, and not in what the test run reports when its process ends.
        var options = Options.Create(new SdkAppOptions { Server = { CaptureChildOutput = true } });
        return new ExternalHost(
            () => canStart()
                ? new ExternalHostProcess(
                    new ChildConnection(
                        new RDCoreServerProcess(new FileSystem(), PlatformEnvironment.Default, options, ChildOutput.Instance),
                        new RDCorePlatformDefaultTransportLayer(options),
                        NullLogger<ChildConnection>.Instance),
                    ExternalHost.DefaultExecutable)
                : throw new InvalidOperationException("The external host is not to be started yet."),
            NullLogger<ExternalHost>.Instance);
    }

    // the lines the host writes, which the server process logs as they come, written to the standard error of the test that started it.
    private sealed class ChildOutput : ILogger<RDCoreServerProcess>
    {
        public static ChildOutput Instance { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is LogLevel.Trace;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Console.Error.WriteLine(formatter(state, exception));
            }
        }
    }

    /// <summary>
    /// Everything a program reaches outside the platform, through <paramref name="host"/>: its servers, which host its forms too, and its native libraries.
    /// </summary>
    public static ExternalWorld WorldOf(ExternalHost host)
    {
        var servers = new RemoteAutomationServer(host);
        return new(servers, new RemoteNativeLibraryHost(host)) { Forms = servers };
    }

    /// <summary>
    /// The world of <paramref name="host"/>, whose forms are created through <paramref name="forms"/>: what a test that steps in before a form is made is given.
    /// </summary>
    /// <param name="host">The external host.</param>
    /// <param name="forms">The forms of the world, made of its servers.</param>
    public static ExternalWorld WorldOf(ExternalHost host, Func<RemoteAutomationServer, RDCore.External.Forms.IFormHost> forms)
    {
        var servers = new RemoteAutomationServer(host);
        return new(servers, new RemoteNativeLibraryHost(host)) { Forms = forms(servers) };
    }
}
