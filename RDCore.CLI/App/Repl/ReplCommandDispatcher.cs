namespace RDCore.CLI.App.Repl;

/// <summary>
/// Resolves a shell verb to the command that implements it.
/// </summary>
public interface IReplCommandDispatcher
{
    /// <summary>Every registered command, in the order <c>HELP</c> should list them.</summary>
    IReadOnlyList<IReplCommand> Commands { get; }

    /// <summary>
    /// Whether <paramref name="name"/> names a command. This is what decides, for a line with no
    /// leading number, whether it is a command or VBA.
    /// </summary>
    /// <param name="name">The candidate verb, as typed.</param>
    bool IsCommandName(string name);

    /// <summary>
    /// Runs the command <paramref name="name"/> resolves to.
    /// </summary>
    /// <param name="context">The live session the command acts on.</param>
    /// <param name="name">The verb, as typed.</param>
    /// <param name="arguments">Everything after the verb.</param>
    /// <param name="token">A token that cancels the command.</param>
    /// <exception cref="KeyNotFoundException">No command answers to <paramref name="name"/>.</exception>
    Task<ReplCommandResult> DispatchAsync(ReplCommandContext context, string name, string arguments, CancellationToken token);
}

/// <inheritdoc cref="IReplCommandDispatcher"/>
/// <param name="commands">Every command the shell offers.</param>
public sealed class ReplCommandDispatcher(IEnumerable<IReplCommand> commands) : IReplCommandDispatcher
{
    // VBA identifiers are case-insensitive, and so is every BASIC that ever shipped: LIST, List and
    // list are one verb.
    private readonly Dictionary<string, IReplCommand> _byName = commands
        .SelectMany(command => command.Aliases.Prepend(command.Name).Select(name => (name, command)))
        .ToDictionary(entry => entry.name, entry => entry.command, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IReadOnlyList<IReplCommand> Commands { get; } = [.. commands.OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)];

    /// <inheritdoc/>
    public bool IsCommandName(string name) => _byName.ContainsKey(name);

    /// <inheritdoc/>
    public Task<ReplCommandResult> DispatchAsync(ReplCommandContext context, string name, string arguments, CancellationToken token)
        => _byName.TryGetValue(name, out var command)
            ? command.ExecuteAsync(context, arguments, token)
            : throw new KeyNotFoundException($"No shell command answers to '{name}'.");
}
