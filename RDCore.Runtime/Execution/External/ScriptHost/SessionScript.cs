using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Execution.External.ScriptHost;

/// <summary>
/// What Windows Script Host's <c>WScript</c> object says of the script a session runs: its file, the arguments it was started with, and where its
/// <c>StdErr</c> goes.
/// </summary>
/// <remarks>
/// The host that composes the session says which script it runs (<see cref="Runs"/>). The objects <c>WScript</c> gives are the session's: the same object
/// each time it is asked for, as long as the program holds it.
/// </remarks>
public sealed class SessionScript
{
    private static readonly ConditionalWeakTable<IRuntimeSession, SessionScript> Sessions = [];

    private readonly object _gate = new();
    private readonly Dictionary<ScriptObjectKind, VBRuntimeObjectId> _objects = [];

    /// <summary>
    /// The script of <paramref name="session"/>, made the first time it is asked for.
    /// </summary>
    /// <param name="session">The session the script runs in.</param>
    public static SessionScript Of(IRuntimeSession session) => Sessions.GetValue(session, _ => new SessionScript());

    /// <summary>
    /// Says that the session runs the script <paramref name="fullName"/>, started with <paramref name="arguments"/>.
    /// </summary>
    /// <param name="fullName">The full path of the script's file.</param>
    /// <param name="arguments">The arguments the script was started with, in order.</param>
    /// <param name="error">Where <c>WScript.StdErr</c> writes.</param>
    public void Runs(string fullName, IReadOnlyList<string> arguments, TextWriter error)
    {
        lock (_gate)
        {
            FullName = fullName;
            Arguments = [.. arguments];
            Error = error;
        }
    }

    /// <summary>
    /// The full path of the script's file: <c>WScript.ScriptFullName</c>. Empty until <see cref="Runs"/> says.
    /// </summary>
    public string FullName { get; private set; } = string.Empty;

    /// <summary>
    /// The arguments the script was started with, named and unnamed, in order: <c>WScript.Arguments</c>.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; private set; } = [];

    /// <summary>
    /// Where <c>WScript.StdErr</c> writes: nowhere until <see cref="Runs"/> says.
    /// </summary>
    public TextWriter Error { get; private set; } = TextWriter.Null;

    /// <summary>
    /// What the program assigned to <c>WScript.Interactive</c>; <see langword="null"/> until it assigns it, while <c>WScript.Interactive</c> is whether the
    /// session runs attended. Assigning it changes nothing else.
    /// </summary>
    public bool? Interactive { get; set; }

    // the object of each kind that was given last, which is given again while the program holds it.
    internal bool TryGetObject(ScriptObjectKind kind, out VBRuntimeObjectId identity)
    {
        lock (_gate)
        {
            return _objects.TryGetValue(kind, out identity);
        }
    }

    internal void Gave(ScriptObjectKind kind, VBRuntimeObjectId identity)
    {
        lock (_gate)
        {
            _objects[kind] = identity;
        }
    }

    internal void Released(ScriptObjectKind kind)
    {
        lock (_gate)
        {
            _objects.Remove(kind);
        }
    }
}

// the objects of a script's host: WScript, its Arguments, their Named and Unnamed views, StdOut and StdErr.
internal enum ScriptObjectKind
{
    Host,
    Arguments,
    Named,
    Unnamed,
    StdOut,
    StdErr,
}

// what the platform holds for each of those objects.
internal sealed record class ScriptObject(SessionScript Script, ScriptObjectKind Kind);
