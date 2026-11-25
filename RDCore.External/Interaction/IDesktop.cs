using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.External.Interaction;

/// <summary>
/// The desktop a program runs on: its message queue, its speaker, the windows of other applications and the processes it starts.
/// </summary>
/// <remarks>
/// Only Windows has one the language knows how to reach. Elsewhere each of these does nothing (<see cref="NoDesktop"/>), which is not an error: a <c>Beep</c> with no
/// speaker, or a <c>DoEvents</c> with no message queue, has nothing to do.
/// </remarks>
public interface IDesktop
{
    /// <summary>
    /// Processes the messages that wait in the message queue of the thread the program runs on: <c>DoEvents</c>.
    /// </summary>
    void DoEvents();

    /// <summary>
    /// Sounds a tone: <c>Beep</c>.
    /// </summary>
    void Beep();

    /// <summary>
    /// Activates the application window whose title is <paramref name="title"/> - or, with no exact match, whose title begins with it - without changing whether it
    /// is minimized or maximized: <c>AppActivate</c>.
    /// </summary>
    /// <param name="title">The title, compared without regard to case.</param>
    /// <param name="wait">Whether to wait for the program to have the focus first.</param>
    /// <returns><see langword="false"/> when no window has that title.</returns>
    bool AppActivate(string title, bool wait);

    /// <summary>
    /// Activates a window of the process <paramref name="processId"/> - the task ID <see cref="Shell"/> returns.
    /// </summary>
    /// <param name="processId">The process.</param>
    /// <param name="wait">Whether to wait for the program to have the focus first.</param>
    /// <returns><see langword="false"/> when the process has no window.</returns>
    bool AppActivate(int processId, bool wait);

    /// <summary>
    /// Sends keystrokes to the active window, as if typed at the keyboard: <c>SendKeys</c>.
    /// </summary>
    /// <param name="keys">The keystrokes, in the syntax of <c>SendKeys</c> (<see cref="SendKeysParser"/>).</param>
    /// <param name="wait">Whether the keystrokes are processed before this returns.</param>
    /// <exception cref="FormatException"><paramref name="keys"/> is not in the syntax of <c>SendKeys</c>.</exception>
    void SendKeys(string keys, bool wait);

    /// <summary>
    /// Starts a program, and does not wait for it: <c>Shell</c>.
    /// </summary>
    /// <param name="pathName">The program, and the arguments it is given: a command line.</param>
    /// <param name="windowStyle">How the window of the program is shown.</param>
    /// <param name="workingDirectory">
    /// The directory the program starts in: the current directory of the session that starts it, or <see langword="null"/> for the process's.
    /// </param>
    /// <returns>The ID of the process that was started, or <c>0</c> when nothing was.</returns>
    /// <exception cref="FileNotFoundException">There is no such program.</exception>
    int Shell(string pathName, VBAppWinStyle windowStyle, string? workingDirectory);
}

/// <summary>
/// The desktop of a platform the language has none of: everything is done, and nothing happens.
/// </summary>
/// <remarks>
/// <c>Shell</c> starts nothing and answers <c>0</c>, <c>AppActivate</c> finds the window it is asked for - there being none to say otherwise of - and <c>SendKeys</c>
/// does not even read its keystrokes: there is nowhere they could go.
/// </remarks>
public sealed class NoDesktop : IDesktop
{
    /// <summary>The one there is: it has nothing to hold.</summary>
    public static NoDesktop Instance { get; } = new();

    private NoDesktop() { }

    /// <inheritdoc/>
    public void DoEvents() { }

    /// <inheritdoc/>
    public void Beep() { }

    /// <inheritdoc/>
    public bool AppActivate(string title, bool wait) => true;

    /// <inheritdoc/>
    public bool AppActivate(int processId, bool wait) => true;

    /// <inheritdoc/>
    public void SendKeys(string keys, bool wait) { }

    /// <inheritdoc/>
    public int Shell(string pathName, VBAppWinStyle windowStyle, string? workingDirectory) => 0;
}
