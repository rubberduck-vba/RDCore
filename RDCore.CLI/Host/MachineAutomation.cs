using RDCore.External.Automation;
using RDCore.External.Windows.Automation;

namespace RDCore.CLI.Host;

/// <summary>
/// The automation servers of the machine this host runs on.
/// </summary>
/// <remarks>
/// One to a process, since it is one thing: the machine's. Which implementation reaches them is the platform's, and the host is where the platform is known: the
/// runtime only ever sees the abstraction.
/// </remarks>
internal static class MachineAutomation
{
    private static readonly Lazy<IAutomationServer> _server = new(()
        => OperatingSystem.IsWindows() ? new ComAutomationServer() : UnavailableAutomationServer.Instance, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The automation servers of this machine.
    /// </summary>
    public static IAutomationServer Server => _server.Value;
}
