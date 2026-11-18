using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Execution.External.Application;

/// <summary>
/// What Visual Basic 6.0's <c>App</c> object says of the program a session runs: where it is, its name, and the properties a program sets on it.
/// </summary>
/// <remarks>
/// The host that composes the session says where the program is (<see cref="Runs"/>, <see cref="Built"/>). What the program assigns to <c>App</c> is kept
/// by the session, which outlives the pipelines that run its programs, until the session is composed again.
/// </remarks>
public sealed class SessionApplication
{
    private static readonly ConditionalWeakTable<IRuntimeSession, SessionApplication> Sessions = [];

    private readonly object _gate = new();
    private string _title = string.Empty;

    /// <summary>
    /// The application of <paramref name="session"/>, made the first time it is asked for.
    /// </summary>
    /// <param name="session">The session the program runs in.</param>
    public static SessionApplication Of(IRuntimeSession session) => Sessions.GetValue(session, _ => new SessionApplication());

    /// <summary>
    /// Says that the session runs a workspace's program, as <c>rdc run</c> runs it: from the folder of its project, under the project's name.
    /// </summary>
    /// <param name="projectName">The name of the workspace project.</param>
    /// <param name="folder">The folder of the workspace project's <c>.rdproj</c>.</param>
    public void Runs(string projectName, string folder)
    {
        lock (_gate)
        {
            Folder = Trimmed(folder);
            ExecutableName = projectName;
            _title = projectName;
            PrevInstance = false;
        }
    }

    /// <summary>
    /// Says that the session runs a program <c>rdc build</c> built, from its executable.
    /// </summary>
    /// <param name="projectName">The name of the project the program was built from.</param>
    /// <param name="executable">The full path of the program's executable.</param>
    /// <param name="prevInstance">Whether another instance of the same executable was running when this one started.</param>
    public void Built(string projectName, string executable, bool prevInstance)
    {
        lock (_gate)
        {
            Folder = Trimmed(System.IO.Path.GetDirectoryName(executable) ?? string.Empty);
            ExecutableName = System.IO.Path.GetFileNameWithoutExtension(executable);
            _title = projectName;
            PrevInstance = prevInstance;
        }
    }

    /// <summary>
    /// The folder the program runs from: <c>App.Path</c>.
    /// </summary>
    public string Folder { get; private set; } = string.Empty;

    /// <summary>
    /// The name of the program's executable, without its extension: <c>App.EXEName</c>.
    /// </summary>
    public string ExecutableName { get; private set; } = string.Empty;

    /// <summary>
    /// Whether another instance of the program's executable was running when it started: <c>App.PrevInstance</c>.
    /// </summary>
    public bool PrevInstance { get; private set; }

    /// <summary>
    /// <c>App.Title</c>: the project's name, until the program assigns another.
    /// </summary>
    public string Title
    {
        get
        {
            lock (_gate)
            {
                return _title;
            }
        }
        set
        {
            lock (_gate)
            {
                _title = value;
            }
        }
    }

    /// <summary>
    /// <c>App.TaskVisible</c>, which changes nothing the platform shows.
    /// </summary>
    public bool TaskVisible { get; set; } = true;

    /// <summary>
    /// <c>App.KeepSystemTheme</c>: whether the program's forms follow the system's theme, or stay light. Its value starts as the project's, and the form host
    /// is told each value it takes.
    /// </summary>
    public bool KeepSystemTheme { get; set; }

    /// <summary>
    /// <c>App.OleServerBusyTimeout</c>, in milliseconds, which no automation call waits for.
    /// </summary>
    public int OleServerBusyTimeout { get; set; } = 10000;

    /// <summary>
    /// <c>App.OleRequestPendingTimeout</c>, in milliseconds, which no automation call waits for.
    /// </summary>
    public int OleRequestPendingTimeout { get; set; } = 5000;

    /// <summary>
    /// <c>App.OleServerBusyRaiseError</c>, which no automation call reads.
    /// </summary>
    public bool OleServerBusyRaiseError { get; set; }

    /// <summary>
    /// <c>App.OleServerBusyMsgTitle</c>: <see cref="Title"/> until the program assigns one.
    /// </summary>
    public string? OleServerBusyMessageTitle { get; set; }

    /// <summary>
    /// <c>App.OleServerBusyMsgText</c>, empty until the program assigns one.
    /// </summary>
    public string OleServerBusyMessageText { get; set; } = string.Empty;

    /// <summary>
    /// <c>App.OleRequestPendingMsgTitle</c>: <see cref="Title"/> until the program assigns one.
    /// </summary>
    public string? OleRequestPendingMessageTitle { get; set; }

    /// <summary>
    /// <c>App.OleRequestPendingMsgText</c>, empty until the program assigns one.
    /// </summary>
    public string OleRequestPendingMessageText { get; set; } = string.Empty;

    // the one App object of the session, while something holds it.
    internal VBRuntimeObjectId? Object { get; set; }

    // App.Path has no separator at its end, unless it is the root of a drive or of the file system.
    private static string Trimmed(string folder) => System.IO.Path.TrimEndingDirectorySeparator(folder);
}
