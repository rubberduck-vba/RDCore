using RDCore.SDK.Server.Commands;

namespace RDCore.SDK.Server.Services;

/// <summary>
/// A service that provides <em>server commands</em>.
/// </summary>
public interface IServerCommandProvider
{
    /// <summary>
    /// Gets the specified command if it exists.
    /// </summary>
    /// <param name="name">The registered command name.</param>
    /// <returns>
    /// <c>null</c> if no <c>ServerCommand</c> exists in this assembly with the specified name.
    /// </returns>
    ServerCommand? GetCommand(string name);
    /// <summary>
    /// Gets all the command registrations for this assembly.
    /// </summary>
    /// <returns>
    /// An array of command names used for registering the command capabilities during the initialization handshake.
    /// </returns>
    string[] GetCommandRegistrations();
}

public class ServerCommandProvider : IServerCommandProvider
{
    private readonly Dictionary<string, ServerCommand> _serverCommands;

    public ServerCommandProvider(IEnumerable<ServerCommand> commands)
    {
        _serverCommands = commands.ToDictionary(command => command.Name);
    }

    public ServerCommand? GetCommand(string name) => _serverCommands.TryGetValue(name, out var command) ? command : default;

    public string[] GetCommandRegistrations()
    {
        return [.. _serverCommands.Keys];
    }
}
