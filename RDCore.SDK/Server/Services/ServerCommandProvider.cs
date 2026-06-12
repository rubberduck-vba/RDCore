using Microsoft.Extensions.DependencyInjection;
using RDCore.SDK.Server.Commands;
using System.Data;
using System.Reflection;

namespace RDCore.SDK.Server.Services
{
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

    /// <summary>
    /// Uses reflection to discover all types derived from <c>ServerCommand</c> in the entry or executing assembly.
    /// </summary>
    public class ServerCommandProvider : IServerCommandProvider
    {
        private static readonly Lazy<Type[]> _serverCommandTypes = new(() =>
            [.. (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
                .GetTypes()
                .Where(t => t.IsAssignableFrom(typeof(ServerCommand)))
            ], LazyThreadSafetyMode.PublicationOnly);

        private readonly Lazy<Dictionary<string, ServerCommand>> _serverCommands;
        private readonly IServiceProvider provider;

        public ServerCommandProvider(IServiceProvider Provider)
        {
            provider = Provider;
            _serverCommands = new(() => _serverCommandTypes.Value
                .Select(commandType => provider.GetRequiredService(commandType))
                .OfType<ServerCommand>()
                .ToDictionary(e => e.Name, e => e));
        }

        public ServerCommand? GetCommand(string name) => _serverCommands.Value.TryGetValue(name, out var command) ? command : default;

        public string[] GetCommandRegistrations()
        {
            return [.. _serverCommands.Value.Keys];
        }
    }
}
