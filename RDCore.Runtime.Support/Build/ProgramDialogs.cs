using RDCore.SDK.Workspace;

namespace RDCore.Runtime.Support.Build;

// who answers the MsgBox and InputBox of a program rdc runs or builds. A console application's console does, on every operating system. An RDForms
// application gets the machine's own dialogs, which only Windows has, and its console elsewhere: RDForms has no dialogs of its own. The settings can ask
// for the machine's own dialogs for a console application too.
internal static class ProgramDialogs
{
    // whether the project is a console application, whose console is its user interface: the one place each choice of dialogs reads it from.
    public static bool IsConsole(ProjectBuild? build) => build?.IsConsoleApplication == true;

    // whether the program is to show the machine's own dialogs where it has them: an RDForms application, or one whose settings ask for them (native).
    public static bool WantsNative(ProjectBuild? build, bool native) => native || !IsConsole(build);

    // whether the program shows the machine's own dialogs: wanted (native), and on a machine that has them.
    public static bool ShowsNative(bool native) => native && OperatingSystem.IsWindows();
}
