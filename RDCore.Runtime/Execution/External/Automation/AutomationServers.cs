using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.External.Automation;

/// <summary>
/// The automation servers of the machine this process runs on.
/// </summary>
/// <remarks>
/// One to a process, since it is one thing: the machine's. A machine that has none is not an error to have asked for - a program that needs one is told so
/// when it asks (<see cref="UnavailableAutomationServer"/>), which is also what makes a program that references <c>Excel</c> analyzable anywhere.
/// </remarks>
public static class AutomationServers
{
    private static readonly Lazy<IAutomationServer> _machine = new(()
        => OperatingSystem.IsWindows() ? new ComAutomationServer() : new UnavailableAutomationServer(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The automation servers of this machine.
    /// </summary>
    public static IAutomationServer Machine => _machine.Value;
}

/// <summary>
/// The automation servers of a machine that has none.
/// </summary>
public sealed class UnavailableAutomationServer : IAutomationServer
{
    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public object CreateObject(string progId) => throw Unavailable();

    /// <inheritdoc/>
    public object? Invoke(
        object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, System.Globalization.CultureInfo culture)
        => throw Unavailable();

    /// <inheritdoc/>
    public string? ClassNameOf(object target) => null;

    /// <inheritdoc/>
    public bool MoveNext(object enumerator, out object? current) => throw Unavailable();

    /// <inheritdoc/>
    public void Reset(object enumerator) => throw Unavailable();

    /// <inheritdoc/>
    public void Release(object handle)
    {
    }

    private static AutomationException Unavailable()
        => new(unchecked((int)0x80040154), "Automation servers are not available on this platform.");
}
