using System.Runtime.InteropServices;

namespace RDCore.SDK.ConsoleIO;

/// <summary>
/// Owns the console's <em>frame</em>: the background and foreground the whole shell is painted on,
/// for as long as the process owns the terminal.
/// </summary>
/// <remarks>
/// Setting <see cref="Console.BackgroundColor"/> and clearing is not enough on its own. Every
/// renderer that emits SGR sequences — Spectre.Console among them — ends a styled span with a reset
/// (<c>ESC[0m</c>), which returns the cell colours to the <em>terminal's</em> defaults, not to
/// whatever <see cref="Console.BackgroundColor"/> currently holds; .NET will not re-emit an attribute
/// it believes is already set, so from the first styled write onwards the frame is silently gone, and
/// every line that scrolls in afterwards is painted in the terminal's own background. Changing the
/// terminal's default colours instead (<c>OSC 10</c>/<c>OSC 11</c>) makes the reset land back on the
/// frame's colours, so the shell stays one solid colour through styled output, scrolling and screen
/// clears alike.
/// <para>
/// A terminal that cannot do this gets the nearest-16 <see cref="ConsoleColor"/> frame instead, which
/// is what the frame did before; <see cref="Restore"/> puts the terminal back the way it was found,
/// and must run before the process exits.
/// </para>
/// </remarks>
public interface IConsoleShellFrame
{
    /// <summary>
    /// Whether the console accepts ANSI escape sequences — <c>false</c> when output is redirected or
    /// the terminal could not be switched into virtual-terminal mode. The frame degrades to the
    /// legacy 16-colour console attributes when this is <c>false</c>.
    /// </summary>
    bool IsAnsiEnabled { get; }

    /// <summary>
    /// Paints the frame: makes <paramref name="background"/>/<paramref name="foreground"/> the
    /// console's default colours and clears the screen.
    /// </summary>
    /// <param name="background">The shell background.</param>
    /// <param name="foreground">The shell foreground.</param>
    void Apply(ConsoleRgbColor background, ConsoleRgbColor foreground);

    /// <summary>
    /// Writes pre-formatted art (a splash banner, a program listing) one line at a time in
    /// <paramref name="color"/>, without any layout, wrapping or re-flowing, and fills the rest of
    /// each line with the frame's background so a short line does not leave a differently-coloured
    /// tail behind.
    /// </summary>
    /// <param name="art">The pre-formatted text; every line is written as-is.</param>
    /// <param name="color">The foreground colour to write it in.</param>
    void WriteArt(string art, ConsoleRgbColor color);

    /// <summary>
    /// Restores the console colours this frame replaced. Safe to call more than once, and a no-op if
    /// <see cref="Apply"/> never ran.
    /// </summary>
    void Restore();
}

/// <inheritdoc cref="IConsoleShellFrame"/>
public sealed class ConsoleShellFrame : IConsoleShellFrame
{
    // OSC 10/11 set the terminal's default foreground/background; OSC 110/111 restore them. The
    // string terminator is BEL rather than ESC-backslash: both are legal, and BEL is the form every
    // terminal that implements these at all has always accepted.
    private const string SetForeground = "\u001b]10;{0}\u0007";
    private const string SetBackground = "\u001b]11;{0}\u0007";
    private const string ResetForeground = "\u001b]110\u0007";
    private const string ResetBackground = "\u001b]111\u0007";

    /// <summary>Erase-in-line: fills from the cursor to the end of the line with the current background.</summary>
    private const string EraseToEndOfLine = "\u001b[K";

    /// <summary>Reset every SGR attribute — back to the frame's own colours, once <see cref="Apply"/> has run.</summary>
    private const string ResetAttributes = "\u001b[0m";

    private bool _applied;

    /// <inheritdoc/>
    public bool IsAnsiEnabled { get; private set; }

    /// <inheritdoc/>
    public void Apply(ConsoleRgbColor background, ConsoleRgbColor foreground)
    {
        IsAnsiEnabled = TryEnableVirtualTerminal();

        try
        {
            // the legacy attributes are set either way: they are the whole frame on a console without
            // ANSI, and on one with it they keep Console's own idea of the colours consistent with the
            // terminal defaults for anything that reads them back.
            Console.BackgroundColor = background.ToNearestConsoleColor();
            Console.ForegroundColor = foreground.ToNearestConsoleColor();

            if (IsAnsiEnabled)
            {
                Console.Out.Write(string.Format(SetBackground, background));
                Console.Out.Write(string.Format(SetForeground, foreground));
            }

            Console.Clear();
            _applied = true;
        }
        catch (IOException)
        {
            // no console to paint (redirected, or none attached at all) - not fatal, nothing renders.
        }
    }

    /// <inheritdoc/>
    public void WriteArt(string art, ConsoleRgbColor color)
    {
        if (!IsAnsiEnabled)
        {
            WriteArtWithConsoleAttributes(art, color);
            return;
        }

        var writer = Console.Out;
        foreach (var line in art.Split('\n'))
        {
            writer.Write($"\u001b[38;2;{color.R};{color.G};{color.B}m");
            writer.Write(line.TrimEnd('\r'));
            writer.Write(EraseToEndOfLine);
            writer.WriteLine(ResetAttributes);
        }
    }

    private static void WriteArtWithConsoleAttributes(string art, ConsoleRgbColor color)
    {
        var previous = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color.ToNearestConsoleColor();
            Console.Out.WriteLine(art);
        }
        catch (IOException)
        {
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }

    /// <inheritdoc/>
    public void Restore()
    {
        if (!_applied)
        {
            return;
        }
        _applied = false;

        try
        {
            if (IsAnsiEnabled)
            {
                Console.Out.Write(ResetBackground);
                Console.Out.Write(ResetForeground);
                Console.Out.Write(ResetAttributes);
            }
            Console.ResetColor();
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Whether the console can be written to with escape sequences: not redirected, and — on Windows,
    /// where it is opt-in per console handle — actually switched into virtual-terminal mode.
    /// </summary>
    private static bool TryEnableVirtualTerminal()
    {
        if (Console.IsOutputRedirected)
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            // everywhere else the terminal either speaks ANSI or advertises that it does not.
            return !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            var handle = NativeMethods.GetStdHandle(NativeMethods.StdOutputHandle);
            if (handle == nint.Zero || handle == NativeMethods.InvalidHandleValue
                || !NativeMethods.GetConsoleMode(handle, out var mode))
            {
                return false;
            }

            return (mode & NativeMethods.EnableVirtualTerminalProcessing) != 0
                || NativeMethods.SetConsoleMode(handle, mode | NativeMethods.EnableVirtualTerminalProcessing);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static class NativeMethods
    {
        internal const int StdOutputHandle = -11;
        internal const uint EnableVirtualTerminalProcessing = 0x0004;
        internal static readonly nint InvalidHandleValue = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
    }
}
