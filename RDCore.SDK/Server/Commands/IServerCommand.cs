using Newtonsoft.Json.Linq;

namespace RDCore.SDK.Server.Commands;

/// <summary>
/// A command a client asks a server to run (<strong>LSP 3.17</strong> §Execute a command): what a client cannot do itself, and no other request of the
/// protocol does - <c>rdcore.importProject</c> makes a <c>.rdproj</c> of a VB6 project.
/// </summary>
/// <remarks>
/// A server advertises the names of its commands (<c>executeCommandProvider.commands</c>), and a client routes those to it. A command the client runs
/// itself - <c>rdcore.rename</c>, which asks its user for a name - is none of them, and is not advertised: a client would route it to the server.
/// </remarks>
public interface IServerCommand
{
    /// <summary>
    /// The name a client asks for the command by: <c>rdcore.</c> and what it does.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Runs the command.
    /// </summary>
    /// <param name="arguments">The arguments the client sends, as it sends them.</param>
    /// <param name="context">What the command can ask of the client while it runs.</param>
    /// <param name="token">A token that cancels the command.</param>
    /// <returns>What the command answers the client with; <see langword="null"/> for nothing.</returns>
    /// <exception cref="ServerCommandRefusedException">The command was not run, for a reason the client's user is told.</exception>
    Task<JToken?> ExecuteAsync(JArray? arguments, IServerCommandContext context, CancellationToken token);
}

/// <summary>
/// What a server command can ask of the client while it runs.
/// </summary>
public interface IServerCommandContext
{
    /// <summary>
    /// Asks the client's user to choose (<strong>LSP 3.17</strong> <c>window/showMessageRequest</c>): whether to replace a file, which of several projects.
    /// </summary>
    /// <param name="message">The question, in the language of the user interface.</param>
    /// <param name="choices">What the user can choose, as the buttons say it.</param>
    /// <param name="token">A token that cancels the question.</param>
    /// <returns>The choice; <see langword="null"/> when the user chose none - closed the question, or has a client that asks no questions.</returns>
    Task<string?> AskAsync(string message, IReadOnlyList<string> choices, CancellationToken token);
}

/// <summary>
/// A command that was not run, for a reason the user of the client is told: answered as an error (<c>RequestFailed</c>) whose message is the reason.
/// </summary>
/// <param name="message">Why the command was not run, in the language of the user interface.</param>
public sealed class ServerCommandRefusedException(string message) : Exception(message);
