namespace RDCore.CLI.ConsoleIO;

// Ctrl+C and Ctrl+Break mean "stop what you are doing" (the RUN/STOP key, not "quit") while this is alive. Some hosts end the process on Ctrl+Break
// whatever a handler asks, so a shell treats both keys as one rather than rely on either alone.
public sealed class ConsoleBreakHandler : IDisposable
{
    private readonly Action _onBreak;
    private bool _disposed;

    // onBreak runs on the console's own handler thread: it signals the shell and returns, rather than do the work there.
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
