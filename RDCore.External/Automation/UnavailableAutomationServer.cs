namespace RDCore.External.Automation;

/// <summary>
/// The automation servers of a machine that has none.
/// </summary>
/// <remarks>
/// A machine that has none is not an error to have asked for: a program that needs one is told so when it asks, which is also what makes a program that
/// references <c>Excel</c> analyzable anywhere.
/// </remarks>
public sealed class UnavailableAutomationServer : IAutomationServer
{
    /// <summary>
    /// The servers of a machine that has none: there is nothing about them to hold, and one is as good as another.
    /// </summary>
    public static UnavailableAutomationServer Instance { get; } = new();

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
    public void Advise(object source, IAutomationEventSink sink)
    {
    }

    /// <inheritdoc/>
    public void Unadvise(object source)
    {
    }

    /// <inheritdoc/>
    public void Release(object handle)
    {
    }

    private static AutomationException Unavailable()
        => new(unchecked((int)0x80040154), "Automation servers are not available on this platform.");
}
