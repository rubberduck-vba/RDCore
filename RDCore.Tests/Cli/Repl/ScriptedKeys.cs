using RDCore.CLI.App.Console;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// Keys typed at a console, for a test: a key at a time, or - as a script piped to the shell has it - a line at a time.
/// </summary>
internal sealed class ScriptedKeys(bool interactive = true) : IConsoleKeys
{
    private readonly Queue<ConsoleKeyInfo> _keys = new();
    private readonly Queue<string?> _lines = new();

    public bool IsInteractive => interactive;

    public static ConsoleKeyInfo Key(ConsoleKey key, bool shift = false, bool control = false) => new('\0', key, shift, false, control);

    public static ConsoleKeyInfo Char(char character)
        => new(character, char.IsAsciiLetter(character) ? Enum.Parse<ConsoleKey>(char.ToUpperInvariant(character).ToString()) : ConsoleKey.NoName, char.IsUpper(character), false, false);

    public ScriptedKeys Press(params ConsoleKeyInfo[] keys)
    {
        foreach (var key in keys)
        {
            _keys.Enqueue(key);
        }

        return this;
    }

    public ScriptedKeys Type(string text) => Press([.. text.Select(Char)]);

    public ScriptedKeys Lines(params string?[] lines)
    {
        foreach (var line in lines)
        {
            _lines.Enqueue(line);
        }

        return this;
    }

    public ConsoleKeyInfo ReadKey(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return _keys.Count > 0 ? _keys.Dequeue() : throw new OperationCanceledException("the script has no more keys");
    }

    public string? ReadLine(CancellationToken token) => _lines.Count > 0 ? _lines.Dequeue() : null;
}
