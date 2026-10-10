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

namespace RDCore.CLI.Host;

/// <summary>
/// The outside world of the machine this host runs on - its automation servers and its native libraries - for a host that was not composed with its own:
/// reached through an external host of this process.
/// </summary>
/// <remarks>
/// One to a process, since it is one thing: the machine's. A host that runs as the platform's environment host is composed with its external host, and logs what
/// it does; this one is for a host that was built by hand, with nothing to log to.
/// </remarks>
internal static class MachineExternal
{
    private static readonly Lazy<ExternalWorld> _world = new(() =>
    {
        var options = Options.Create(new SdkAppOptions());
        var host = new ExternalHost(
            () => new ChildConnection(
                new RDCoreServerProcess(new FileSystem(), PlatformEnvironment.Default, options, NullLogger<RDCoreServerProcess>.Instance),
                new RDCorePlatformDefaultTransportLayer(options),
                NullLogger<ChildConnection>.Instance),
            ExternalHost.DefaultExecutable,
            NullLogger<ExternalHost>.Instance);
        return new ExternalWorld(new RemoteAutomationServer(host), new RemoteNativeLibraryHost(host));
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The outside world of this machine.
    /// </summary>
    public static ExternalWorld World => _world.Value;
}
