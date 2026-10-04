using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.App.Repl.Commands;

internal record class ShowMemoryReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.Memory;
    public IReadOnlyList<string> Aliases => [];

    public string Summary => Resources.Repl_Memory_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        SessionStatusResult status;
        try
        {
            status = await context.Platform.GetSessionStatusAsync(10000, token);
        }
        catch (Exception exception)
        {
            context.Console.WriteMessage(SDK.ConsoleIO.Model.MessageKind.Warning, "The runtime session status could not be read.", exception.Message);
            status = new SessionStatusResult();
        }

        context.Console.WriteLine();
        context.Console.WriteLine(status.IsComposed
            ? string.Format(Resources.Repl_Memory,
                status.Memory.ReservedBytes, status.Memory.AvailableBytes,
                status.Memory.AllocatedBytes, status.Memory.FreeBytes)
            : Resources.Repl_NoSession);

        return ReplCommandResult.Continue;
    }
}
