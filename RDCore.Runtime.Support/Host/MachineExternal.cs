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

namespace RDCore.Runtime.Support.Host;

/// <summary>
/// The outside world of the machine this host runs on - its automation servers, its native libraries, its dialogs and its desktop - for a host that was not composed with its own:
/// reached through an external host of this process.
/// </summary>
/// <remarks>
/// One to a process, since it is one thing: the machine's. A host that runs as the platform's environment host is composed with its external host, and logs what
/// it does; this one is for a host that was built by hand, with nothing to log to.
/// </remarks>
internal static class MachineExternal
{
    /// <summary>
    /// The executable the external host is started from, run in external mode: <c>rdc</c> beside this process's assemblies, unless this process is a built
    /// program, whose folder holds no <c>rdc</c> and which is its own external host. Read once, when the world is first reached.
    /// </summary>
    public static string Executable { get; set; } = ExternalHost.DefaultExecutable;

    private static ExternalHost? _host;

    private static readonly Lazy<ExternalWorld> _world = new(() =>
    {
        var options = Options.Create(new SdkAppOptions());
        var host = _host = new ExternalHost(
            () => new ChildConnection(
                new RDCoreServerProcess(new FileSystem(), PlatformEnvironment.Default, options, NullLogger<RDCoreServerProcess>.Instance),
                new RDCorePlatformDefaultTransportLayer(options),
                NullLogger<ChildConnection>.Instance),
            Executable,
            NullLogger<ExternalHost>.Instance);
        var servers = new RemoteAutomationServer(host);
        return new ExternalWorld(servers, new RemoteNativeLibraryHost(host))
        {
            Forms = servers,
            Interaction = MachinePlatform.Interaction,
            Desktop = MachinePlatform.Desktop,
        };
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The outside world of this machine.
    /// </summary>
    public static ExternalWorld World => _world.Value;

    /// <summary>
    /// Stops the external host, if one was started, and waits for its process to end: a program that ended leaves nothing running in its folder.
    /// </summary>
    public static void Shutdown()
    {
        if (_world.IsValueCreated)
        {
            _host?.Dispose();
        }
    }
}
