using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// What the shell does next once a command has run.
/// </summary>
public enum ReplCommandResult
{
    /// <summary>Print the ready banner and prompt again.</summary>
    Continue,

    /// <summary>End the session and exit the process.</summary>
    Exit,
}

/// <summary>
/// The interactive shell's console, as a command sees it.
/// </summary>
/// <remarks>
/// Plain lines are the shell's own voice — program output, listings, the ready banner — and are
/// written unstyled so they take the shell frame's colours. A <see cref="WriteMessage"/> is the
/// platform speaking: an error, a warning, something the theme should mark.
/// </remarks>
public interface IReplConsole
{
    /// <summary>Writes one plain line in the shell's own colours.</summary>
    /// <param name="text">The line; empty writes a blank line.</param>
    void WriteLine(string text = "");

    /// <summary>Writes a themed platform message.</summary>
    /// <param name="kind">The message kind, which selects the icon and accent style.</param>
    /// <param name="message">The message body.</param>
    /// <param name="verbose">An optional second, dimmer line of detail.</param>
    void WriteMessage(MessageKind kind, string message, string? verbose = null);
}

/// <summary>
/// The platform, as the interactive shell sees it: one language server, reached over LSP, and
/// whatever it told the shell it can provide.
/// </summary>
/// <remarks>
/// The shell is an LSP client and nothing more. It does not know that a parser or an environment
/// host exist, let alone address one — every capability it uses is one the language server answered
/// for in the platform handshake, and a command that needs one it did not get says so instead of
/// failing obscurely.
/// </remarks>
public interface IReplPlatformClient
{
    /// <summary>
    /// Whether the language server told this client, in the platform handshake, that it provides
    /// <typeparamref name="TCapability"/>.
    /// </summary>
    /// <typeparam name="TCapability">The capability to test for.</typeparam>
    bool Provides<TCapability>() where TCapability : CorePlatformClientCapability;

    /// <summary>
    /// Asks the language server for the state of the platform's runtime session.
    /// </summary>
    /// <param name="waitMilliseconds">How long the language server may wait for the session to come up; <c>0</c> answers immediately.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<SessionStatusResult> GetSessionStatusAsync(int waitMilliseconds, CancellationToken token);

    /// <summary>
    /// Asks the language server to run one procedure of a module.
    /// </summary>
    /// <param name="source">The complete module source.</param>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="entryPoint">The parameterless procedure to invoke.</param>
    /// <param name="token">
    /// Cancelling this cancels the run itself, all the way into the interpreter loop — it is what a
    /// break at the keyboard turns into.
    /// </param>
    Task<ExecuteSessionResult> ExecuteAsync(string source, string moduleName, string entryPoint, CancellationToken token);
}

/// <summary>
/// Everything a shell command acts on. Deliberately a growable type rather than a parameter list:
/// a later command needing a new fact gets it added here, without every command's signature moving.
/// </summary>
/// <param name="Program">The program buffer the numbered lines are kept in.</param>
/// <param name="Console">The shell's console.</param>
/// <param name="Platform">The language server this shell is a client of.</param>
/// <param name="Commands">
/// Every command the shell offers. Carried here rather than injected into the one command that needs
/// it — <c>HELP</c> is itself in the list, so asking the container for the list while building it is
/// a cycle.
/// </param>
public sealed record class ReplCommandContext(
    ReplProgram Program,
    IReplConsole Console,
    IReplPlatformClient Platform,
    IReadOnlyList<IReplCommand> Commands);

/// <summary>
/// A command of the interactive shell — <c>LIST</c>, <c>RUN</c>, <c>EXIT</c> — matched before the
/// line is treated as VBA.
/// </summary>
/// <remarks>
/// The sibling of <c>ICliCommand</c>, which is explicitly command-mode only: no workspace, no LSP
/// connection, one shot, an exit code. A shell command is the opposite on every count — it runs
/// inside a live, connected session and acts on it — so it shares the shape and nothing else.
/// </remarks>
public interface IReplCommand
{
    /// <summary>The command's primary name, as <c>HELP</c> lists it. Matched case-insensitively.</summary>
    string Name { get; }

    /// <summary>Alternative spellings that resolve to this command.</summary>
    IReadOnlyList<string> Aliases { get; }

    /// <summary>A one-line description, for <c>HELP</c>.</summary>
    string Summary { get; }

    /// <summary>
    /// Runs the command.
    /// </summary>
    /// <param name="context">The live session the command acts on.</param>
    /// <param name="arguments">Everything the user typed after the verb, trimmed.</param>
    /// <param name="token">A token that cancels the command — what <kbd>Ctrl</kbd>+<kbd>Break</kbd> trips.</param>
    Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token);
}
