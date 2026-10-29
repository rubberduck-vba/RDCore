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
        var options = Options.Create(new SdkAppOptions());
        return new ExternalHost(
            () => canStart()
                ? new ChildConnection(
                    new RDCoreServerProcess(new FileSystem(), PlatformEnvironment.Default, options, NullLogger<RDCoreServerProcess>.Instance),
                    new RDCorePlatformDefaultTransportLayer(options),
                    NullLogger<ChildConnection>.Instance)
                : throw new InvalidOperationException("The external host is not to be started yet."),
            ExternalHost.DefaultExecutable,
            NullLogger<ExternalHost>.Instance);
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
