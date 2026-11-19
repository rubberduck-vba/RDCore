using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// The window of a class and a caption, once it is shown, in this process or in another: Windows says a window is shown (<c>EVENT_OBJECT_SHOW</c>) to a hook
/// set on a thread of the waiter's own, which pumps the messages the hook is told by. A window shown before the hook was set is found once it is.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowShown
{
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;

    /// <summary>
    /// Completed with the window's handle when a window of <paramref name="className"/> captioned <paramref name="caption"/> is shown.
    /// </summary>
    public static Task<IntPtr> WaitAsync(string className, string caption)
    {
        var shown = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Pump(className, caption, shown)) { IsBackground = true, Name = "WindowShown" };
        thread.Start();
        return shown.Task;
    }

    private static void Pump(string className, string caption, TaskCompletionSource<IntPtr> shown)
    {
        void Found(IntPtr window)
        {
            if (shown.TrySetResult(window))
            {
                PostQuitMessage(0);
            }
        }

        WinEventProc told = (_, _, window, objectId, childId, _, _) =>
        {
            if (objectId == OBJID_WINDOW && childId == CHILDID_SELF && Is(window, className, caption))
            {
                Found(window);
            }
        };

        var hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, told, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (hook == IntPtr.Zero)
        {
            shown.TrySetException(new InvalidOperationException($"SetWinEventHook failed ({Marshal.GetLastPInvokeError()})."));
            return;
        }

        try
        {
            // the hook is set first, so that a window shown since is told of, and one shown before is found here.
            if (FindWindow(className, caption) is var existing && existing != IntPtr.Zero && IsWindowVisible(existing))
            {
                Found(existing);
            }

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }
        }
        finally
        {
            _ = UnhookWinEvent(hook);
            GC.KeepAlive(told);
        }
    }

    private static bool Is(IntPtr window, string className, string caption)
    {
        var text = new StringBuilder(512);
        if (GetClassName(window, text, text.Capacity) == 0 || text.ToString() != className)
        {
            return false;
        }

        text.Clear();
        _ = GetWindowText(window, text, text.Capacity);
        return text.ToString() == caption;
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Value;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr window, uint first, uint last);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string title);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
}
