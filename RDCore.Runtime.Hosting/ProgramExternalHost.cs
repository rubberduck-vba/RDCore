using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform;

namespace RDCore.Runtime.Hosting;

/// <summary>
/// A host that is the external host of its own machines: the process that makes their calls to the outside world of the machine (the forms they make, the
/// objects of automation servers they create and their native calls) is the host's own executable, started again in external mode, as a built program is.
/// A host that deploys no <c>rdc</c> is its own external host.
/// </summary>
/// <remarks>
/// The host's <c>Main</c> runs as the external host first, when it was started as one, and names its own executable as its machines' external host:
/// <code>
/// public static int Main(string[] args)
/// {
///     if (ProgramExternalHost.IsThisProcess)
///     {
///         return ProgramExternalHost.Run(args);
///     }
///
///     var options = new ComposedProgramOptions { ExternalHost = ProgramExternalHost.Executable };
///     // load the program with the options, and run the host
/// }
/// </code>
/// </remarks>
public static class ProgramExternalHost
{
    /// <summary>
    /// Whether this process was started as the external host of a program's machines: <c>RDCORE_MODE</c> is <c>external</c>. Its <c>Main</c> then calls
    /// <see cref="Run"/>, and runs nothing else.
    /// </summary>
    public static bool IsThisProcess => MachineExternalHost.IsThisProcess;

    /// <summary>
    /// The executable of this process, which its machines' external host is started from (<see cref="ComposedProgramOptions.ExternalHost"/>). It is
    /// <see langword="null"/> for a process run with <c>dotnet</c>, whose executable is <c>dotnet</c>'s and cannot run as an external host.
    /// </summary>
    public static string? Executable
        => Environment.ProcessPath is { } path && !string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? path
            : null;

    /// <summary>
    /// Runs this process as the external host of the machines of the process that started it, until that process stops it. It makes their forms (in RDForms),
    /// the objects of the machine's automation servers (on Windows) and their native calls (on Windows).
    /// </summary>
    /// <remarks>
    /// It is called from <c>Main</c>, on the process's main thread: on macOS, the windows of the forms run their loop on it (<see cref="ProcessMainThread"/>).
    /// </remarks>
    /// <param name="args">The arguments the process was started with, which say how to reach the process that started it.</param>
    /// <returns>The exit code of the process.</returns>
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return ProcessMainThread.Serve(() => MachineExternalHost.RunThisProcessAsync(args));
    }
}
