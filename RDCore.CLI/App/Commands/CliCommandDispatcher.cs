using RDCore.CLI.App.Messages;
using RDCore.CLI.App.Messages.Model;

namespace RDCore.CLI.App.Commands;

/// <summary>
/// Resolves a verb against every registered <see cref="ICliCommandProvider"/> and runs it.
/// </summary>
internal interface ICliCommandDispatcher
{
    Task<int> DispatchAsync(string verb, IReadOnlyList<string> args, CancellationToken token);
}

internal sealed class CliCommandDispatcher(IEnumerable<ICliCommandProvider> providers, IConsoleMessageWriter writer) : ICliCommandDispatcher
{
    public async Task<int> DispatchAsync(string verb, IReadOnlyList<string> args, CancellationToken token)
    {
        // provider order is registration order: the native provider is first and wins collisions.
        var commands = providers.SelectMany(provider => provider.GetCommands()).ToArray();

        var match = commands.FirstOrDefault(command =>
            string.Equals(command.Name, verb, StringComparison.OrdinalIgnoreCase)
            || command.Aliases.Any(alias => string.Equals(alias, verb, StringComparison.OrdinalIgnoreCase)));

        if (match is null)
        {
            WriteUnknownVerb(verb, commands);
            return 2;
        }

        try
        {
            return await match.ExecuteAsync(args, token);
        }
        catch (Exception exception)
        {
            writer.WriteException(exception);
            return 1;
        }
    }

    private void WriteUnknownVerb(string verb, IReadOnlyList<ICliCommand> commands)
    {
        writer.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Error)
            .WithTitle(Resources.Command_Unknown)
            .WithMessageBody(verb));

        var listing = string.Join(
            Environment.NewLine,
            commands
                .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                .Select(command => $"  {command.Name,-16} {command.Summary}"));

        writer.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Information)
            .WithTitle(Resources.Command_Available)
            .WithMessageBody(listing));
    }
}
