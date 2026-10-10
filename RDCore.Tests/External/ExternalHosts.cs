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
    public static ExternalHost New()
    {
        var options = Options.Create(new SdkAppOptions());
        return new ExternalHost(
            () => new ChildConnection(
                new RDCoreServerProcess(new FileSystem(), PlatformEnvironment.Default, options, NullLogger<RDCoreServerProcess>.Instance),
                new RDCorePlatformDefaultTransportLayer(options),
                NullLogger<ChildConnection>.Instance),
            ExternalHost.DefaultExecutable,
            NullLogger<ExternalHost>.Instance);
    }

    /// <summary>
    /// Everything a program reaches outside the platform, through <paramref name="host"/>.
    /// </summary>
    public static ExternalWorld WorldOf(ExternalHost host) => new(new RemoteAutomationServer(host), new RemoteNativeLibraryHost(host));
}
