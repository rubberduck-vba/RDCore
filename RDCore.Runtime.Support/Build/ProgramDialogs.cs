namespace RDCore.Runtime.Support.Build;

// who answers the MsgBox and InputBox of a program rdc runs or builds: its console, on every operating system, unless the settings ask for the machine's
// own dialogs, which only Windows has.
internal static class ProgramDialogs
{
    // whether the program shows the machine's own dialogs: asked for (native), and on a machine that has them.
    public static bool ShowsNative(bool native) => native && OperatingSystem.IsWindows();
}
