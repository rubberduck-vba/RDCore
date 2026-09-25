namespace RDCore.SDK.ConsoleIO;

/// <summary>
/// Turns the console's break keys into a callback for as long as it is alive, instead of letting
/// them end the process.
/// </summary>
/// <remarks>
/// An interactive shell needs <kbd>Ctrl</kbd>+<kbd>C</kbd> and <kbd>Ctrl</kbd>+<kbd>Break</kbd> to
/// mean "stop what you are doing" — the RUN/STOP key, not "quit" — which is only true while
/// something owns them. Disposing hands them back.
/// <para>
/// Cancelling the default behaviour is best-effort for <kbd>Ctrl</kbd>+<kbd>Break</kbd>
/// specifically: some hosts terminate the process on it whatever a handler asks for. It is honoured
/// for <kbd>Ctrl</kbd>+<kbd>C</kbd>, which is why a shell should treat both as the same key rather
/// than relying on either alone.
/// </para>
/// </remarks>
public sealed class ConsoleBreakHandler : IDisposable
{
    private readonly Action _onBreak;
    private bool _disposed;

    /// <summary>
    /// Starts intercepting the break keys.
    /// </summary>
    /// <param name="onBreak">
    /// Runs on the console's own handler thread, so it must return promptly — signal something and
    /// let the shell react, rather than doing the work here.
    /// </param>
    public ConsoleBreakHandler(Action onBreak)
    {
        _onBreak = onBreak;
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _onBreak();
    }

    /// <summary>Stops intercepting the break keys.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Console.CancelKeyPress -= OnCancelKeyPress;
    }
}
