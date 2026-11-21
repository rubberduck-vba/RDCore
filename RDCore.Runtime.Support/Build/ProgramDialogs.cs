using RDCore.External.Interaction;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Workspace;

namespace RDCore.Runtime.Support.Build;

// who answers the MsgBox and InputBox of a program rdc runs or builds, the same for both. An RDForms application's are RDForms', over its forms, and a
// console application's are asked at its console, on every operating system. The settings can ask for the machine's own dialogs instead (native), which
// only Windows has: elsewhere they change nothing. A built program keeps what the settings said when it was built.
internal static class ProgramDialogs
{
    // whether the project is a console application, whose console is its user interface: the one place each choice of dialogs reads it from.
    public static bool IsConsole(ProjectBuild? build) => build?.IsConsoleApplication == true;

    // whether the program shows the machine's own dialogs: asked for (native), and on a machine that has them.
    public static bool ShowsNative(bool native) => ShowsNative(native, OperatingSystem.IsWindows());

    // whether the program shows the machine's own dialogs on a machine that is Windows, or is not.
    public static bool ShowsNative(bool native, bool windows) => native && windows;

    // who answers the dialogs of a program: the machine when its own dialogs are asked for (native) and the machine has them; RDForms otherwise for an
    // RDForms application, and the console for a console application.
    public static DialogSource SourceOf(ProjectBuild? build, bool native) => SourceOf(build, native, OperatingSystem.IsWindows());

    // who answers the dialogs of a program on a machine that is Windows, or is not.
    public static DialogSource SourceOf(ProjectBuild? build, bool native, bool windows)
        => ShowsNative(native, windows) ? DialogSource.Machine : IsConsole(build) ? DialogSource.Console : DialogSource.Forms;

    // the dialogs of a program, as SourceOf says: null for the machine's own, which the machine's outside world shows already. An RDForms application's are
    // shown by the external host over its forms, and by the machine, or the console where the machine has none, when the external host shows none.
    public static IUserInteraction? DialogsOf(ProjectBuild? build, bool native, Func<IUserInteraction> console) => SourceOf(build, native) switch
    {
        DialogSource.Forms => MachineExternal.FormDialogs(ShowsNative(native: true) ? MachinePlatform.Interaction : console()),
        DialogSource.Machine => null,
        _ => console(),
    };
}

// who answers the dialogs of a program.
internal enum DialogSource
{
    // the console the program runs in.
    Console,

    // the machine's own.
    Machine,

    // RDForms', over the program's forms in the external host.
    Forms,
}
