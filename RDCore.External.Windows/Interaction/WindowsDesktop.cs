using RDCore.External.Interaction;
using RDCore.SDK.Runtime.Abstract.StdLib;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace RDCore.External.Windows.Interaction;

/// <summary>
/// The desktop of Windows: the message queue of the thread the program runs on, the system's sound, the top-level windows of other applications, the keyboard input
/// stream, and <c>CreateProcess</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktop : IDesktop
{
    private const int ERROR_FILE_NOT_FOUND = 2;
    private const int ERROR_PATH_NOT_FOUND = 3;

    /// <inheritdoc/>
    /// <remarks>Every message that waits in the queue of the calling thread is dispatched, as a message loop would.</remarks>
    public void DoEvents()
    {
        while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, PM_REMOVE))
        {
            _ = TranslateMessage(ref message);
            _ = DispatchMessageW(ref message);
        }
    }

    /// <inheritdoc/>
    public void Beep() => _ = MessageBeep(MB_OK);

    /// <inheritdoc/>
    public bool AppActivate(string title, bool wait)
    {
        var windows = TopLevelWindows();
        var target = windows.FirstOrDefault(window => string.Equals(window.Title, title, StringComparison.OrdinalIgnoreCase))
            ?? windows.FirstOrDefault(window => window.Title.StartsWith(title, StringComparison.OrdinalIgnoreCase));
        return target is not null && Activate(target.Handle);
    }

    /// <inheritdoc/>
    public bool AppActivate(int processId, bool wait)
        => TopLevelWindows().FirstOrDefault(window => window.ProcessId == processId) is { } target && Activate(target.Handle);

    // "without affecting whether it is maximized or minimized": it is brought to the foreground as it is.
    private static bool Activate(IntPtr window)
    {
        _ = SetForegroundWindow(window);
        return true;
    }

    private sealed record class TopLevelWindow(IntPtr Handle, string Title, int ProcessId);

    private static List<TopLevelWindow> TopLevelWindows()
    {
        var windows = new List<TopLevelWindow>();
        _ = EnumWindows((window, parameter) =>
        {
            var length = GetWindowTextLengthW(window);
            if (!IsWindowVisible(window) || length == 0)
            {
                return true;
            }

            var title = new StringBuilder(length + 1);
            _ = GetWindowTextW(window, title, title.Capacity);
            _ = GetWindowThreadProcessId(window, out var processId);
            windows.Add(new TopLevelWindow(window, title.ToString(), (int)processId));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    /// <inheritdoc/>
    public void SendKeys(string keys, bool wait)
    {
        var inputs = new List<INPUT>();
        foreach (var stroke in SendKeysParser.Parse(keys))
        {
            Append(inputs, stroke);
        }

        if (inputs.Count > 0 && SendInput((uint)inputs.Count, [.. inputs], Marshal.SizeOf<INPUT>()) != inputs.Count)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (wait)
        {
            DoEvents();
        }
    }

    // a character with no modifier is typed as itself, whatever the keyboard layout; anything else is the key that types it, with the modifiers held down around it.
    private static void Append(List<INPUT> inputs, KeyStroke stroke)
    {
        if (stroke.Key is null && stroke.Modifiers == KeyModifiers.None)
        {
            inputs.Add(Unicode(stroke.Character, up: false));
            inputs.Add(Unicode(stroke.Character, up: true));
            return;
        }

        var modifiers = stroke.Modifiers;
        ushort key;
        if (stroke.Key is { } name)
        {
            key = VirtualKeys[name];
        }
        else
        {
            var scan = VkKeyScanW(stroke.Character);
            if (scan == -1)
            {
                throw new FormatException($"'{stroke.Character}' is typed by no key of the keyboard.");
            }

            key = (ushort)(scan & 0xFF);
            var shiftState = (scan >> 8) & 0xFF;
            modifiers |= ((shiftState & 1) != 0 ? KeyModifiers.Shift : 0) | ((shiftState & 2) != 0 ? KeyModifiers.Control : 0) | ((shiftState & 4) != 0 ? KeyModifiers.Alt : 0);
        }

        ushort[] held = [.. Modifier(modifiers, KeyModifiers.Shift, VK_SHIFT), .. Modifier(modifiers, KeyModifiers.Control, VK_CONTROL), .. Modifier(modifiers, KeyModifiers.Alt, VK_MENU)];
        inputs.AddRange(held.Select(modifier => Key(modifier, up: false)));
        inputs.Add(Key(key, up: false));
        inputs.Add(Key(key, up: true));
        inputs.AddRange(held.Reverse().Select(modifier => Key(modifier, up: true)));
    }

    private static IEnumerable<ushort> Modifier(KeyModifiers modifiers, KeyModifiers modifier, ushort key)
        => modifiers.HasFlag(modifier) ? [key] : [];

    private static INPUT Key(ushort key, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = key, dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (ExtendedKeys.Contains(key) ? KEYEVENTF_EXTENDEDKEY : 0) },
        },
    };

    private static INPUT Unicode(char character, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wScan = character, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } },
    };

    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;

    private static readonly Dictionary<string, ushort> VirtualKeys = BuildVirtualKeys();

    private static Dictionary<string, ushort> BuildVirtualKeys()
    {
        var keys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["BACKSPACE"] = 0x08, ["BREAK"] = 0x03, ["CAPSLOCK"] = 0x14, ["DELETE"] = 0x2E, ["DOWN"] = 0x28, ["END"] = 0x23, ["ENTER"] = 0x0D,
            ["ESC"] = 0x1B, ["HELP"] = 0x2F, ["HOME"] = 0x24, ["INSERT"] = 0x2D, ["LEFT"] = 0x25, ["NUMLOCK"] = 0x90, ["PGDN"] = 0x22, ["PGUP"] = 0x21,
            ["PRTSC"] = 0x2C, ["RIGHT"] = 0x27, ["SCROLLLOCK"] = 0x91, ["TAB"] = 0x09, ["UP"] = 0x26, ["ADD"] = 0x6B, ["SUBTRACT"] = 0x6D,
            ["MULTIPLY"] = 0x6A, ["DIVIDE"] = 0x6F,
        };

        for (var function = 1; function <= 16; function++)
        {
            keys[$"F{function}"] = (ushort)(0x70 + function - 1);
        }

        return keys;
    }

    // the keys of the navigation block and the keypad's divide and num lock are extended keys: without the flag, an arrow is a keypad digit.
    private static readonly HashSet<ushort> ExtendedKeys = [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x6F, 0x90];

    /// <inheritdoc/>
    /// <remarks>
    /// The command line is <c>CreateProcess</c>'s to read, as MS-VBA's <c>Shell</c> gives it: a program path with spaces in it and no quotes around it is found the way
    /// Windows finds it.
    /// </remarks>
    public int Shell(string pathName, VBAppWinStyle windowStyle, string? workingDirectory)
    {
        var startup = new STARTUPINFOW
        {
            cb = Marshal.SizeOf<STARTUPINFOW>(),
            dwFlags = STARTF_USESHOWWINDOW,
            wShowWindow = ShowCommand(windowStyle),
        };

        // CreateProcessW may write to the command line it is given, which is why it is a buffer and not a string.
        var commandLine = new StringBuilder(pathName);
        if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, workingDirectory, ref startup, out var process))
        {
            var error = Marshal.GetLastWin32Error();
            throw error is ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND
                ? new FileNotFoundException($"'{pathName}' is not a program that can be run.", pathName)
                : new Win32Exception(error);
        }

        _ = CloseHandle(process.hThread);
        _ = CloseHandle(process.hProcess);
        return process.dwProcessId;
    }

    // the values of VbAppWinStyle are those of SW_*, but for vbMinimizedNoFocus: 6 is SW_MINIMIZE, which activates the next window, and the style is SW_SHOWMINNOACTIVE.
    private static short ShowCommand(VBAppWinStyle style) => style switch
    {
        VBAppWinStyle.VBHide => 0,
        VBAppWinStyle.VBNormalFocus => 1,
        VBAppWinStyle.VBMinimizedFocus => 2,
        VBAppWinStyle.VBMaximizedFocus => 3,
        VBAppWinStyle.VBNormalNoFocus => 4,
        VBAppWinStyle.VBMinimizedNoFocus => 7,
        _ => 1,
    };

    private const uint PM_REMOVE = 0x1;
    private const uint MB_OK = 0x0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1;
    private const uint KEYEVENTF_KEYUP = 0x2;
    private const uint KEYEVENTF_UNICODE = 0x4;
    private const int STARTF_USESHOWWINDOW = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOW
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool PeekMessageW(out MSG message, IntPtr window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32", ExactSpelling = true)]
    private static extern IntPtr DispatchMessageW(ref MSG message);

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool MessageBeep(uint type);

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32", ExactSpelling = true)]
    private static extern int GetWindowTextLengthW(IntPtr window);

    [DllImport("user32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32", ExactSpelling = true, SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern short VkKeyScanW(char character);

    [DllImport("kernel32", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool CreateProcessW(
        string? applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
        IntPtr environment, string? currentDirectory, ref STARTUPINFOW startupInfo, out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32", ExactSpelling = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
