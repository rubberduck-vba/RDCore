using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>PEEK &lt;address&gt;</c>: reads the byte at an address in the runtime session's memory.
/// </summary>
internal sealed class PeekReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.Peek;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Peek_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (!MemoryAccessService.IsAvailable(context))
        {
            return ReplCommandResult.Continue;
        }

        if (MemoryAccessService.ParseArguments(arguments) is not [var address])
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Memory_BadArguments, arguments);
            return ReplCommandResult.Continue;
        }

        var result = await context.Platform.PeekAsync(address, token);
        if (!result.IsAllocated)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Memory_NotAllocated, MemoryAccessService.Format(address));
            return ReplCommandResult.Continue;
        }

        context.Console.WriteLine($" {result.Value} ");
        return ReplCommandResult.Continue;
    }
}
