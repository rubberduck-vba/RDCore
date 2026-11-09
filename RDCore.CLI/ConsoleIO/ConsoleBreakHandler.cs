namespace RDCore.CLI.ConsoleIO;

public sealed class ConsoleBreakHandler : IDisposable
{
    private readonly Action _onBreak;
    private bool _disposed;

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
