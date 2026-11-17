using System.Runtime.InteropServices;

namespace RDCore.Runtime.Execution.External.Application;

/// <summary>
/// The operating system's identifier of the thread that asks: <c>App.ThreadID</c>, of the process the program runs in.
/// </summary>
internal static class OperatingSystemThread
{
    /// <summary>
    /// The identifier of the calling thread as the operating system numbers it; <see langword="null"/> on an operating system that gives none here.
    /// </summary>
    public static long? CurrentId()
    {
        if (OperatingSystem.IsWindows())
        {
            return GetCurrentThreadId();
        }

        if (OperatingSystem.IsLinux())
        {
            return gettid();
        }

        if (OperatingSystem.IsMacOS() && pthread_threadid_np(IntPtr.Zero, out var id) == 0)
        {
            return (long)id;
        }

        return null;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("libc")]
    private static extern int gettid();

    [DllImport("libc")]
    private static extern int pthread_threadid_np(IntPtr thread, out ulong id);
}
