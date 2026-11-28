using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RDCore.External;
using RDCore.External.Client;
using RDCore.External.Interaction;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using System.Collections.Concurrent;
using System.IO.Abstractions;

namespace RDCore.Runtime.Support.Host;

/// <summary>
/// The outside world of the machine this host runs on - its automation servers, its native libraries, its dialogs and its desktop - for a host that was not composed with its own:
/// reached through an external host of this process.
/// </summary>
/// <remarks>
/// One to each executable an external host is started from, since each is one thing: the machine's, reached through a process of that executable. A host
/// that runs as the platform's environment host is composed with its external host, and logs what it does; this one is for a host that was built by hand,
/// with nothing to log to.
/// </remarks>
internal static class MachineExternal
{
    /// <summary>
    /// The executable the external host of <see cref="World"/> is started from, run in external mode: <c>rdc</c> beside this process's assemblies, unless this
    /// process is a built program, whose folder holds no <c>rdc</c> and which is its own external host.
    /// </summary>
    public static string Executable { get; set; } = ExternalHost.DefaultExecutable;

    // the worlds reached in this process, by the full path of the executable their external host is started from.
    private static readonly ConcurrentDictionary<string, Lazy<Reached>> _worlds
        = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private sealed record class Reached(ExternalWorld World, ExternalHost Host, RemoteAutomationServer Servers);

    /// <summary>
    /// The outside world of this machine, through an external host started from <see cref="Executable"/>.
    /// </summary>
    public static ExternalWorld World => WorldOf(Executable);

    /// <summary>
    /// The outside world of this machine, through an external host started from <paramref name="executable"/>: the same world each time it is asked for one of
    /// the same executable. Nothing is started until a program reaches out.
    /// </summary>
    /// <param name="executable">The executable the external host is started from, run in external mode: <c>rdc</c>, or a built program.</param>
    public static ExternalWorld WorldOf(string executable) => ReachedThrough(executable).World;

    private static Reached ReachedThrough(string executable)
        => _worlds.GetOrAdd(Path.GetFullPath(executable), full => new(() => Reach(full), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static Reached Reach(string executable)
    {
        var options = Options.Create(new SdkAppOptions());
        var host = new ExternalHost(
            () => new ExternalHostProcess(
                new ChildConnection(
                    new RDCoreServerProcess(new FileSystem(), PlatformEnvironment.Default, options, NullLogger<RDCoreServerProcess>.Instance),
                    new RDCorePlatformDefaultTransportLayer(options),
                    NullLogger<ChildConnection>.Instance),
                executable),
            NullLogger<ExternalHost>.Instance);
        var servers = new RemoteAutomationServer(host);
        var world = new ExternalWorld(servers, new RemoteNativeLibraryHost(host))
        {
            Forms = servers,
            Interaction = MachinePlatform.Interaction,
            Desktop = MachinePlatform.Desktop,
        };
        return new Reached(world, host, servers);
    }

    /// <summary>
    /// The dialogs of a program whose user interface is its forms: shown by the external host over them, or by <paramref name="fallback"/> when the
    /// external host shows none.
    /// </summary>
    /// <param name="fallback">The dialogs shown when the external host shows none.</param>
    public static IUserInteraction FormDialogs(IUserInteraction fallback) => new RemoteUserInteraction(ReachedThrough(Executable).Servers, fallback);

    /// <summary>
    /// Stops every external host that was started, and waits for its process to end: a program that ended leaves nothing running in its folder.
    /// </summary>
    public static void Shutdown()
    {
        foreach (var reached in _worlds.Values.Where(reached => reached.IsValueCreated))
        {
            reached.Value.Host.Dispose();
        }
    }
}
