using RDCore.CLI.App.Configuration;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.CLI.App.Commands;

/// <summary>
/// <c>rdc config</c>: the shell's <c>CONFIG</c> in command mode (<see cref="ConfigCommand"/>) - what a user who cannot start the shell, or a script, sets and resets
/// the settings with, the platform not started.
/// </summary>
/// <param name="config">The verb.</param>
/// <param name="writer">Where what it says is written.</param>
internal sealed class ConfigCliCommand(ConfigCommand config, IConsoleMessageWriter writer) : ICliCommand
{
    public string Name => CommandNames.ConfigCommand.Name;

    public IReadOnlyList<string> Aliases => [CommandNames.ConfigCommand.Alias];

    public string Summary => Resources.Config_Summary;

    // a value is the rest of the line, as the shell reads it: `rdc config Platform.Extensions.Allowed A, B`.
    public Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var failed = false;
        foreach (var output in config.Run(string.Join(' ', args)))
        {
            if (output.Kind is { } kind)
            {
                failed |= kind == MessageKind.Error;
                var message = new ConsoleMessageBuilder().WithKind(kind).WithTitle(ConfigCommand.Name).WithMessageBody(output.Text);
                writer.WriteMessage(output.Detail is { Length: > 0 } detail ? message.WithVerbose(detail) : message);
            }
            else
            {
                writer.WriteLine(output.Text);
            }
        }

        return Task.FromResult(failed ? 1 : 0);
    }
}
