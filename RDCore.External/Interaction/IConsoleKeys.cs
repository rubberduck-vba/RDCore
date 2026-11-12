namespace RDCore.External.Interaction;

/// <summary>
/// Where a prompt or a dialog at a console reads what is typed: a key at a time at a console, or a line at a time when the input is a script.
/// </summary>
public interface IConsoleKeys
{
    /// <summary>
    /// Whether a key can be read at a time: <see langword="false"/> when the input is redirected (a script piped to the program), and a prompt reads a line instead.
    /// </summary>
    bool IsInteractive { get; }

    /// <summary>
    /// Reads the next key, without echoing it.
    /// </summary>
    /// <remarks>
    /// A key that is there when <paramref name="token"/> is cancelled is read, not left for whatever reads the keys next: the prompt stops reading its key to
    /// have a question asked, and a key typed before then is the prompt's.
    /// </remarks>
    /// <param name="token">Stops waiting for one.</param>
    /// <exception cref="OperationCanceledException"><paramref name="token"/> was cancelled before a key was pressed.</exception>
    ConsoleKeyInfo ReadKey(CancellationToken token);

    /// <summary>
    /// Reads the next line, or <see langword="null"/> at the end of the input.
    /// </summary>
    /// <param name="token">Stops waiting for one, when the input can be stopped waiting for.</param>
    string? ReadLine(CancellationToken token);
}

/// <summary>
/// The keys of the process's own console.
/// </summary>
/// <remarks>
/// A key is waited for by asking whether one is available, so that a prompt whose program is stopped stops waiting: <see cref="System.Console.ReadKey(bool)"/> blocks
/// its thread, and nothing can interrupt it.
/// </remarks>
public sealed class SystemConsoleKeys : IConsoleKeys
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(15);

    /// <inheritdoc/>
    public bool IsInteractive => !System.Console.IsInputRedirected;

    /// <inheritdoc/>
    public ConsoleKeyInfo ReadKey(CancellationToken token)
    {
        while (!System.Console.KeyAvailable)
        {
            token.ThrowIfCancellationRequested();
            Thread.Sleep(Poll);
        }

        return System.Console.ReadKey(intercept: true);
    }

    /// <inheritdoc/>
    public string? ReadLine(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return System.Console.ReadLine();
    }
}
