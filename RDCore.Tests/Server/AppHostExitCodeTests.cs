using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDCore.SDK;
using RDCore.SDK.Client;
using RDCore.SDK.Server;

namespace RDCore.Tests.Server;

[TestClass]
public class AppHostExitCodeTests
{
    [TestMethod]
    // adversarial review, PRs #208-224, item 6: BuildAndRunAsync's own catch swallowed any fault from
    // the hosted app, then RunAsync's final `return ExitCode` read a state-derived exit code that was
    // still whatever the server's initial (benign) state carried - StartingServerState.ExitCode is 0,
    // the same value a client exiting before it ever sends Initialize legitimately produces. A server
    // that never started must not be indistinguishable from a clean shutdown to any supervisor.
    public async Task RunAsync_WhenTheHostedAppThrows_ReturnsANonZeroExitCode()
    {
        var host = new FaultingTestAppHost();

        var exitCode = await host.RunAsync([]);

        Assert.AreNotEqual(0, exitCode);
    }

    [TestMethod]
    public async Task RunAsync_WhenTheHostedAppSucceeds_ReturnsTheBaseExitCode()
    {
        var host = new SucceedingTestAppHost();

        var exitCode = await host.RunAsync([]);

        Assert.AreEqual(0, exitCode);
    }

    [TestMethod]
    [DataRow("--help")]
    [DataRow("--version")]
    public async Task RunAsync_WhenTheCommandLineOnlyAskedForText_SucceedsWithoutBuildingTheHost(string arg)
    {
        // `rdc --help` printed its help text, then an ArgumentNullException stack trace underneath it,
        // and exited -1: the parser answers --help and --version itself and hands back a default
        // instance, which the client host's "a client cannot start without a workspace" guard rejected.
        // A command line that only asked a question is a successful run, not a startup failure — and
        // there is nothing to configure or start, which is what UnreachableFakeApp is here to prove.
        var host = new TextOnlyCommandLineTestAppHost();

        var exitCode = await host.RunAsync([arg]);

        Assert.AreEqual(0, exitCode);
    }

    private sealed class TextOnlyCommandLineTestAppHost : AppHost<TextOnlyCommandLineTestAppHost.UnreachableFakeApp>
    {
        protected override void Configure(IConfigurationBuilder configuration, IServiceCollection services, string[] args)
            => throw new InvalidOperationException("nothing should be configured when the command line only asked for text");

        internal sealed class UnreachableFakeApp : IRDCoreApp
        {
            public CoreServerComponent PlatformComponent => CoreServerComponent.LanguageServer;
            public Task RunAsync(IServiceProvider provider, string[] args)
                => throw new InvalidOperationException("nothing should be run when the command line only asked for text");
            public void LogIfEnabled(LogLevel logLevel, string message) { }
            public void Dispose() { }
        }
    }

    private sealed class FaultingTestAppHost : AppHost<FaultingTestAppHost.FaultingFakeApp>
    {
        protected override void Configure(IConfigurationBuilder configuration, IServiceCollection services, string[] args) { }

        internal sealed class FaultingFakeApp : IRDCoreApp
        {
            public CoreServerComponent PlatformComponent => CoreServerComponent.LanguageServer;
            public Task RunAsync(IServiceProvider provider, string[] args) => throw new InvalidOperationException("simulated startup failure");
            public void LogIfEnabled(LogLevel logLevel, string message) { }
            public void Dispose() { }
        }
    }

    private sealed class SucceedingTestAppHost : AppHost<SucceedingTestAppHost.SucceedingFakeApp>
    {
        protected override void Configure(IConfigurationBuilder configuration, IServiceCollection services, string[] args) { }

        internal sealed class SucceedingFakeApp : IRDCoreApp
        {
            public CoreServerComponent PlatformComponent => CoreServerComponent.LanguageServer;
            public Task RunAsync(IServiceProvider provider, string[] args) => Task.CompletedTask;
            public void LogIfEnabled(LogLevel logLevel, string message) { }
            public void Dispose() { }
        }
    }
}
