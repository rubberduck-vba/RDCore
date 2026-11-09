using System.Runtime.InteropServices;

namespace RDCore.CLI.ConsoleIO;

// setting Console.BackgroundColor and clearing is not enough: every renderer that writes SGR sequences (Spectre.Console among them) ends a styled span
// with a reset (ESC[0m), which goes back to the terminal's default colours, and .NET does not write again an attribute it believes is set. Making the
// frame's colours the terminal's defaults (OSC 10/11) lands each reset back on the frame. A terminal that cannot gets the nearest 16-colour frame, and
// Restore must run before the process exits to give the terminal back as it was found.
public interface IConsoleShellFrame
{
    bool IsAnsiEnabled { get; }

    void Apply(ConsoleRgbColor background, ConsoleRgbColor foreground);

    void WriteArt(string art, ConsoleRgbColor color);

    void Restore();
}

public sealed class ConsoleShellFrame : IConsoleShellFrame
{
    // OSC 10/11 set the terminal's default foreground/background; OSC 110/111 restore them. The
    // string terminator is BEL rather than ESC-backslash: both are legal, and BEL is the form every
    // terminal that implements these at all has always accepted.
    private const string SetForeground = "\u001b]10;{0}\u0007";
    private const string SetBackground = "\u001b]11;{0}\u0007";
    private const string ResetForeground = "\u001b]110\u0007";
    private const string ResetBackground = "\u001b]111\u0007";

    private const string EraseToEndOfLine = "\u001b[K";

    private const string ResetAttributes = "\u001b[0m";

    private bool _applied;

    public bool IsAnsiEnabled { get; private set; }

    public void Apply(ConsoleRgbColor background, ConsoleRgbColor foreground)
    {
        IsAnsiEnabled = TryEnableVirtualTerminal();

        try
        {
            if (IsAnsiEnabled)
            {
                // The legacy attributes must stay unset here: Console writes a set attribute out explicitly with the text it writes, as the
                // palette entry nearest to the colour (dark blue for the C64 blue, bright white for white), which paints the text on a
                // background that is not the frame's - exactly where there is text, and nowhere else.
                Console.ResetColor();
                Console.Out.Write(string.Format(SetBackground, background));
                Console.Out.Write(string.Format(SetForeground, foreground));
            }
            else
            {
                // without ANSI the legacy attributes are the whole frame.
                Console.BackgroundColor = background.ToNearestConsoleColor();
                Console.ForegroundColor = foreground.ToNearestConsoleColor();
            }

            Console.Clear();
            _applied = true;
        }
        catch (IOException)
        {
            // no console to paint (redirected, or none attached at all) - not fatal, nothing renders.
        }
    }

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
            // the rest of a short line in the frame's background, not a tail of another colour.
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

    // on Windows, virtual-terminal mode is opt-in for each console handle.
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
