using RDCore.External.Interaction;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Workspace;

namespace RDCore.Runtime.Support.Build;

// who answers the MsgBox and InputBox of a program rdc runs or builds, the same for both. A console application's console does, on every operating system,
// unless the settings ask for the machine's own dialogs, which only Windows has. An RDForms application's are RDForms', over its forms.
internal static class ProgramDialogs
{
    // whether the project is a console application, whose console is its user interface: the one place each choice of dialogs reads it from.
    public static bool IsConsole(ProjectBuild? build) => build?.IsConsoleApplication == true;

    // whether the program is to show the machine's own dialogs where it has them: an RDForms application, or one whose settings ask for them (native). What a
    // built program keeps of the settings.
    public static bool WantsNative(ProjectBuild? build, bool native) => native || !IsConsole(build);

    // whether the program shows the machine's own dialogs: wanted (native), and on a machine that has them.
    public static bool ShowsNative(bool native) => native && OperatingSystem.IsWindows();

    // who answers the dialogs of a program: an RDForms application's forms, and a console application's machine when its own dialogs are wanted (native) and
    // the machine has them, its console otherwise.
    public static DialogSource SourceOf(ProjectBuild? build, bool native)
        => !IsConsole(build) ? DialogSource.Forms : ShowsNative(native) ? DialogSource.Machine : DialogSource.Console;

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
