using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>POKE &lt;address&gt;, &lt;value&gt;</c>: writes a byte at an address in the runtime session's
/// memory.
/// </summary>
/// <remarks>
/// Unchecked, exactly as its BASIC namesake is. The byte goes into whatever is there — including the
/// middle of a live variable, which then holds whatever the new bytes spell.
/// </remarks>
internal sealed class PokeReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.Poke;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Poke_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (!MemoryAccessService.IsAvailable(context))
        {
            return ReplCommandResult.Continue;
        }

        if (MemoryAccessService.ParseArguments(arguments) is not [var address, var value] || value is < 0 or > 255)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Memory_BadArguments, arguments);
            return ReplCommandResult.Continue;
        }

        var result = await context.Platform.PokeAsync(address, (byte)value, token);
        if (!result.IsWritten)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Memory_NotAllocated, MemoryAccessService.Format(address));
        }

        return ReplCommandResult.Continue;
    }
}
